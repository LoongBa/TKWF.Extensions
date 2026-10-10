using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道工厂实现（多通道联邦 v0.3.0——Oracle M8 定案）。
/// <para>按 <see cref="ChannelConfig.ChannelId"/> 经 <see cref="IChannelRegistry"/> 定位配置 → 按
/// <paramref name="channelType"/> 从注入的 <see cref="ISsoChannel"/> 集合（ADR92 集合版守卫工厂——
/// 现状保留为<b>工厂内部索引源</b>，Oracle M2 定案）匹配模板类型 → <c>ActivatorUtilities.CreateInstance</c>
/// 构造带 channelId 的真实实例。</para>
/// <para><b>IDomainUser 供给（Oracle M8）</b>：本工厂继承 <see cref="DomainServiceBase"/>——<c>CreateAsync</c>
/// 帧内调用时基类 <c>User</c>（当前 AOP 用户）作为 IDomainUser 显式传入通道 ctor
/// （IDomainUser 永不注册 DI，ActivatorUtilities 无法从 DI 解析——经工厂帧内供给）。
/// 帧外（启动预热/后台）<c>User</c> 为空 → 构造失败 = 正确 fail。</para>
/// <para><c>[DiContractIgnore]</c>：运行时手写注册豁免 DI001（对齐通道/门面先例）。</para>
/// </summary>
[DiContractIgnore]
public sealed class SsoChannelFactory : DomainServiceBase, ISsoChannelFactory
{
    private readonly IServiceProvider _sp;
    private readonly IReadOnlyList<ISsoChannel> _channels;
    private readonly ILogger<SsoChannelFactory> _logger;
    private IChannelRegistry? _registry;

    /// <summary>构造——集合版守卫工厂帧内供给 ISsoChannel 集合（Oracle M2）；registry 经基类 User 懒加载（DI004）。</summary>
    public SsoChannelFactory(
        IDomainUser user,
        IServiceProvider sp,
        IEnumerable<ISsoChannel> channels,
        ILogger<SsoChannelFactory> logger)
        : base(user)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _channels = channels?.ToList() ?? throw new ArgumentNullException(nameof(channels));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ISsoChannel?> CreateAsync(string channelId, string? channelType = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
        {
            _logger.LogWarning("通道工厂构造拒绝：ChannelId 为空");
            return null;
        }

        var cfg = await Registry.GetAsync(channelId, ct);
        if (cfg == null)
        {
            _logger.LogDebug("通道工厂构造：CHANNEL_NOT_FOUND ChannelId={ChannelId}", channelId);
            return null;
        }
        if (!cfg.IsEnabled)
        {
            _logger.LogWarning("通道工厂构造：CHANNEL_DISABLED ChannelId={ChannelId}", channelId);
            return null;
        }

        // 从集合匹配目标通道模板（按 ChannelType 精确或 PlatformType 前缀推导）
        var targetType = channelType;
        var template = targetType is null
            ? _channels.FirstOrDefault(c => IsOAuthChannel(c.ChannelType, cfg.PlatformType))
            : _channels.FirstOrDefault(c => string.Equals(c.ChannelType, targetType, StringComparison.Ordinal));

        if (template is null)
        {
            _logger.LogWarning("通道工厂构造：平台库未注册通道类型 ChannelId={ChannelId} ChannelType={Type}",
                channelId, targetType ?? cfg.PlatformType);
            return null;
        }

        // Oracle M8 + 实现修订：帧内 User 供给 IDomainUser；ChannelConfig（POCO）显式传入——
        // 通道 ctor 契约：(IDomainUser, 平台依赖..., ChannelConfig? channel)。ActivatorUtilities 从 DI 解析其余依赖
        var instance = ActivatorUtilities.CreateInstance(_sp, template.GetType(), User, cfg);
        _logger.LogTrace("通道工厂构造成功：ChannelId={ChannelId} Type={Type}", channelId, template.GetType().Name);
        return instance as ISsoChannel;
    }

    /// <inheritdoc />
    public async Task<ISsoChannel?> CreateDefaultAsync(CancellationToken ct = default)
    {
        var cfg = await Registry.GetDefaultAsync(ct);
        return cfg is null ? null : await CreateAsync(cfg.ChannelId, null, ct);
    }

    private static bool IsOAuthChannel(string channelType, string platformType)
        => channelType.StartsWith(platformType, StringComparison.OrdinalIgnoreCase)
           && (channelType.EndsWith("_oauth", StringComparison.Ordinal) || channelType.EndsWith("_oidc", StringComparison.Ordinal));

    private IChannelRegistry Registry => _registry ??= User.Use<IChannelRegistry>();
}