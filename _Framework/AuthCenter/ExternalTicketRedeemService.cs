using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Ticket;
using TKW.Framework.Utility.RateLimitChecks;
using TKWF.Ext.Federation;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部 IdP 业务兑票门面实现（V0.14.0——DMP ADR05 方案 B「仅兑票返回 payload，不建会话不发 token」契约）。
/// <para>编排（开发方案 §2.3）：缺/空白 ticket 显式校验（与测试 Fake 同码 <c>TICKET_INVALID</c>，不复制
/// 镜像源 LoginAsync 的隐性 null 路径）→ 频控（P0-2 独立桶 <c>ext:ticket-redeem:{ip}</c>——服务间调用 IP
/// 集中，阈值 <see cref="LoginProtectionOptions.TicketRedeemServicePerMinutePerIp"/> 默认 60）→
/// <see cref="ISealedTicketService{T}"/> 验票（P1-3 统一判因，null 恒 <c>TICKET_INVALID</c>）→
/// payload.ChannelId 空白 fail-closed（事件链签发契约异常观测）→ L2 账号状态检查（PlatformUid 直读 →
/// <see cref="IExternalIdentityService"/> 反查 fallback（含直读 miss 数据漂移）→ 二跳取账号行 →
/// 禁用 <c>ACCOUNT_DISABLED</c> / 冻结 <c>ACCOUNT_FROZEN</c>——均 400，与 FederatedLoginOrchestrator 守卫同序同语义）
/// → 返回 <see cref="FederatedTicketPayload"/> 明文。失败抛类型化异常（频控 <see cref="RateLimitedException"/> 429 /
/// 业务拒绝 <see cref="DomainException"/> 400 / 票据服务未装配 <see cref="ServiceNotConfiguredException"/> 503）。</para>
/// <para><b>不依赖 <see cref="IFederatedLoginOrchestrator"/></b>（兑票不编排、不建号、不发 token——与登录面
/// 边界彻底分离；建号归事件链预置/DMP register/登录编排）。</para>
/// <para>注册：<c>AddConstructibleService&lt;IExternalTicketRedeemService, ExternalTicketRedeemService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// <para>⚠️ 边缘语义：冻结/禁用拒绝发生在验票 CAS 核销<b>之后</b>——票已消费，重试需事件链新票
/// （<c>ISealedTicketService</c> 无 Peek 原语下唯一可行顺序，与登录面 P2-5 同型）；拒绝分支 LogWarning
/// （业务预期拒绝，非异常补偿）。</para>
/// </summary>
[DiContractIgnore]
[GenerateController]
public sealed class ExternalTicketRedeemService : DomainServiceBase, IExternalTicketRedeemService
{
    private const string TicketInvalid = "TICKET_INVALID";

    private readonly IOptions<AuthCenterOptions> _options;
    private readonly IRateLimitCheck _rateLimit;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<ExternalTicketRedeemService> _logger;

    // DI004 铁律：领域服务经 User.Use<T>() 懒加载（守卫工厂帧内解析——验票/L2 检查全为守卫工厂门面）
    private ISealedTicketService<FederatedTicketPayload>? _ticketService;
    private ISealedTicketService<FederatedTicketPayload> TicketService => _ticketService ??= User.Use<ISealedTicketService<FederatedTicketPayload>>();
    private IAuthAccountQueryService? _accountQuery;
    private IAuthAccountQueryService AccountQuery => _accountQuery ??= User.Use<IAuthAccountQueryService>();
    private IExternalIdentityService? _externalIdentity;
    private IExternalIdentityService ExternalIdentity => _externalIdentity ??= User.Use<IExternalIdentityService>();
    private IFreezeStateProvider? _freezeState;
    private IFreezeStateProvider FreezeState => _freezeState ??= User.Use<IFreezeStateProvider>();

    public ExternalTicketRedeemService(
        IDomainUser user,
        IOptions<AuthCenterOptions> options,
        IRateLimitCheck rateLimit,
        IHttpContextAccessor? httpContextAccessor,
        ILogger<ExternalTicketRedeemService> logger)
        : base(user)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rateLimit = rateLimit ?? throw new ArgumentNullException(nameof(rateLimit));
        _httpContextAccessor = httpContextAccessor;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<FederatedTicketPayload> RedeemAsync(ExternalTicketRedeemRequest request, CancellationToken ct = default)
    {
        // ① 入参显式校验（对齐方案 §2.3——生产与测试 Fake 恒同码；不复制镜像源 LoginAsync 的隐性
        // RedeemAsync("") null 路径——空请求快速失败 400 明确诊断，不耗频控桶）
        var ticket = request?.Ticket;
        if (string.IsNullOrWhiteSpace(ticket))
        {
            _logger.LogWarning("业务兑票请求缺 ticket（400 TICKET_INVALID）");
            throw new DomainException(TicketInvalid) { ErrorCode = TicketInvalid };
        }

        // ② 频控 IP 维度（P0-2 独立桶——服务间调用 IP 集中，配额语义「每后端实例」；防重试风暴 + DoS 纵深）——
        // 失败抛 429；IP 经 IHttpContextAccessor 取服务端 RemoteIpAddress（不可由客户端伪造——防频控绕过）
        var ipAddress = _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString();
        var ipKey = string.IsNullOrWhiteSpace(ipAddress) ? "unknown" : ipAddress;
        var protection = _options.Value.LoginProtection;
        if (!_rateLimit.TryAcquire($"ext:ticket-redeem:{ipKey}", protection.TicketRedeemServicePerMinutePerIp, TimeSpan.FromMinutes(1), out _))
            throw new RateLimitedException(0, $"ext:ticket-redeem:{ipKey}") { ErrorCode = "TICKET_RATE_LIMITED" };

        try
        {
            // ③ 验票（P1-3 统一判因：RedeemAsync 失败/过期/已用/篡改/核销人不符恒返回 null——不泄露判因）
            var payload = await TicketService.RedeemAsync(ticket, context: null, ct);
            if (payload is null)
            {
                _logger.LogWarning("业务兑票失败——RedeemAsync 返回 null（票无效/过期/已用/篡改/claimant 不符——统一 TICKET_INVALID）");
                throw new DomainException(TicketInvalid) { ErrorCode = TicketInvalid };
            }

            // ④ payload.ChannelId 空白 fail-closed（事件链签发契约异常场景——防「无法定位通道的 payload」
            // 透传给 DMP 致 register 分流链断裂；对齐镜像源 L102-104 观测先例）
            if (string.IsNullOrWhiteSpace(payload.ChannelId))
            {
                _logger.LogWarning("业务兑票失败——票载荷 ChannelId 空白（事件链 EventReplyHandlerBase 签发契约异常，请排查——统一 TICKET_INVALID）");
                throw new DomainException(TicketInvalid) { ErrorCode = TicketInvalid };
            }

            // ⑤ L2 账号状态检查（只读查询不建号不改写；无账号放行——新粉丝兑票由 DMP register 分流建号）：
            // PlatformUid 直读 → 反查 fallback（含直读 miss 数据漂移）→ 二跳取账号行 → 禁用/冻结拒绝（400，
            // 与 FederatedLoginOrchestrator 守卫 (e)/(f) 同序同语义——先 IsEnabled 后 IsFrozen）
            var account = await ResolveAccountAsync(payload, ct);
            if (account is not null)
            {
                if (!account.IsEnabled)
                {
                    _logger.LogWarning("业务兑票拒绝——账号禁用（UId={UId}, ChannelId={ChannelId}——票已消费，重试需事件链新票）",
                        account.UId, payload.ChannelId);
                    throw new DomainException("ACCOUNT_DISABLED") { ErrorCode = "ACCOUNT_DISABLED" };
                }
                if (await FreezeState.IsFrozenAsync(account.UId, ct: ct))
                {
                    _logger.LogWarning("业务兑票拒绝——账号冻结（UId={UId}, ChannelId={ChannelId}——票已消费，重试需事件链新票）",
                        account.UId, payload.ChannelId);
                    throw new DomainException("ACCOUNT_FROZEN") { ErrorCode = "ACCOUNT_FROZEN" };
                }
            }

            // ⑥ 成功——返回 payload 明文（不建会话、不发 token、不触编排）
            _logger.LogDebug("业务兑票成功（ChannelId={ChannelId}, ExternalUserId={ExternalUserId}, PlatformUid={PlatformUid}, TicketPrefix={TicketPrefix}）",
                payload.ChannelId, payload.ExternalUserId, payload.PlatformUid, TicketPrefix(ticket));
            return payload;
        }
        catch (DomainGuardException ex)
        {
            // fail-hard：ISealedTicketService（主框架契约，TrustCenter DB 后端注册）/L2 检查依赖（AuthCenter
            // 内部门面必然注册，纯防御）未装配 → 守卫异常 → 503（v4.10.118 类型化守卫基类——转达 F1 闭环，
            // 类型判定替代字符串匹配，框架文案变更不再影响映射）
            throw new ServiceNotConfiguredException("ticket-service", ex.Message, ex) { ErrorCode = "TICKET_SERVICE_NOT_CONFIGURED" };
        }
    }

    /// <summary>L2 账号定位：PlatformUid 直读 → 反查 fallback（含直读 miss 数据漂移）→ 二跳取账号行；均未命中 → null（放行）。</summary>
    private async Task<AuthAccountEntity?> ResolveAccountAsync(FederatedTicketPayload payload, CancellationToken ct)
    {
        if (!string.IsNullOrWhiteSpace(payload.PlatformUid))
        {
            var direct = await AccountQuery.GetByUIdAsync(payload.PlatformUid, ct);
            if (direct is not null)
                return direct;
        }

        // PlatformUid 空 或 直读 miss（弱身份清理物理删除等数据漂移）→ 反查 fallback（channelId/externalUserId
        // 恒可从 payload 取得——步骤④已保证 ChannelId 非空白）
        var mapped = await ExternalIdentity.GetByExternalIdAsync(payload.ChannelId, payload.ExternalUserId, ct);
        if (mapped is null || string.IsNullOrWhiteSpace(mapped.UId))
            return null;
        return await AccountQuery.GetByUIdAsync(mapped.UId, ct);
    }

    /// <summary>票标识前缀（成功日志用——不落全票：ticket 高熵机密，仅前 8 字符协助运维反查）。</summary>
    private static string TicketPrefix(string ticket)
        => string.IsNullOrEmpty(ticket) ? "?" : (ticket.Length <= 8 ? ticket : ticket[..8]);
}