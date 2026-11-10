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
/// V0.14.0：外部 IdP 业务兑票门面 <see cref="ExternalTicketRedeemService"/> 测试——
/// 缺/空白 ticket 显式校验（生产与 Fake 同码）→ 频控（P0-2 独立桶 <c>ext:ticket-redeem:{ip}</c>，服务间调用
/// 阈值 <see cref="LoginProtectionOptions.TicketRedeemServicePerMinutePerIp"/> 默认 60）→ 验票
/// （P1-3 统一 <c>TICKET_INVALID</c>）→ payload.ChannelId 空白 fail-closed → L2 账号状态检查
/// （PlatformUid 直读 → <see cref="IExternalIdentityService"/> 反查 fallback（含直读 miss 数据漂移）→
/// 二跳取账号行 → 禁用 <c>ACCOUNT_DISABLED</c> / 冻结 <c>ACCOUNT_FROZEN</c>，与 FederatedLoginOrchestrator
/// 守卫 (e)/(f) 同序）→ 返回 <see cref="FederatedTicketPayload"/> 明文（不建会话不发 token 不触编排）。
/// <para>分层逻辑单测：StubDomainUser 直构门面（经基类 User 取上下文）+ Fake 依赖注册进桩 ServiceProvider
/// （Fake <see cref="ISealedTicketService{T}"/> / Fake 三 L2 查询依赖 + 可配置 IRateLimitCheck +
/// Fake IHttpContextAccessor——IP 维度注入可测键值；<b>不注册 IFederatedLoginOrchestrator</b>——
/// 成功用例通过 = 兑票零编排依赖的实证断言）。</para>
/// </summary>
public class ExternalTicketRedeemServiceTests
{
    private const string ChannelId = "wechat-event-1";
    private const string ExternalUserId = "openid-event-1";
    private const string Ticket = "ticket-32B-base64url-abcdefghijklmnopqrstuvwxyz12";

    private static (ExternalTicketRedeemService Service, ExternalTicketLoginServiceTests.FakeSealedTicketService TicketSvc,
        FakeAuthAccountQueryService AccountQuery, FakeExternalIdentityService ExternalIdentity,
        FakeFreezeStateProvider FreezeState, ExternalTicketLoginServiceTests.FakeRateLimitCheck RateLimit)
        CreateService(AuthCenterOptions? options = null, string? remoteIp = null)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var ticketSvc = new ExternalTicketLoginServiceTests.FakeSealedTicketService();
        var accountQuery = new FakeAuthAccountQueryService();
        var externalIdentity = new FakeExternalIdentityService();
        var freezeState = new FakeFreezeStateProvider();
        var rateLimit = new ExternalTicketLoginServiceTests.FakeRateLimitCheck();
        var accessor = remoteIp is null ? null : new ExternalTicketLoginServiceTests.FakeHttpContextAccessor(remoteIp);

        var stub = AuthenticationTestHost.CreateStub(fsql, register: (services, user) =>
        {
            services.AddSingleton<ISealedTicketService<FederatedTicketPayload>>(ticketSvc);
            services.AddSingleton<IAuthAccountQueryService>(accountQuery);
            services.AddSingleton<IExternalIdentityService>(externalIdentity);
            services.AddSingleton<IFreezeStateProvider>(freezeState);
            services.AddSingleton<IRateLimitCheck>(rateLimit);
        });

        var service = new ExternalTicketRedeemService(
            stub,
            Options.Create(options ?? new AuthCenterOptions()),
            rateLimit,
            accessor,
            NullLogger<ExternalTicketRedeemService>.Instance);
        return (service, ticketSvc, accountQuery, externalIdentity, freezeState, rateLimit);
    }

    private static ExternalTicketRedeemRequest Request(string ticket)
        => new(ticket);

    /// <summary>票载荷构造（直接构造 record——保留空 ChannelId 用例语义；protocol_type 双写协议类型）。</summary>
    private static FederatedTicketPayload Payload(string? uid = null, string? channelId = ChannelId)
        => new(
            Provider: "wechat",
            ChannelType: "mp_subscription",
            ChannelId: channelId ?? string.Empty,   // 空 = payload.ChannelId 缺失场景用例
            ChannelAlias: null,
            ExternalUserId: ExternalUserId,
            UnionId: null,
            PlatformUid: uid,
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

    private static AuthAccountEntity Account(string uid, bool isEnabled = true)
        => new() { UId = uid, IsEnabled = isEnabled };

    /// <summary>(a) 成功路径：PlatformUid 非空 → 直读命中 → 返回 payload；频控被检查（独立桶 key）；不触编排（无 Orchestrator 依赖即设计断言）。</summary>
    [Fact]
    public async Task Redeem_PlatformUidDirectHit_ReturnsPayload()
    {
        var (service, ticketSvc, accountQuery, _, _, rateLimit) = CreateService(remoteIp: "10.0.0.1");
        ticketSvc.Payload = Payload(uid: "u-direct");
        accountQuery.AccountResolver = uid => uid == "u-direct" ? Account("u-direct") : null;

        var result = await service.RedeemAsync(Request(Ticket), ct: TestContext.Current.CancellationToken);

        Assert.Equal("u-direct", result.PlatformUid);
        Assert.Equal(Ticket, ticketSvc.LastTicket);
        Assert.Null(ticketSvc.LastContext);                          // context: null（ExpectedClaimant=null 匿名语义）
        Assert.Equal("u-direct", accountQuery.LastUid);
        Assert.True(rateLimit.LastTryAcquireCalled);
        Assert.Equal("ext:ticket-redeem:10.0.0.1", rateLimit.LastKey);   // 独立桶——不占用登录桶 ext:ticket:{ip}
    }

    /// <summary>(b) PlatformUid 空 → 反查命中 → 二跳链（ExternalIdentityDto.UId → GetByUIdAsync 账号行）→ 返回 payload。</summary>
    [Fact]
    public async Task Redeem_PlatformUidNull_FallbackByExternalId_TwoHop_ReturnsPayload()
    {
        var (service, ticketSvc, accountQuery, externalIdentity, _, _) = CreateService(remoteIp: "10.0.0.2");
        ticketSvc.Payload = Payload(uid: null);
        externalIdentity.Identity = new ExternalIdentityDto("u-mapped", ChannelId, ExternalUserId, DateTime.UtcNow);
        accountQuery.AccountResolver = uid => uid == "u-mapped" ? Account("u-mapped") : null;

        var result = await service.RedeemAsync(Request(Ticket), ct: TestContext.Current.CancellationToken);

        Assert.Equal(ChannelId, externalIdentity.LastChannelId);     // 反查参数（ChannelId 经步骤④非空白保证）
        Assert.Equal(ExternalUserId, externalIdentity.LastExternalUserId);
        Assert.Equal("u-mapped", accountQuery.LastUid);              // 二跳——取反查结果账号行
        Assert.Null(result.PlatformUid);
    }

    /// <summary>(c) 无账号放行：直读 miss + 反查 miss（未绑定）→ 不拒票返回 payload（新粉丝兑票由 DMP register 分流建号）。</summary>
    [Fact]
    public async Task Redeem_NoAccountFound_AllowsPayload()
    {
        var (service, ticketSvc, _, _, freezeState, _) = CreateService(remoteIp: "10.0.0.3");
        ticketSvc.Payload = Payload(uid: "u-orphan");    // 直读 miss（账号不存在/已清理）
        freezeState.IsFrozenResult = false;

        var result = await service.RedeemAsync(Request(Ticket), ct: TestContext.Current.CancellationToken);

        Assert.Equal("u-orphan", result.PlatformUid);    // 放行——payload 原样返回
    }

    /// <summary>(d) 冻结账号 → ACCOUNT_FROZEN（400；L2 守卫拒绝——票已消费，重试需新票）。</summary>
    [Fact]
    public async Task Redeem_FrozenAccount_ThrowsAccountFrozen()
    {
        var (service, ticketSvc, accountQuery, _, freezeState, _) = CreateService(remoteIp: "10.0.0.4");
        ticketSvc.Payload = Payload(uid: "u-frozen");
        accountQuery.AccountResolver = uid => uid == "u-frozen" ? Account("u-frozen") : null;
        freezeState.IsFrozenResult = true;

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.RedeemAsync(Request(Ticket), ct: TestContext.Current.CancellationToken));

        Assert.Equal("ACCOUNT_FROZEN", ex.ErrorCode);
    }

    /// <summary>(e) 禁用账号 → ACCOUNT_DISABLED（400；IsEnabled=false——先于冻结检查，与 Orchestrator 守卫同序）。</summary>
    [Fact]
    public async Task Redeem_DisabledAccount_ThrowsAccountDisabled()
    {
        var (service, ticketSvc, accountQuery, _, freezeState, _) = CreateService(remoteIp: "10.0.0.5");
        ticketSvc.Payload = Payload(uid: "u-disabled");
        accountQuery.AccountResolver = uid => uid == "u-disabled" ? Account("u-disabled", isEnabled: false) : null;
        freezeState.IsFrozenResult = true;   // 冻结也命中——验证禁用先判（顺序锁定）

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.RedeemAsync(Request(Ticket), ct: TestContext.Current.CancellationToken));

        Assert.Equal("ACCOUNT_DISABLED", ex.ErrorCode);   // 禁用优先于冻结
    }

    /// <summary>(f) payload.ChannelId 空白（事件链签发契约异常）→ fail-closed TICKET_INVALID。</summary>
    [Fact]
    public async Task Redeem_EmptyChannelIdInPayload_ThrowsTicketInvalid()
    {
        var (service, ticketSvc, _, _, _, _) = CreateService(remoteIp: "10.0.0.6");
        ticketSvc.Payload = Payload(uid: "u-channel-missing", channelId: null);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.RedeemAsync(Request(Ticket), ct: TestContext.Current.CancellationToken));

        Assert.Equal("TICKET_INVALID", ex.ErrorCode);   // fail-closed——不透传无法定位通道的 payload
    }

    /// <summary>(g) PlatformUid 直读 miss（数据漂移）→ fallthrough 反查命中 → 返回 payload。</summary>
    [Fact]
    public async Task Redeem_PlatformUidMiss_FallthroughByExternalId_ReturnsPayload()
    {
        var (service, ticketSvc, accountQuery, externalIdentity, _, _) = CreateService(remoteIp: "10.0.0.7");
        ticketSvc.Payload = Payload(uid: "u-purged");      // 直读 miss（弱身份清理物理删除等数据漂移）
        externalIdentity.Identity = new ExternalIdentityDto("u-mapped-2", ChannelId, ExternalUserId, DateTime.UtcNow);
        accountQuery.AccountResolver = uid => uid == "u-mapped-2" ? Account("u-mapped-2") : null;

        var result = await service.RedeemAsync(Request(Ticket), ct: TestContext.Current.CancellationToken);

        Assert.Equal("u-mapped-2", accountQuery.LastUid);  // fallthrough 反查生效（直读 miss 不阻断）
        Assert.Equal("u-purged", result.PlatformUid);
    }

    /// <summary>(h) 缺/空白 ticket → 400 TICKET_INVALID（生产显式校验——不复制镜像 LoginAsync 的隐性 RedeemAsync("") null 路径；空请求快速失败不耗频控桶）。</summary>
    [Fact]
    public async Task Redeem_BlankTicket_ThrowsTicketInvalid_NoRateLimitTouch()
    {
        var (service, ticketSvc, _, _, _, rateLimit) = CreateService(remoteIp: "10.0.0.8");

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.RedeemAsync(Request("  "), ct: TestContext.Current.CancellationToken));

        Assert.Equal("TICKET_INVALID", ex.ErrorCode);
        Assert.Null(ticketSvc.LastTicket);                 // 验票未触
        Assert.False(rateLimit.LastTryAcquireCalled);      // 频控未触——空请求快速失败
    }

    /// <summary>(i) 票无效（P1-3）：RedeemAsync 恒 null → 统一 TICKET_INVALID（不泄露判因）。</summary>
    [Fact]
    public async Task Redeem_RedeemNull_ThrowsTicketInvalid()
    {
        var (service, ticketSvc, _, _, _, _) = CreateService(remoteIp: "10.0.0.9");
        ticketSvc.Payload = null;

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.RedeemAsync(Request(Ticket), ct: TestContext.Current.CancellationToken));

        Assert.Equal("TICKET_INVALID", ex.ErrorCode);
    }

    /// <summary>(j) 频控触发（P0-2 独立桶）：TryAcquire false → TICKET_RATE_LIMITED（429）；验票不执行。</summary>
    [Fact]
    public async Task Redeem_RateLimited_ThrowsTicketRateLimited_TicketSvcNotTouched()
    {
        var (service, ticketSvc, _, _, _, rateLimit) = CreateService(remoteIp: "10.0.0.10");
        rateLimit.AcquireResult = false;

        var ex = await Assert.ThrowsAsync<RateLimitedException>(() =>
            service.RedeemAsync(Request(Ticket), ct: TestContext.Current.CancellationToken));

        Assert.Equal("TICKET_RATE_LIMITED", ex.ErrorCode);
        Assert.Null(ticketSvc.LastTicket);                 // 频控前置——票服务未被触碰
    }

    /// <summary>(k) IP 空（无 IHttpContextAccessor）→ key 用 "unknown"（频控键不落空）。</summary>
    [Fact]
    public async Task Redeem_NullIp_UsesUnknownKey()
    {
        var (service, ticketSvc, _, _, _, rateLimit) = CreateService(remoteIp: null);
        ticketSvc.Payload = Payload(uid: "u-unknown-ip");

        await service.RedeemAsync(Request(Ticket), ct: TestContext.Current.CancellationToken);

        Assert.Equal("ext:ticket-redeem:unknown", rateLimit.LastKey);
    }

    /// <summary>(l) fail-hard：守卫异常（<see cref="DomainGuardException"/>——v4.10.118 类型化守卫基类，F1 闭环）
    /// → 503 TICKET_SERVICE_NOT_CONFIGURED。单测桩不模拟守卫工厂（StubDomainUser 接口解析走 GetRequiredService），
    /// 经 RedeemBehavior 抛守卫类型异常触发门面 catch 分支（与生产同路径：异常经 catch 过滤 → 503 映射）。</summary>
    [Fact]
    public async Task Redeem_GuardException_ThrowsServiceNotConfigured()
    {
        var (service, ticketSvc, _, _, _, _) = CreateService(remoteIp: "10.99.0.1");
        ticketSvc.RedeemBehavior = (ticket, context) =>
            throw new DomainGuardException("领域架构守卫: 未装配 ISealedTicketService<FederatedTicketPayload>");

        var ex = await Assert.ThrowsAsync<ServiceNotConfiguredException>(() =>
            service.RedeemAsync(Request(Ticket), ct: TestContext.Current.CancellationToken));

        Assert.Equal("TICKET_SERVICE_NOT_CONFIGURED", ex.ErrorCode);
    }

    // ── 测试桩（Fake 门面——仅测试项目，不注册进生产） ──

    /// <summary>Fake 账号查询——<see cref="AccountResolver"/> 按 uid 解析账号（null = 未命中）；GetByUIdAsync 捕获 LastUid。</summary>
    internal sealed class FakeAuthAccountQueryService : IAuthAccountQueryService
    {
        public Func<string, AuthAccountEntity?>? AccountResolver { get; set; }
        public string? LastUid { get; private set; }

        public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        {
            LastUid = uid;
            return Task.FromResult(AccountResolver?.Invoke(uid));
        }

        public Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default)
            => Task.FromResult<AuthAccountEntity?>(null);
        public Task<UserProfileEntity?> GetProfileByUIdAsync(string uid, CancellationToken ct = default)
            => Task.FromResult<UserProfileEntity?>(null);
        public Task<bool> IsWechatBoundAsync(string uid, CancellationToken ct = default)
            => Task.FromResult(false);
    }

    /// <summary>Fake 外部身份映射——GetByExternalIdAsync 返回可配置 Identity（null = 未绑定），参数捕获。</summary>
    internal sealed class FakeExternalIdentityService : IExternalIdentityService
    {
        public ExternalIdentityDto? Identity { get; set; }
        public string? LastChannelId { get; private set; }
        public string? LastExternalUserId { get; private set; }

        public Task<ExternalIdentityDto> LinkAsync(string uid, string channelId, string externalUserId, string? operatorName = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task UnlinkAsync(string uid, string channelId, string externalUserId, string? operatorName = null, CancellationToken ct = default)
            => throw new NotSupportedException();
        public Task<ExternalIdentityDto?> GetByExternalIdAsync(string channelId, string externalUserId, CancellationToken ct = default)
        {
            LastChannelId = channelId;
            LastExternalUserId = externalUserId;
            return Task.FromResult(Identity);
        }
        public Task<IReadOnlyList<ExternalIdentityDto>> GetByAccountAsync(string uid, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>Fake 冻结状态提供者——IsFrozenAsync 返回可配置结果。</summary>
    internal sealed class FakeFreezeStateProvider : IFreezeStateProvider
    {
        public bool IsFrozenResult { get; set; }

        public Task<bool> IsFrozenAsync(string uid, int? scope = null, CancellationToken ct = default)
            => Task.FromResult(IsFrozenResult);
        public Task<IReadOnlyList<AuthAccountFreezeEntity>> GetActiveAsync(string uid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuthAccountFreezeEntity>>(Array.Empty<AuthAccountFreezeEntity>());
        public void Invalidate(string uid) { }
    }
}