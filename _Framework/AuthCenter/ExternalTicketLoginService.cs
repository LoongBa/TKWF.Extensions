using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Ticket;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部 IdP 票据 SPA 兑现门面实现（V0.10.0——事件取 OpenId 链路"票 → token1"前端兑换面）。
/// <para>编排：频控（P2-4 强制——<see cref="IRateLimitCheck"/> key <c>ext:ticket:{ip}</c>）→
/// <see cref="ISealedTicketService{T}"/> 验票（P1-3：null 统一 <c>TICKET_INVALID</c>，不泄露判因）→
/// 取 <see cref="FederatedIdentity"/>（<c>channelId</c> 兜底 identity.ChannelId 为空——
/// 正常由事件链签入）→ 委托 <see cref="IFederatedLoginOrchestrator"/>（P2-2 共享编排）。</para>
/// <para>注册：<c>AddConstructibleService&lt;IExternalTicketLoginService, ExternalTicketLoginService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。
/// P2-5（fail-closed 边缘）：票消费（CAS）先于建号，建号失败 → 票已消费无会话——<c>LogError</c> 记录
/// 补偿信息（ChannelId/ExternalUserId/票前 8 位 hash），客户端重试取新票（见使用指南 §边缘）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class ExternalTicketLoginService : DomainServiceBase, IExternalTicketLoginService
{
    private const string TicketInvalid = "TICKET_INVALID";

    private readonly IOptions<AuthCenterOptions> _options;
    private readonly IRateLimitCheck _rateLimit;
    private readonly ILogger<ExternalTicketLoginService> _logger;

    // DI004 铁律：领域服务经 User.Use<T>() 懒加载（守卫工厂帧内解析——验票/编排全为守卫工厂门面）
    private ISealedTicketService<FederatedIdentity>? _ticketService;
    private ISealedTicketService<FederatedIdentity> TicketService => _ticketService ??= User.Use<ISealedTicketService<FederatedIdentity>>();
    private IFederatedLoginOrchestrator? _orchestrator;
    private IFederatedLoginOrchestrator Orchestrator => _orchestrator ??= User.Use<IFederatedLoginOrchestrator>();

    public ExternalTicketLoginService(
        IDomainUser user,
        IOptions<AuthCenterOptions> options,
        IRateLimitCheck rateLimit,
        ILogger<ExternalTicketLoginService> logger)
        : base(user)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rateLimit = rateLimit ?? throw new ArgumentNullException(nameof(rateLimit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<LoginResult> LoginAsync(string ticket, string? channelId, string? ipAddress, CancellationToken ct = default)
    {
        // ① 频控（P2-4 强制——IP 维度，纵深防御；票 32B 高熵不可枚举但防暴力试探）
        var protection = _options.Value.LoginProtection;
        var ipKey = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress;
        if (!_rateLimit.TryAcquire($"ext:ticket:{ipKey}", protection.TicketRedeemPerMinutePerIp, TimeSpan.FromMinutes(1), out _))
            throw new AuthenticationException("TICKET_RATE_LIMITED");

        // ② 验票（P1-3 统一判因：RedeemAsync 失败/过期/已用/篡改/核销人不符恒返回 null——不泄露判因）
        var identity = await TicketService.RedeemAsync(ticket, context: null, ct);
        if (identity is null)
        {
            _logger.LogWarning("票据 SPA 兑现失败——RedeemAsync 返回 null（票无效/过期/已用/claimant 不符——统一 TICKET_INVALID）");
            return new LoginResult(false, null, null, TicketInvalid);
        }

        // channel_id 兜底（identity.ChannelId 正常由事件链签入；为空时用可选参消歧；均空 → 无法建映射 → 拒）
        var effectiveChannelId = string.IsNullOrWhiteSpace(identity.ChannelId) ? channelId : identity.ChannelId;
        if (string.IsNullOrWhiteSpace(effectiveChannelId))
        {
            _logger.LogWarning("票据 SPA 兑现失败——票载荷 ChannelId 缺失且未提供 channel_id（无法建立联邦映射）");
            return new LoginResult(false, null, null, TicketInvalid);
        }
        if (!string.IsNullOrWhiteSpace(channelId)
            && !string.Equals(effectiveChannelId, channelId, StringComparison.Ordinal))
        {
            _logger.LogWarning("票据 SPA 兑现失败——channel_id 与票载荷 ChannelId 不一致（载荷={LoadChannelId}, 参数={ReqChannelId}）",
                identity.ChannelId, channelId);
            return new LoginResult(false, null, null, TicketInvalid);
        }
        if (string.IsNullOrWhiteSpace(identity.ChannelId))
        {
            // P2-2 审核收敛：票载荷 ChannelId 缺失靠 body 兜底成功——事件链签发契约异常，运维侧需观测
            _logger.LogWarning("票据 SPA 兑现——票载荷 ChannelId 缺失，兜底使用 body channel_id（事件链 EventReplyHandlerBase 签发契约异常，请排查）");
        }
        var effectiveIdentity = identity with { ChannelId = effectiveChannelId };

        // ③ 委托共享编排（P2-2——身份回读/建号/回读守卫/签 token1）；P2-5：编排失败（建号异常）→
        // 票已消费无会话——LogError 记录补偿信息，异常向表现层传播（fail-closed，不 catch）
        try
        {
            return await Orchestrator.LoginAsync(effectiveIdentity, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException and not AuthenticationException)
        {
            _logger.LogError(ex,
                "票据 SPA 兑现编排失败——票已消费但无会话（补偿信息：ChannelId={ChannelId}, ExternalUserId={ExternalUserId}, TicketPrefix={TicketPrefix}）",
                effectiveIdentity.ChannelId, effectiveIdentity.ExternalUserId, TicketPrefix(ticket));
            throw;
        }
    }

    /// <summary>票标识前缀（补偿日志用——不落全票：ticket 高熵机密，仅前 8 字符协助运维反查）。</summary>
    private static string TicketPrefix(string ticket)
        => string.IsNullOrEmpty(ticket) ? "?" : (ticket.Length <= 8 ? ticket : ticket[..8]);
}