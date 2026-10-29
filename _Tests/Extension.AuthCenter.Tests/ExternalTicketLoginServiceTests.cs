using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Ticket;
using TKW.Framework.Utility.RateLimitChecks;
using TKWF.Ext.Federation;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// V0.10.0 + B3b + D03 1.3：外部 IdP 票据 SPA 兑现门面 <see cref="ExternalTicketLoginService"/> 测试——
/// 频控（P2-4）→ 验票（P1-3 统一 TICKET_INVALID）→ 委托共享编排（P2-2）。
/// <para>B3b（MapGenerated 生成化）：门面签名 <c>LoginAsync(ExternalTicketLoginRequest, ct)</c>（ipAddress 参数删除——
/// 门面内经 <see cref="IHttpContextAccessor"/> 取服务端 RemoteIpAddress）；成功返回 <see cref="TokenResponse"/>，
/// 失败抛类型化异常（频控 <see cref="RateLimitedException"/> 429 / 验票 <see cref="DomainException"/> TICKET_INVALID 400）。</para>
/// <para>D03 1.3（载荷切换）：票载荷 = <see cref="FederatedTicketPayload"/>（record 直接构造——保留空 ChannelId
/// 用例语义（f/g 兑付兜底路径）；<b>不经 <see cref="FederatedTicketPayloadBuilder"/></b>——Builder 是签票侧校验器
/// （空 ChannelId 抛异常），测试需构造任意形态载荷含缺失场景）；兑付后门面经
/// <see cref="FederatedTicketPayloadExtensions.ToFederatedIdentity"/> 适配传 Orchestrator（签名未变吃
/// FederatedIdentity）——Orchestrator 断言方向：UId=payload.PlatformUid / ChannelId 透传 /
/// ChannelType=Extensions["protocol_type"]（协议类型还原）。</para>
/// <para>分层逻辑单测：StubDomainUser 直构门面（经基类 User 取上下文）+ Fake 依赖注册进桩 ServiceProvider
/// （ISealedTicketService&lt;FederatedTicketPayload&gt;/IFederatedLoginOrchestrator Fake + 可配置 IRateLimitCheck +
/// Fake IHttpContextAccessor——IP 维度注入可测键值）。</para>
/// </summary>
public class ExternalTicketLoginServiceTests
{
    private const string ChannelId = "wechat-event-1";
    private const string ExternalUserId = "openid-event-1";
    private const string Ticket = "ticket-32B-base64url-abcdefghijklmnopqrstuvwxyz12";

    private static (ExternalTicketLoginService Service, FakeSealedTicketService TicketSvc,
        FakeFederatedLoginOrchestrator Orchestrator, FakeRateLimitCheck RateLimit)
        CreateService(Action<IServiceCollection, StubDomainUser>? register = null, AuthCenterOptions? options = null,
            string? remoteIp = null)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var ticketSvc = new FakeSealedTicketService();
        var orchestrator = new FakeFederatedLoginOrchestrator();
        var rateLimit = new FakeRateLimitCheck();
        var accessor = remoteIp is null ? null : new FakeHttpContextAccessor(remoteIp);

        var stub = AuthenticationTestHost.CreateStub(fsql, register: (services, user) =>
        {
            services.AddSingleton<ISealedTicketService<FederatedTicketPayload>>(ticketSvc);
            services.AddSingleton<IFederatedLoginOrchestrator>(orchestrator);
            services.AddSingleton<IRateLimitCheck>(rateLimit);
            register?.Invoke(services, user);
        });

        var service = new ExternalTicketLoginService(
            stub,
            Options.Create(options ?? new AuthCenterOptions()),
            rateLimit,
            accessor,
            NullLogger<ExternalTicketLoginService>.Instance);
        return (service, ticketSvc, orchestrator, rateLimit);
    }

    private static ExternalTicketLoginRequest Request(string ticket, string? channelId = null)
        => new(ticket, channelId);

    /// <summary>票载荷构造（D03 1.3——直接构造 record 保留空 ChannelId 用例语义；protocol_type 双写协议类型）。</summary>
    private static FederatedTicketPayload Payload(string? uid = null, string? channelId = ChannelId)
        => new(
            Provider: "wechat",
            ChannelType: "mp_subscription",   // 通道形态（协议类型走 Extensions）
            ChannelId: channelId ?? string.Empty,   // 空 = 兑付侧 body channel_id 兜底路径（f/g 用例）
            ChannelAlias: null,
            ExternalUserId: ExternalUserId,
            UnionId: null,
            PlatformUid: uid,                 // null = null-claimant（编排映射/建号兜底）
            Assurance: "event",
            Iat: 0,
            Exp: 0,
            Jti: "jti-test")
        {
            Extensions = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                [FederatedTicketPayloadExtensions.ProtocolTypeKey] = "wechat_event",
            },
        };

    /// <summary>(a) 成功路径：验票成功（identity 载荷）→ 委托 orchestrator → token1；频控被检查；channel_id 透传。</summary>
    [Fact]
    public async Task Login_TicketValid_DelegatesOrchestrator_ReturnsToken()
    {
        var (service, ticketSvc, orchestrator, rateLimit) = CreateService(remoteIp: "10.0.0.1");
        ticketSvc.Payload = Payload(uid: "u-ticket-1");
        orchestrator.Result = new TokenResponse("at-t", "rt-t", "Bearer", 7200);

        var result = await service.LoginAsync(Request(Ticket));

        Assert.Equal("at-t", result.AccessToken);
        Assert.NotNull(result.RefreshToken);
        Assert.True(rateLimit.LastTryAcquireCalled);
        Assert.Equal("ext:ticket:10.0.0.1", rateLimit.LastKey);
        Assert.Equal(Ticket, ticketSvc.LastTicket);
        Assert.Null(ticketSvc.LastContext);                          // context: null（ExpectedClaimant=null 匿名浏览器语义）
        Assert.True(orchestrator.Invoked);
        Assert.Equal("u-ticket-1", orchestrator.LastIdentity!.UId);
    }

    /// <summary>(b) 票无效/过期/已用/篡改/claimant 不符（P1-3）：RedeemAsync 恒 null → 统一 TICKET_INVALID（不泄露判因）；orchestrator 不调。</summary>
    [Fact]
    public async Task Login_RedeemNull_ReturnsTicketInvalid_NoDelegate()
    {
        var (service, ticketSvc, orchestrator, _) = CreateService(remoteIp: "10.0.0.2");
        ticketSvc.Payload = null;   // RedeemAsync null（无效/过期/已用/篡改统一）

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.LoginAsync(Request(Ticket)));

        Assert.Equal("TICKET_INVALID", ex.ErrorCode);
        Assert.False(orchestrator.Invoked);
    }

    /// <summary>(c) 频控触发（P2-4）：TryAcquire false → TICKET_RATE_LIMITED 异常（429）；orchestrator/验票均不执行。</summary>
    [Fact]
    public async Task Login_RateLimited_Throws_TicketSvcNotTouched()
    {
        var (service, ticketSvc, orchestrator, rateLimit) = CreateService(remoteIp: "10.0.0.3");
        rateLimit.AcquireResult = false;

        var ex = await Assert.ThrowsAsync<RateLimitedException>(() =>
            service.LoginAsync(Request(Ticket)));

        Assert.Equal("TICKET_RATE_LIMITED", ex.ErrorCode);
        Assert.Null(ticketSvc.LastTicket);     // 频控前置——票服务未被触碰
        Assert.False(orchestrator.Invoked);
    }

    /// <summary>(d) IP 空（无 IHttpContextAccessor）→ key 用 "unknown"（频控键不落空）。
    /// B3b：ipAddress 参数删除，门面内经 IHttpContextAccessor 取——null → "unknown" 对齐原 handler 语义。</summary>
    [Fact]
    public async Task Login_NullIp_UsesUnknownKey()
    {
        var (service, ticketSvc, _, rateLimit) = CreateService(remoteIp: null);
        ticketSvc.Payload = Payload();

        await service.LoginAsync(Request(Ticket));

        Assert.Equal("ext:ticket:unknown", rateLimit.LastKey);
    }

    /// <summary>(e) channel_id 消歧（P2-3）：票载荷 ChannelId 与参数不一致 → TICKET_INVALID（防跨通道错配）。</summary>
    [Fact]
    public async Task Login_ChannelIdMismatch_ReturnsTicketInvalid()
    {
        var (service, ticketSvc, orchestrator, _) = CreateService(remoteIp: "10.0.0.4");
        ticketSvc.Payload = Payload(uid: "u-ticket-2", channelId: ChannelId);

        var ex = await Assert.ThrowsAsync<DomainException>(() =>
            service.LoginAsync(Request(Ticket, "other-channel")));

        Assert.Equal("TICKET_INVALID", ex.ErrorCode);
        Assert.False(orchestrator.Invoked);
    }

    /// <summary>(f) 票载荷 ChannelId 空 + 参数兜底：identity with 兜底 ChannelId 委托 orchestrator。</summary>
    [Fact]
    public async Task Login_EmptyTicketChannelId_UsesProvidedChannelId()
    {
        var (service, ticketSvc, orchestrator, _) = CreateService(remoteIp: "10.0.0.5");
        ticketSvc.Payload = Payload(uid: "u-ticket-3", channelId: null);   // 票载荷 ChannelId 空

        var result = await service.LoginAsync(Request(Ticket, ChannelId));

        Assert.NotNull(result.AccessToken);                          // 兜底生效
        Assert.Equal(ChannelId, orchestrator.LastIdentity!.ChannelId);
    }

    /// <summary>(g) 票载荷 ChannelId 空 + 参数也空 → TICKET_INVALID（无法建立联邦映射，fail-closed）。</summary>
    [Fact]
    public async Task Login_NoChannelIdAtAll_ReturnsTicketInvalid()
    {
        var (service, ticketSvc, orchestrator, _) = CreateService(remoteIp: "10.0.0.6");
        ticketSvc.Payload = Payload(uid: "u-ticket-4", channelId: null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.LoginAsync(Request(Ticket)));

        Assert.Equal("TICKET_INVALID", ex.ErrorCode);
        Assert.False(orchestrator.Invoked);
    }

    /// <summary>(h) P1-2（claimant 非 null 票）：Fake 模拟"签发带 claimant 的票 → RedeemAsync(context:null) CAS 恒败 → null"——
    /// 统一 TICKET_INVALID（安全正确 fail，非隐蔽失效）。</summary>
    [Fact]
    public async Task Login_ClaimantBoundTicket_ReturnsTicketInvalid()
    {
        var (service, ticketSvc, orchestrator, _) = CreateService(remoteIp: "10.0.0.7");
        // 模拟 TrustCenter SealedTicketService CAS：签发 ExpectedClaimant="app-1" + 兑换 context:null → 不 swap → null
        ticketSvc.RedeemBehavior = (ticket, context) =>
            context?.ExpectedClaimant == "app-1" ? Payload() : null;

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.LoginAsync(Request(Ticket)));

        Assert.Equal("TICKET_INVALID", ex.ErrorCode);   // 统一判因——不泄露"claimant 不符"
        Assert.False(orchestrator.Invoked);
    }

    /// <summary>(i) P2-5（fail-closed 边缘）：验票成功但编排异常（建号失败）→ 票已消费无会话——异常传播不 catch，LogError 补偿记录。</summary>
    [Fact]
    public async Task Login_OrchestratorThrows_Propagates()
    {
        var (service, ticketSvc, orchestrator, _) = CreateService(remoteIp: "10.0.0.8");
        ticketSvc.Payload = Payload(uid: "u-ticket-5");
        orchestrator.Throw = new InvalidOperationException("DB 瞬时故障——建号失败");

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync(Request(Ticket)));
    }

    // ── 测试桩（Fake 门面——仅测试项目，不注册进生产） ──

    /// <summary>B3b：Fake IHttpContextAccessor——RemoteIpAddress 可配置（门面内经其取频控 IP 维度）。</summary>
    internal sealed class FakeHttpContextAccessor(string remoteIp) : IHttpContextAccessor
    {
        private HttpContext? _httpContext;
        public HttpContext? HttpContext
        {
            get => _httpContext ??= new DefaultHttpContext
            {
                Connection = { RemoteIpAddress = System.Net.IPAddress.Parse(remoteIp) }
            };
            set => _httpContext = value;
        }
    }

    /// <summary>
    /// Fake 密封票据服务——<see cref="Identity"/> 固定返回值（null = 验票失败）；
    /// <see cref="RedeemBehavior"/> 委托可模拟复杂 CAS 语义（claimant 校验等）。
    /// </summary>
    internal sealed class FakeSealedTicketService : ISealedTicketService<FederatedTicketPayload>
    {
        public FederatedTicketPayload? Payload { get; set; }
        public Func<string, TicketRedeemContext?, FederatedTicketPayload?>? RedeemBehavior { get; set; }
        public string? LastTicket { get; private set; }
        public TicketRedeemContext? LastContext { get; private set; }

        public Task<SealedTicketResult> IssueAsync(FederatedTicketPayload payload, TimeSpan ttl, SealedTicketOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<FederatedTicketPayload?> RedeemAsync(string ticket, TicketRedeemContext? context = null, CancellationToken ct = default)
        {
            LastTicket = ticket;
            LastContext = context;
            return Task.FromResult(RedeemBehavior is not null ? RedeemBehavior(ticket, context) : Payload);
        }

        public Task<int> CleanupExpiredAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }

    internal sealed class FakeFederatedLoginOrchestrator : IFederatedLoginOrchestrator
    {
        public bool Invoked { get; private set; }
        public FederatedIdentity? LastIdentity { get; private set; }
        public TokenResponse Result { get; set; } = new("at-t", "rt-t", "Bearer", 7200);
        public Exception? Throw { get; set; }

        public Task<TokenResponse> LoginAsync(FederatedIdentity identity, CancellationToken ct = default)
        {
            Invoked = true;
            LastIdentity = identity;
            if (Throw is not null) throw Throw;
            return Task.FromResult(Result);
        }
    }

    /// <summary>Fake 频控——可配置 TryAcquire 结果 + key 捕获。</summary>
    internal sealed class FakeRateLimitCheck : IRateLimitCheck
    {
        public bool AcquireResult { get; set; } = true;
        public string? LastKey { get; private set; }
        public bool LastTryAcquireCalled { get; private set; }

        public bool TryAcquire(string key, int maxAttempts, TimeSpan window, out int remaining)
        {
            LastTryAcquireCalled = true;
            LastKey = key;
            remaining = AcquireResult ? maxAttempts - 1 : 0;
            return AcquireResult;
        }

        public TimeSpan GetRetryAfter(string key, TimeSpan window) => TimeSpan.Zero;
        public int GetRemaining(string key, int maxAttempts, TimeSpan window) => AcquireResult ? maxAttempts : 0;
    }
}
