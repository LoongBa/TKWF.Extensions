using System;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Events;

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道配置缓存键规范（D03 §8.2——Composite 与事件失效 handler 共享，防键格式漂移）。
/// <para>键 = <c>ch:{ChannelId}</c> + <c>ch:all</c> + <c>ch:default</c>（方案 §8.2 定稿）。</para>
/// </summary>
internal static class ChannelCacheKeys
{
    /// <summary>单通道缓存键（ch:{ChannelId}）。</summary>
    public static string Channel(string channelId) => "ch:" + channelId;

    /// <summary>全量列表缓存键（ch:all）。</summary>
    public const string All = "ch:all";

    /// <summary>默认通道缓存键（ch:default）。</summary>
    public const string Default = "ch:default";
}

/// <summary>
/// 通道配置变更域事件失效 handler（D03 §4.2.3 三机制之一「事件失效」）——WeChatOperations DB 写路径
/// 落库后发布 <see cref="ChannelConfigChanged"/> → 本 handler 失效 <see cref="ChannelCacheKeys"/> 对应缓存项。
/// <para><b>必须 public sealed（对齐 <c>DistributedFeatureChangedHandler</c> 先例）</b>：SG4 在消费方编译时经
/// <c>ReferencedAssemblySymbols</c> 扫描 <see cref="DomainEventHandlerAttribute"/> 生成 <c>typeof(Handler)</c>
/// 引用并自动注册（Transient）——internal 类型在消费方无 InternalsVisibleTo 时 CS0122 编译失败；
/// 扩展 <see cref="FederationExtensionInitializer{TUserInfo}"/> <b>不手动注册</b>（SG4 消费方编译期自动接线）。</para>
/// <para>ctr 注入 <see cref="IMemoryCache"/> 单例（Composite 同源缓存——失效即命中）；
/// 版本防线：按 ChannelId 隔离可见最大 <c>DataVersion</c>（<see cref="ChannelConfigChanged.DataVersion"/>——
/// 乱序旧事件忽略，Obs5；null ChannelId = 全量事件不比对版本直接全量失效）。</para>
/// </summary>
[DomainEventHandler]
public sealed class ChannelConfigChangedHandler : ILocalEventHandler<ChannelConfigChanged>
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<ChannelConfigChangedHandler> _logger;

    // 进程级版本表：handler 生命周期由 SG4 决定（Transient）——实例字段会使乱序防线恒空失效
    private static readonly ConcurrentDictionary<string, long> DataVersions = new(StringComparer.Ordinal);

    public ChannelConfigChangedHandler(IMemoryCache cache, ILogger<ChannelConfigChangedHandler> logger)
    {
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task HandleEventAsync(ChannelConfigChanged eventData)
    {
        if (eventData is null) return Task.CompletedTask;

        // null ChannelId = 全量变更（同 InvalidateAsync(null) 语义）——不比对版本直接全量失效
        if (eventData.ChannelId is null)
        {
            InvalidateAll();
            return Task.CompletedTask;
        }

        // 乱序防线（Obs5）：按 ChannelId 隔离——严格小于已见才忽略（等值/并发由原子 Max 更新收敛）
        var lastSeen = DataVersions.AddOrUpdate(
            eventData.ChannelId,
            eventData.DataVersion,
            (_, existing) => Math.Max(existing, eventData.DataVersion));

        if (eventData.DataVersion < lastSeen)
        {
            _logger.LogDebug("ChannelConfigChanged 乱序旧事件忽略：ChannelId={ChannelId} DataVersion={DataVersion}（已见 {Last}）",
                eventData.ChannelId, eventData.DataVersion, lastSeen);
            return Task.CompletedTask;
        }

        _cache.Remove(ChannelCacheKeys.Channel(eventData.ChannelId));
        _cache.Remove(ChannelCacheKeys.All);        // 全量聚合含该通道——保守清理重算（Count 级代价可忽略）
        _cache.Remove(ChannelCacheKeys.Default);    // 默认选区可能涉及该通道
        return Task.CompletedTask;
    }

    private void InvalidateAll()
    {
        _cache.Remove(ChannelCacheKeys.All);
        _cache.Remove(ChannelCacheKeys.Default);
    }
}