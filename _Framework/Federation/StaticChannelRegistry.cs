using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.Federation;

/// <summary>
/// 静态通道注册表实现（多通道联邦 v0.3.0 Phase 1——来源 = 各平台库 <c>XxxOptions.Channels</c> 投影）。
/// <para>聚合全部 <see cref="IChannelSource"/>（普通 DI 集合），按 <see cref="ChannelConfig.ChannelId"/> 精确定位
/// → 消解平台库 14 处硬编码 <c>Channels.FirstOrDefault()</c>（方案 §3.4）。纯内存无异步 IO——
/// <c>GetAsync</c> 实现返回 <c>Task.FromResult</c>（接口统一异步签名，不另设同步变体——Oracle M1 定案）。</para>
/// <para>注册：<c>AddConstructibleService&lt;IChannelRegistry, StaticChannelRegistry&gt;()</c> 经
/// <see cref="CompositeChannelRegistry"/> 暴露（组合语义单点预留——P1 内部仅本实现，P2 换 Db 注入零返工）。
/// 归属 Federation 域（Oracle P2-4 凭证自持边界——不挤 AuthCenter IPlatformCredentialService）。</para>
/// <para><b>默认选区语义（方案 §3.2 边界）</b>：<see cref="GetDefaultAsync"/> 选区"首个 IsDefault 标记
/// → 无标记则 Channels 首项"——单实例向后兼容（现状 Channels[0] 行为）；多公众号场景不承诺业务合理默认
/// （装配层显式决策 channelId）。</para>
/// </summary>
public sealed class StaticChannelRegistry : IChannelRegistry
{
    private readonly IReadOnlyList<IChannelSource> _sources;
    private readonly ILogger<StaticChannelRegistry> _logger;
    private readonly IOptions<FederationStaticChannelOptions> _options;

    /// <summary>构造——聚合平台库来源（普通 DI 注入，非守卫工厂）。</summary>
    public StaticChannelRegistry(
        IEnumerable<IChannelSource> sources,
        IOptions<FederationStaticChannelOptions> options,
        ILogger<StaticChannelRegistry> logger)
    {
        _sources = sources?.ToList() ?? throw new ArgumentNullException(nameof(sources));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public Task<ChannelConfig?> GetAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return Task.FromResult<ChannelConfig?>(null);

        foreach (var source in _sources)
        {
            foreach (var cfg in source.GetChannels())
            {
                if (string.Equals(cfg.ChannelId, channelId, StringComparison.Ordinal))
                    return Task.FromResult<ChannelConfig?>(cfg);
            }
        }

        LogNotFound(channelId);
        return Task.FromResult<ChannelConfig?>(null);
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken ct = default)
    {
        var all = new List<ChannelConfig>();
        foreach (var source in _sources)
        {
            try
            {
                all.AddRange(source.GetChannels());
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // 单平台来源读取失败不阻断整体枚举（对齐 UserCenter 降级矩阵——Warning + 跳过）
                _logger.LogWarning(ex, "通道来源读取失败：PlatformType={PlatformType}（跳过该来源）", source.PlatformType);
            }
        }
        return Task.FromResult<IReadOnlyList<ChannelConfig>>(all);
    }

    /// <inheritdoc />
    public Task<ChannelConfig?> GetDefaultAsync(CancellationToken ct = default)
    {
        // 默认选区：显式声明 IsDefault 的通道优先；无声明则各平台来源首项（兼容单实例 Channels[0] 语义）
        foreach (var source in _sources)
        {
            foreach (var cfg in source.GetChannels())
            {
                if (cfg.IsDefault)
                    return Task.FromResult<ChannelConfig?>(cfg);
            }
        }
        foreach (var source in _sources)
        {
            var first = source.GetChannels().FirstOrDefault();
            if (first != null)
                return Task.FromResult<ChannelConfig?>(first);
        }
        return Task.FromResult<ChannelConfig?>(null);
    }

    private void LogNotFound(string channelId)
        => _logger.LogDebug("通道未找到：ChannelId={ChannelId}（CHANNEL_NOT_FOUND）", channelId);
}

/// <summary>
/// 多通道联邦静态配置（<c>TKWF:Federation:Channels</c> 节——可选公共设置）。
/// <para><c>[Options]</c> 声明——SG1 消费方自动绑定；本类为 registry 的<u>非平台类</u>配置（跨平台通用），
/// 平台凭证仍归各平台库 <c>TKWF:Federation:{Platform}</c> 节（Oracle P2-4 凭证自持）。</para>
/// </summary>
public class FederationStaticChannelOptions
{
    /// <summary>默认通道选区策略（static=无 IsDefault 标记时取首项——现状兼容；<c>null</c> 同 static）。</summary>
    public string? DefaultSelection { get; set; } = "static";
}