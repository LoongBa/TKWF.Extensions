using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 外部 IdP 借道验证桥接（三层边界 2026-10-09，方案 §5.4 T5）——AuthCenter 内网经
/// <see cref="IExternalIdpAuthenticator"/> 契约借道 Federation 连接层完成外部身份认证。
/// <para><b>委托非双实现</b>：本桥接<b>不实现平台协议</b>——按方法参数 <c>channelType</c> 经
/// <see cref="ISsoChannelFactory"/> 选区构造平台库通道（<see cref="ISsoChannel"/>）→
/// <c>AuthenticateAsync</c> 委托认证（协议归平台网关库单源）。消除 AuthCenter 内微信 Provider
/// 双实现根因（根因修复，方案 §5.4）。</para>
/// <para>选区语义（对齐 <see cref="SsoLogin"/>）：parameters 内 <c>channel_id</c> 非空 → 精确选区
/// （<c>Factory.CreateAsync(channelId, channelType)</c>）；缺省 → 默认通道
/// （<c>Factory.CreateDefaultAsync</c>——无前缀降级）。选区失败 → <c>CHANNEL_NOT_FOUND</c>；
/// registry/工厂故障 → <c>CHANNEL_REGISTRY_UNAVAILABLE</c>（可观测性，不抛不出帧）。</para>
/// <para>fail-hard（P7）：未装配 Federation → 本实现未注册 → 消费方 <c>User.Use&lt;IExternalIdpAuthenticator&gt;()</c>
/// 抛守卫（不静默降级）。</para>
/// <para><c>[DiContractIgnore]</c>：运行时手写注册豁免 DI001（对齐 SsoLogin 先例）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class ExternalIdpAuthenticator : DomainServiceBase, IExternalIdpAuthenticator
{
    private const string ChannelIdKey = "channel_id";
    private const string ChannelNotFound = "CHANNEL_NOT_FOUND";
    private const string RegistryUnavailable = "CHANNEL_REGISTRY_UNAVAILABLE";

    private readonly ILogger<ExternalIdpAuthenticator> _logger;
    private ISsoChannelFactory? _factory;

    /// <summary>构造（IDomainUser 经守卫工厂帧内供给；非域基础设施可构造注入——工厂经基类 User 懒加载，DI004 零豁免）。</summary>
    public ExternalIdpAuthenticator(IDomainUser user, ILogger<ExternalIdpAuthenticator> logger)
        : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<ExternalIdpAuthResult> AuthenticateAsync(string channelType, IReadOnlyDictionary<string, string?> parameters, CancellationToken ct = default)
    {
        try
        {
            // B 守卫（Oracle 评审条件 1——对外路由命名空间方案）：多通道（活跃数 >1）+ 缺省 channel_id →
            // CHANNEL_REQUIRED 硬失败（防 GetDefaultAsync IsDefault→首项 非确定性静默选区）；
            // 单通道（活跃数 ==1）→ 降级无歧义。守卫在消费者层（CreateDefaultAsync null 语义已占用）。
            if (!parameters.TryGetValue(ChannelIdKey, out var channelId) || string.IsNullOrWhiteSpace(channelId))
            {
                if ((await Registry.GetAllAsync(ct)).Count(c => c.IsEnabled) > 1)
                {
                    _logger.LogWarning("外部 IdP 借道认证守卫：多通道部署须显式 channel_id（CHANNEL_REQUIRED）");
                    return new ExternalIdpAuthResult(false, null, "CHANNEL_REQUIRED", 0);
                }
            }

            var channel = await ResolveChannelAsync(channelType, parameters, ct);
            if (channel is null)
            {
                _logger.LogWarning("外部 IdP 借道认证选区失败：ChannelType={ChannelType}（CHANNEL_NOT_FOUND）", channelType);
                return new ExternalIdpAuthResult(false, null, ChannelNotFound, 0);
            }

            // 委托平台库通道认证（协议单源——桥接不双实现；parameters 原样进上下文）
            var result = await channel.AuthenticateAsync(new SsoChannelAuthContext(parameters), ct);
            return new ExternalIdpAuthResult(result.Success, result.ExternalUserId, result.FailReason, result.AuthLevel);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // registry/工厂故障——与业务 NOT_FOUND 区分（可观测性：CHANNEL_REGISTRY_UNAVAILABLE，对齐 SsoLogin）
            _logger.LogError(ex, "外部 IdP 借道认证异常：ChannelType={ChannelType}（CHANNEL_REGISTRY_UNAVAILABLE）", channelType);
            return new ExternalIdpAuthResult(false, null, RegistryUnavailable, 0);
        }
    }

    private async Task<ISsoChannel?> ResolveChannelAsync(string channelType, IReadOnlyDictionary<string, string?> parameters, CancellationToken ct)
    {
        // 显式 channel_id 优先（精确选区）→ 缺省降级默认通道（对齐 SsoLogin.LoginDefaultAsync 语义；B 守卫已在 AuthenticateAsync 前置）
        if (parameters.TryGetValue(ChannelIdKey, out var channelId) && !string.IsNullOrWhiteSpace(channelId))
            return await Factory.CreateAsync(channelId, channelType, ct);
        return await Factory.CreateDefaultAsync(ct);
    }

    private ISsoChannelFactory Factory => _factory ??= User.Use<ISsoChannelFactory>();
    private IChannelRegistry Registry => _registry ??= User.Use<IChannelRegistry>();

    private IChannelRegistry? _registry;
}
