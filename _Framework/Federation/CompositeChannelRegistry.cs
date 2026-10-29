using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道注册表组合门面（D03 §4.2.3 供给层——1.7 改造：按 <see cref="IChannelSource.Priority"/> 降序选区 +
/// 内存缓存三键 + 三机制失效，替代硬编码「DB 命中优先 → 静态回退」）。
/// <para><b>组合语义（Oracle6 条件 5 定稿）</b>：遍历 <see cref="IChannelSource"/> 集合（普通 DI 集合——
/// 静态源（平台库 <c>AddXxxFederationChannels()</c> 注册，Priority 默认 0）+ DB 源（WeChatOperations
/// <c>WeChatChannelRegistrySource</c>，Priority=100）），按 Priority <b>降序首命中返回</b>
/// （首个返回非 null 的源即终值——高优先源的 alias 覆盖低优先源）；未装配源自然跳过（空集合 → null/空）。
/// ⚠️ Federation 主包 <c>SsoChannelRegistryEntity</c> 系（<see cref="DbChannelRegistry"/>）<b>停用新装配</b>
/// （D03 A9 权威归 WeChatOperations——旧装配保留兼容、文档标注废弃，彻底删除留后续清理迭代）。</para>
/// <para><b>缓存层（三机制）</b>：内容 = 解密后明文 <see cref="ChannelConfig"/>（进程内信任边界，不落盘不进日志）；
/// 键 = <c>ch:{ChannelId}</c> + <c>ch:all</c> + <c>ch:default</c>（方案 §8.2）；TTL =
/// <see cref="FederationChannelRegistryOptions.CacheTtlSeconds"/>（默认 120）。失效三机制：
/// ① 显式（<c>InvalidateAsync(channelId?)</c>——管理端写后调用，channelId 空 = 全量）；
/// ② 事件（<c>ChannelConfigChanged</c> 域事件——WeChatOperations DB 写路径落库后发布，本实现订阅失效命中通道）；
/// ③ TTL 兜底（缓存项绝对过期）。</para>
/// <para>职责边界：只做"组合 + 缓存"不读数据——数据经 <see cref="IChannelSource"/> 集合下发钻
/// （<c>GetChannels()</c> 同步契约；DB 源内部经 DataService 同步阻塞，调用点恒在用户帧内——无死锁）。
/// 单来源读取异常 → Warning + 跳过该来源（降级矩阵惯例：DB 不可用 → 静态源仍可选区）。</para>
/// <para>注册：<c>AddConstructibleService&lt;IChannelRegistry, CompositeChannelRegistry&gt;()</c> 经
/// FederationInitializer——消费方统一 <c>User.Use&lt;IChannelRegistry&gt;()</c> 解析（门面标准形态）。</para>
/// </summary>
[DiContractIgnore]
public sealed class CompositeChannelRegistry : DomainServiceBase, IChannelRegistry
{
    private readonly IEnumerable<IChannelSource> _sources;
    private readonly IMemoryCache _cache;
    private readonly int _ttlSeconds;
    private readonly ILogger<CompositeChannelRegistry> _logger;
    private IReadOnlyList<IChannelSource>? _ordered;

    /// <summary>构造——IDomainUser 帧内供给；IChannelSource 集合普通 DI 注入（静态 + DB 源）；IMemoryCache 单例共享。</summary>
    public CompositeChannelRegistry(
        IDomainUser user,
        IEnumerable<IChannelSource> sources,
        IMemoryCache cache,
        IOptions<FederationChannelRegistryOptions> options,
        ILogger<CompositeChannelRegistry> logger)
        : base(user)
    {
        _sources = sources ?? throw new ArgumentNullException(nameof(sources));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        var o = options?.Value ?? throw new ArgumentNullException(nameof(options));
        _ttlSeconds = o.CacheTtlSeconds > 0 ? o.CacheTtlSeconds : 120;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    /// <remarks>键 <c>ch:{ChannelId}</c>；未命中 → 按 Priority 降序首命中返回（不缓存 null——不存在记录不入缓存）。</remarks>
    public Task<ChannelConfig?> GetAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return Task.FromResult<ChannelConfig?>(null);

        var key = ChannelCacheKeys.Channel(channelId);
        if (_cache.TryGetValue(key, out ChannelConfig? cached) && cached != null)
            return Task.FromResult(cached);

        var cfg = FirstMatch(src => src.ChannelId == channelId);
        if (cfg != null)
            CacheSet(key, cfg);
        return Task.FromResult(cfg);
    }

    /// <inheritdoc />
    /// <remarks>走 <c>ch:all</c> 键缓存（全量枚举后内存内别名/ChannelId 双遍匹配——避免 alias 单键缓存爆炸）；
    /// 未中下钻重建全量缓存。</remarks>
    public Task<ChannelConfig?> GetByAliasOrIdAsync(string key, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(key))
            return Task.FromResult<ChannelConfig?>(null);

        var all = GetAllCached();
        var cfg = all.FirstOrDefault(c => !string.IsNullOrWhiteSpace(c.Alias) && string.Equals(c.Alias, key, StringComparison.Ordinal))
                  ?? all.FirstOrDefault(c => string.Equals(c.ChannelId, key, StringComparison.Ordinal));
        return Task.FromResult(cfg);
    }

    /// <inheritdoc />
    /// <remarks>键 <c>ch:all</c>；按 Priority 降序合并去重（同 ChannelId 高优先源胜出——低优先源同 id 跳过）。</remarks>
    public Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken ct = default)
        => Task.FromResult(GetAllCached());

    /// <inheritdoc />
    /// <remarks>键 <c>ch:default</c>；选区 = 按 Priority 降序首 IsDefault 命中 → 无标记回退高优先源首项
    /// （对齐方案 §3.2 默认选区边界：显式声明 IsDefault 优先，无声明则高优先源 Channels 首项）。</remarks>
    public Task<ChannelConfig?> GetDefaultAsync(CancellationToken ct = default)
    {
        var key = ChannelCacheKeys.Default;
        if (_cache.TryGetValue(key, out ChannelConfig? cached) && cached != null)
            return Task.FromResult(cached);

        var cfg = FirstMatch(src => src.IsDefault) ?? FirstMatch(_ => true);
        if (cfg != null)
            CacheSet(key, cfg);
        return Task.FromResult(cfg);
    }

    /// <inheritdoc />
    /// <remarks>① 显式失效（本实现）——channelId 空 = 全量（all/default 键；单通道键无全量枚举无法清，
    /// 全量语义 = 清两聚合键——单通道缓存项由 TTL 自然过期兜底）；非空 = 清单键 + all + default
    /// （all 聚合含该通道——保守全清重算，Count 级代价可忽略）。② 事件失效经 <see cref="ChannelConfigChangedHandler"/>
    /// （[DomainEventHandler] SG4 消费方编译期自动注册——见 handler 文件注释）。</remarks>
    public Task InvalidateAsync(string? channelId = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            _cache.Remove(ChannelCacheKeys.All);
            _cache.Remove(ChannelCacheKeys.Default);
        }
        else
        {
            _cache.Remove(ChannelCacheKeys.Channel(channelId));
            _cache.Remove(ChannelCacheKeys.All);
            _cache.Remove(ChannelCacheKeys.Default);
        }
        return Task.CompletedTask;
    }

    private IReadOnlyList<IChannelSource> Ordered => _ordered ??= _sources
        .OrderByDescending(s => s.Priority)   // 数值高者优先（稳定排序——同 Priority 保持注册序）
        .ToArray();

    private ChannelConfig? FirstMatch(Func<ChannelConfig, bool> predicate)
    {
        foreach (var source in Ordered)
        {
            try
            {
                foreach (var cfg in source.GetChannels())
                {
                    if (predicate(cfg))
                        return cfg;
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "通道来源读取失败：PlatformType={PlatformType}（跳过该来源——降级矩阵）",
                    source.PlatformType);
            }
        }
        return null;
    }

    private IReadOnlyList<ChannelConfig> GetAllCached()
    {
        if (_cache.TryGetValue(ChannelCacheKeys.All, out IReadOnlyList<ChannelConfig>? cached) && cached != null)
            return cached;

        var merged = new List<ChannelConfig>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in Ordered)
        {
            try
            {
                foreach (var cfg in source.GetChannels())
                {
                    // 高优先源胜出——同 ChannelId 仅首见（高优先序在前）入列
                    if (!string.IsNullOrEmpty(cfg.ChannelId) && seen.Add(cfg.ChannelId))
                        merged.Add(cfg);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "通道来源读取失败：PlatformType={PlatformType}（跳过该来源——降级矩阵）",
                    source.PlatformType);
            }
        }

        var result = (IReadOnlyList<ChannelConfig>)merged;
        CacheSet(ChannelCacheKeys.All, result);
        return result;
    }

    private void CacheSet(string key, object value)
        => _cache.Set(key, value, TimeSpan.FromSeconds(_ttlSeconds));
}