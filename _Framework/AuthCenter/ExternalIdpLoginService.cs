using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部 IdP 登录编排门面实现（T5 三层边界 2026-10-09——桥接消费门面，替代已删 WechatLoginService）。
/// <para>门面内完成 桥接认证（<see cref="IExternalIdpAuthenticator"/>）→ 构建 <see cref="FederatedIdentity"/> →
/// 委托共享编排 <see cref="IFederatedLoginOrchestrator"/>（V0.10.0 P2-2——identity → 账号回读/建号 → 签 token1
/// 全编排收敛共享，端点只调一个门面，不串多门面，Oracle P0-1 既定原则保持）；B3b 返回
/// <see cref="TokenResponse"/>，失败抛类型化异常（业务 <see cref="DomainException"/> 400 / 未装配
/// <see cref="ServiceNotConfiguredException"/> 503）。</para>
/// <para><b>fail-hard（P7→B3b）</b>：<see cref="IExternalIdpAuthenticator"/> 由 Federation 注册（守卫工厂）；
/// 未装配 Federation → <c>User.Use&lt;IExternalIdpAuthenticator&gt;()</c> 抛守卫异常——门面内捕获转
/// <see cref="ServiceNotConfiguredException"/>（503 EXTERNAL_IDP_NOT_CONFIGURED，显式告知而非静默降级）。</para>
/// <para>DI004 铁律：领域服务间调用经基类 <c>User.Use&lt;T&gt;()</c> 懒加载（禁构造注入——桥接/编排
/// 均为守卫工厂门面，帧内解析）。注册：<c>AddConstructibleService&lt;IExternalIdpLoginService, ExternalIdpLoginService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
[GenerateController]
public sealed class ExternalIdpLoginService : DomainServiceBase, IExternalIdpLoginService
{
    private const string ChannelIdKey = "channel_id";
    private const string ExternalAuthFailed = "EXTERNAL_AUTH_FAILED";
    private const string ExternalUserIdMissing = "EXTERNAL_USER_ID_MISSING";
    private const string ChannelIdRequired = "EXTERNAL_CHANNEL_ID_REQUIRED";

    private readonly ILogger<ExternalIdpLoginService> _logger;

    // DI004 铁律：领域服务经 User.Use<T>() 懒加载（守卫工厂帧内解析——桥接/编排全为守卫工厂门面）
    private IExternalIdpAuthenticator? _externalAuth;
    private IExternalIdpAuthenticator ExternalAuth => _externalAuth ??= User.Use<IExternalIdpAuthenticator>();
    private IFederatedLoginOrchestrator? _orchestrator;
    private IFederatedLoginOrchestrator Orchestrator => _orchestrator ??= User.Use<IFederatedLoginOrchestrator>();

    public ExternalIdpLoginService(IDomainUser user, ILogger<ExternalIdpLoginService> logger) : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<TokenResponse> LoginAsync(string channelType, IReadOnlyDictionary<string, string?> parameters, CancellationToken ct = default)
    {
        try
        {
            // ① 桥接认证（fail-hard——Federation 未装配 → User.Use<IExternalIdpAuthenticator>() 抛守卫，下方捕获转 503）
            var auth = await ExternalAuth.AuthenticateAsync(channelType, parameters, ct);
            if (!auth.Success)
            {
                _logger.LogDebug("外部 IdP 登录认证失败——{FailReason}（ChannelType={ChannelType}）", auth.FailReason, channelType);
                throw new DomainException(auth.FailReason ?? ExternalAuthFailed) { ErrorCode = auth.FailReason ?? ExternalAuthFailed };
            }
            if (string.IsNullOrEmpty(auth.ExternalUserId))
            {
                _logger.LogWarning("外部 IdP 登录编排失败——认证成功但外部用户标识缺失（ChannelType={ChannelType}）", channelType);
                throw new DomainException(ExternalUserIdMissing) { ErrorCode = ExternalUserIdMissing };
            }

            // ② channelId 从 parameters["channel_id"]（缺失 → 拒绝——映射键必需）
            if (!parameters.TryGetValue(ChannelIdKey, out var channelId) || string.IsNullOrWhiteSpace(channelId))
            {
                _logger.LogWarning("外部 IdP 登录编排失败——parameters 缺 channel_id（ChannelType={ChannelType}）", channelType);
                throw new DomainException(ChannelIdRequired) { ErrorCode = ChannelIdRequired };
            }

            // ③ 委托共享编排（P2-2）：构建 FederatedIdentity（UId 恒空——认证结果无平台内部 id，走映射/建号路径）
            //    → IFederatedLoginOrchestrator（映射命中复用 / 建号 + LinkAsync / 回读守卫 / 签 token1）→ TokenResponse
            //    ⚠️ 本路径**不经票据**（code 通道直达编排）——载荷类型保持框架通用 FederatedIdentity（D03 A7/P2：
            //    编排签名吃 FederatedIdentity；票据链 ExternalTicketLoginService 兑票得 FederatedTicketPayload
            //    后经 ToFederatedIdentity() 适配再进编排——两通道在编排口收敛，语义归一）
            var identity = new FederatedIdentity(
                UId: string.Empty,
                ChannelId: channelId,
                ExternalUserId: auth.ExternalUserId,
                ChannelType: channelType,
                AuthLevel: auth.AuthLevel);
            return await Orchestrator.LoginAsync(identity, ct);
        }
        catch (InvalidOperationException ex) when (IsDomainGuardException(ex))
        {
            // fail-hard（P7→B3b）：Federation 未装配 → Use<IExternalIdpAuthenticator>() 抛守卫 → 503 显式告知
            throw new ServiceNotConfiguredException("external-idp", ex.Message, ex) { ErrorCode = "EXTERNAL_IDP_NOT_CONFIGURED" };
        }
    }

    /// <summary>领域架构守卫异常判定——<c>User.Use&lt;T&gt;()</c> 帧外/未装配守卫工厂
    /// （AddConstructibleService 实现类 throw-factory / 接口守卫工厂）抛 <c>InvalidOperationException</c> + "领域架构守卫" 前缀。</summary>
    private static bool IsDomainGuardException(InvalidOperationException ex)
        => ex.Message.Contains("领域架构守卫", StringComparison.Ordinal);
}
