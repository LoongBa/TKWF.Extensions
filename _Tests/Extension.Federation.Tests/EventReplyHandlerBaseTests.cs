using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Ticket;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// 事件回复 handler 基类 + 内置测试 handler 测试（2026-10-15——方案
/// docs/Federation/EventReplyHandlerBase与内置测试Handler-开发方案.md，Oracle APPROVE WITH CONDITIONS 条件吸收；
/// 2026-10-16 拆分命名 ADR109 D4 适配——面① 密封票据 <see cref="ISealedTicketService{T}"/>）。
/// <para>生产路径宿主（真实 DI：<see cref="TestHostBase.BindTestScope"/> + <c>DomainUser&lt;TestUserInfo&gt;.BindScope</c>
/// + <c>User.Use&lt;T&gt;()</c> AOP）+ Fake <see cref="ISealedTicketService{T}"/>（记录 IssueAsync payload/claimant）
/// + <see cref="IOptions{FederationEventReplyOptions}"/>（开关/BaseUrl 配置）。</para>
/// <para>覆盖：① 模板方法——子类返回载荷 → 签密封票据（FederatedIdentity 载荷反序列化 + ExpectedClaimant）+ 图文（news）
/// 链接（URL 含 /test/accesscode/{ticket}）；② null 透传——子类返回 null → HandleAsync null（不拦截）；
/// ③ TestEventReplyHandler 业务规则——ChannelType=wechat_event / 开关关 null / BaseUrl 空 null /
/// 开关开 + BaseUrl 配 → FederatedIdentity 载荷（UId/ChannelId/ExternalUserId）+ ExpectedClaimant null。</para>
/// </summary>
public sealed class EventReplyHandlerBaseTests : TestHostBase
{
    private readonly FakeSealedTicketService _codes = new();
    private FederationEventReplyOptions _eventReplyOptions = new();

    protected override TestUserInfo CreateUser() => new("event-reply-tester", "事件回复基类测试用户");

    protected override void ConfigureHost(IServiceCollection services)
    {
        // Fake 密封票据服务（面①——记录 payload/claimant）
        services.AddScoped<ISealedTicketService<FederatedIdentity>>(_ => _codes);

        // 领域 Options（开关 + BaseUrl + RoutePrefix——按测试配置）
        services.AddOptions<FederationEventReplyOptions>();
        services.Configure<FederationEventReplyOptions>(o =>
        {
            o.TestEventReplyHandlerEnabled = _eventReplyOptions.TestEventReplyHandlerEnabled;
            o.TestAccessCodeEndpointBaseUrl = _eventReplyOptions.TestAccessCodeEndpointBaseUrl;
            o.RoutePrefix = _eventReplyOptions.RoutePrefix;
        });

        // 被测 handler：测试子类 + 内置测试 handler（集合注册——守卫工厂 ActivatorUtilities 构造）
        services.TryAddEnumerableConstructible<IEventReplyHandler, ProbeEventReplyHandler>();
        services.TryAddEnumerableConstructible<IEventReplyHandler, TestEventReplyHandler>();
    }

    private void EnableTestHandler(string baseUrl = "https://api.example.com", string routePrefix = "/feberation")
    {
        _eventReplyOptions = new FederationEventReplyOptions
        {
            TestEventReplyHandlerEnabled = true,
            TestAccessCodeEndpointBaseUrl = baseUrl,
            RoutePrefix = routePrefix,
        };
    }

    // ═══════════════════ 1. 模板方法（EventReplyHandlerBase.HandleAsync） ═══════════════════

    /// <summary>模板方法：子类返回载荷 → 签密封票据（FederatedIdentity 载荷反序列化 + ExpectedClaimant）+ 图文链接（URL 含测试端点）。</summary>
    [Fact]
    public async Task HandleAsync_SubclassPayload_IssuesAccessCode_ReturnsNewsArticle()
    {
        EnableTestHandler();                                       // 配置 BaseUrl（验证完整 URL 拼接）
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<ProbeEventReplyHandler>();

        var reply = await handler.HandleAsync(new SsoChannelEventContext(
            new Dictionary<string, string?> { ["external_user_id"] = "openid-1", ["uid"] = "u-1" }),
            CancellationToken.None);

        // 签密封票据被调用（FederatedIdentity 载荷反序列化 + ExpectedClaimant 透传）
        Assert.True(_codes.Issued);
        Assert.NotNull(_codes.LastIssuedIdentity);
        Assert.Equal("probe-uid", _codes.LastIssuedIdentity!.UId);
        Assert.Equal("probe-channel", _codes.LastIssuedIdentity.ChannelId);
        Assert.Equal("probe-ext", _codes.LastIssuedIdentity.ExternalUserId);
        Assert.Equal("claimant-1", _codes.LastClaimant);
        // 图文（news）链接返回
        Assert.NotNull(reply);
        Assert.NotNull(reply.NewsArticles);
        var article = Assert.Single(reply.NewsArticles);
        Assert.Equal("探针回复", article.Title);
        Assert.Contains("/test/accesscode/ac-1", article.Url);           // Fake 返回 ticket="ac-1"
        Assert.StartsWith("https://api.example.com/feberation", article.Url);
    }

    /// <summary>模板方法：子类返回 null → HandleAsync 返回 null（不拦截——first-non-null-wins 语义）。</summary>
    [Fact]
    public async Task HandleAsync_SubclassNull_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<ProbeEventReplyHandler>();

        var reply = await handler.HandleAsync(new SsoChannelEventContext(
            new Dictionary<string, string?> { ["skip"] = "true" }),
            CancellationToken.None);

        Assert.Null(reply);
        Assert.False(_codes.Issued);                                     // 未签密封票据
    }

    // ═══════════════════ 2. TestEventReplyHandler 业务规则 ═══════════════════

    /// <summary>ChannelType = wechat_event（仅事件通道匹配）。</summary>
    [Fact]
    public void TestHandler_ChannelType_IsWechatEvent()
    {
        var handler = new TestEventReplyHandler(new StubDomainUser());
        Assert.Equal("wechat_event", handler.ChannelType);
    }

    /// <summary>开关关闭（默认 false——P1-1 fail-safe）→ BuildReplyAsync 返回 null（不回复不拦截）。</summary>
    [Fact]
    public async Task TestHandler_DisabledByDefault_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<TestEventReplyHandler>();                 // 默认 _eventReplyOptions=false

        var reply = await handler.HandleAsync(new SsoChannelEventContext(
            new Dictionary<string, string?> { ["external_user_id"] = "openid-1" }),
            CancellationToken.None);

        Assert.Null(reply);
        Assert.False(_codes.Issued);
    }

    /// <summary>BaseUrl 未配置（P1-2/P2-1）→ 返回 null（不签无意义密封票据、不拦截业务 handler）。</summary>
    [Fact]
    public async Task TestHandler_BaseUrlMissing_ReturnsNull()
    {
        EnableTestHandler(baseUrl: string.Empty);                        // 开关开但 BaseUrl 空
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<TestEventReplyHandler>();

        var reply = await handler.HandleAsync(new SsoChannelEventContext(
            new Dictionary<string, string?> { ["external_user_id"] = "openid-1", ["uid"] = "u-1" }),
            CancellationToken.None);

        Assert.Null(reply);
        Assert.False(_codes.Issued);
    }

    /// <summary>开关开 + BaseUrl 配 → FederatedIdentity 载荷（UId/ChannelId/ExternalUserId）+ ExpectedClaimant null + 图文链接。</summary>
    [Fact]
    public async Task TestHandler_Enabled_IssuesFederatedIdentityPayload_ReturnsNewsArticle()
    {
        EnableTestHandler("https://api.example.com", "/feberation");
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<TestEventReplyHandler>();

        var reply = await handler.HandleAsync(new SsoChannelEventContext(
            new Dictionary<string, string?>
            {
                ["external_user_id"] = "openid-1",
                ["uid"] = "u-1",
                ["channel_id"] = "wx-1",
            }),
            CancellationToken.None);

        Assert.True(_codes.Issued);
        // P0-1：ExpectedClaimant null（可转让——测试端点 Redeem claimant=null）
        Assert.Null(_codes.LastClaimant);
        // P0-2：载荷 = FederatedIdentity（UId/ChannelId/ExternalUserId/ChannelType——测试端点反序列化对齐）
        Assert.NotNull(_codes.LastIssuedIdentity);
        Assert.Equal("u-1", _codes.LastIssuedIdentity!.UId);
        Assert.Equal("wx-1", _codes.LastIssuedIdentity.ChannelId);
        Assert.Equal("openid-1", _codes.LastIssuedIdentity.ExternalUserId);
        Assert.Equal("wechat_event", _codes.LastIssuedIdentity.ChannelType);
        // 图文链接（URL 指内置测试端点）
        Assert.NotNull(reply);
        var article = Assert.Single(reply.NewsArticles!);
        Assert.Contains("/test/accesscode/ac-1", article.Url);
        Assert.StartsWith("https://api.example.com/feberation", article.Url);
    }

    /// <summary>自定义 RoutePrefix → 图文链接 URL 用自定义前缀（对齐 FederationEndpointOptions 可配）。</summary>
    [Fact]
    public async Task TestHandler_CustomRoutePrefix_UrlUsesPrefix()
    {
        EnableTestHandler("https://api.example.com", "/sso");
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<TestEventReplyHandler>();

        var reply = await handler.HandleAsync(new SsoChannelEventContext(
            new Dictionary<string, string?> { ["external_user_id"] = "openid-1" }),
            CancellationToken.None);

        Assert.NotNull(reply);
        var article = Assert.Single(reply.NewsArticles!);
        Assert.StartsWith("https://api.example.com/sso/test/accesscode/", article.Url);
    }

    // ═══════════════════ 探针子类（模板方法测试） ═══════════════════

    /// <summary>模板方法探针子类——context.Parameters 含 "skip" → null；否则返回固定载荷。</summary>
    private sealed class ProbeEventReplyHandler : EventReplyHandlerBase
    {
        public override string ChannelType => "probe_event";

        public ProbeEventReplyHandler(IDomainUser user,
            Microsoft.Extensions.Options.IOptions<FederationEventReplyOptions>? options = null) : base(user, options)
        {
        }

        protected override Task<EventReplyPayload?> BuildReplyAsync(
            SsoChannelEventContext context, CancellationToken ct)
        {
            if (context.Parameters.TryGetValue("skip", out _))
                return Task.FromResult<EventReplyPayload?>(null);
            return Task.FromResult<EventReplyPayload?>(
                new EventReplyPayload(
                    Title: "探针回复",
                    Description: "探针载荷",
                    PayloadJson: """{"UId":"probe-uid","ChannelId":"probe-channel","ExternalUserId":"probe-ext"}""",
                    Ttl: TimeSpan.FromMinutes(5),
                    ExpectedClaimant: "claimant-1"));
        }
    }

    /// <summary>Fake 密封票据服务（ISealedTicketService&lt;FederatedIdentity&gt;——面① 记录 payload/claimant）。</summary>
    private sealed class FakeSealedTicketService : ISealedTicketService<FederatedIdentity>
    {
        public bool Issued { get; private set; }
        public FederatedIdentity? LastIssuedIdentity { get; private set; }
        public string? LastClaimant { get; private set; }

        public Task<SealedTicketResult> IssueAsync(FederatedIdentity payload, TimeSpan ttl, SealedTicketOptions? options = null, CancellationToken ct = default)
        {
            Issued = true;
            LastIssuedIdentity = payload;
            LastClaimant = options?.ExpectedClaimant;
            return Task.FromResult(new SealedTicketResult("ac-1", 120));
        }

        public Task<FederatedIdentity?> RedeemAsync(string ticket, TicketRedeemContext? context = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> CleanupExpiredAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
