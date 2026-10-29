using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Ticket;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// I2-8 免登入口测试（方案 §10 测试矩阵——微信免登闭环 {ticket} 缺口）——
/// <see cref="ILoginEntryService"/> 契约 + <see cref="LoginEntryService"/> 实现 fail-closed 全分支 +
/// <see cref="EventReplyHandlerBase"/> URL 组装委托逐字节等价（<see cref="LoginEntryService.CombineUrl"/>）。
/// <para>生产路径宿主：真实 FederationExtensionInitializer.ConfigureServices（AddConstructibleService 注册
/// <see cref="ILoginEntryService"/>）+ Fake <see cref="ISealedTicketService&lt;FederatedIdentity&gt;"/>
/// （可签发固定 ticket——FederationEndpointHost.FakeSealedTicketService 的 IssueAsync 抛 NotSupported，不适用，
/// 本测试自建 <see cref="IssuingTicketService"/>）+ Fake <see cref="ISsoChannelFactory"/>（静默授权分支）。
/// <c>User.Use&lt;ILoginEntryService&gt;()</c> AOP 路径。</para>
/// </summary>
public sealed class LoginEntryServiceTests : TestHostBase
{
    private readonly IssuingTicketService _ticket = new();
    private readonly FakeOAuthChannel _channel = new();

    protected override TestUserInfo CreateUser() => new("login-entry-user", "免登入口测试用户");

    protected override void ConfigureHost(IServiceCollection services)
    {
        new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddScoped<ISealedTicketService<FederatedTicketPayload>>(_ => _ticket);
        services.AddScoped<ISsoChannelFactory>(_ => new FakeFactory(_channel));
    }

    // ── 1. 开关关闭（fail-closed 第一层——默认 Enabled=false 显式启用）──

    /// <summary>默认未启用（Enabled=false）→ BuildTicketUrlAsync null（不生成免登链接，§3.8 fail-closed）。</summary>
    [Fact]
    public async Task BuildTicketUrl_Disabled_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var url = await user.Use<ILoginEntryService>().BuildTicketUrlAsync("mp-1", "wechat_event", "openid-1", ct: CancellationToken.None);

        Assert.Null(url);
        Assert.Empty(_ticket.Issued);   // 未启用 → 不签票
    }

    // ── 2. 基址缺失（相对路径兜底—— CombineUrl baseUrl 空 → /{routePrefix}/{path}）──

    /// <summary>启用但 ChannelBaseUrls / TestTicketEndpointBaseUrl / requestBaseUrl 均空 → 签票 + 相对路径 URL
    /// （CombineUrl baseUrl 空 → 本站点同源；微信图文 link 实际链路恒有 requestBaseUrl——本测试锁相对路径兜底语义）。</summary>
    [Fact]
    public async Task BuildTicketUrl_NoBaseUrl_ReturnsRelativeUrl()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
        {
            services.Configure<FederationLoginEntryOptions>(o => o.Enabled = true);
        });
        _ticket.IssueResult = "ticket-abc-123";

        var url = await user.Use<ILoginEntryService>().BuildTicketUrlAsync("mp-1", "wechat_event", "openid-1", ct: CancellationToken.None);

        Assert.Equal("/feberation/login/ticket?ticket=ticket-abc-123", url);
        Assert.Single(_ticket.Issued);   // 相对路径兜底仍签票（票据可核销——落地在本服务同源）
    }

    // ── 3. 正常路径（启用 + 基址 → 签票 + URL 组装）──

    /// <summary>启用 + TestTicketEndpointBaseUrl → 签票（null-claimant——SealedTicketOptions 缺省）
    /// + URL = {baseUrl}/{RoutePrefix}/login/ticket?ticket={issued}（§6.2 目标链路）。</summary>
    [Fact]
    public async Task BuildTicketUrl_EnabledWithBaseUrl_IssuesTicket_AssemblesUrl()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
        {
            services.Configure<FederationLoginEntryOptions>(o =>
            {
                o.Enabled = true;
                o.ChannelBaseUrls["mp-1"] = "https://sso.example.com/";
            });
        });
        _ticket.IssueResult = "ticket-abc-123";

        var url = await user.Use<ILoginEntryService>().BuildTicketUrlAsync("mp-1", "wechat_event", "openid-1", ct: CancellationToken.None);

        Assert.Equal("https://sso.example.com/feberation/login/ticket?ticket=ticket-abc-123", url);
        // 签票载荷（FederatedTicketPayload——D03 1.3：ChannelId/ExternalUserId/协议类型双写 + UId 空 = null-claimant 语义）
        var issued = Assert.Single(_ticket.Issued);
        Assert.Equal("mp-1", issued.Payload.ChannelId);
        Assert.Equal("openid-1", issued.Payload.ExternalUserId);
        Assert.Equal("wechat_event", issued.Payload.Extensions?[FederatedTicketPayloadExtensions.ProtocolTypeKey]);
        Assert.Null(issued.Payload.PlatformUid);    // uId 未传 → null-claimant（免登编排映射/建号兜底）
        Assert.Equal("mp_subscription", issued.Payload.ChannelType);   // 形态回退（wechat_event → mp_subscription）
    }

    /// <summary>ttlSeconds 显式传入 → 覆盖 Options.TicketTtlSeconds（模板 TTL 优先语义）。</summary>
    [Fact]
    public async Task BuildTicketUrl_TtlOverride_UsesExplicit()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
        {
            services.Configure<FederationLoginEntryOptions>(o =>
            {
                o.Enabled = true;
                o.ChannelBaseUrls["mp-1"] = "https://sso.example.com";
                o.TicketTtlSeconds = 120;
            });
        });
        _ticket.IssueResult = "ticket-ttl";

        await user.Use<ILoginEntryService>().BuildTicketUrlAsync("mp-1", "wechat_event", "openid-1", ttlSeconds: 42, ct: CancellationToken.None);

        var issued = Assert.Single(_ticket.Issued);
        Assert.Equal(42, issued.Ttl.TotalSeconds);    }

    // ── 4. 签票失败（fail-closed——票据后端不可用 → null 不阻断回复）──

    /// <summary>IssueAsync 抛异常 → null + 不抛（§3.8 ③——票据后端不可用降级）。</summary>
    [Fact]
    public async Task BuildTicketUrl_IssueThrows_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
        {
            services.Configure<FederationLoginEntryOptions>(o =>
            {
                o.Enabled = true;
                o.ChannelBaseUrls["mp-1"] = "https://sso.example.com";
            });
        });
        _ticket.ThrowOnIssue = true;

        var url = await user.Use<ILoginEntryService>().BuildTicketUrlAsync("mp-1", "wechat_event", "openid-1", ct: CancellationToken.None);

        Assert.Null(url);
    }

    // ── 5. 静默授权（snsapi_base——委托通道 BuildAuthorizeUrlAsync(Scope)）──

    /// <summary>通道支持授权 → 返回通道 URL（Scope=snsapi_base 静默授权入口）。</summary>
    [Fact]
    public async Task BuildSilentAuthorizeUrl_ChannelSupports_ReturnsUrl()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        _channel.AuthorizeResult = new SsoChannelAuthorizeResult("https://open.weixin.qq.com/connect/oauth2/authorize?appid=wx&scope=snsapi_base", null, null, null);

        var url = await user.Use<ILoginEntryService>().BuildSilentAuthorizeUrlAsync("mp-1", "https://sso.example.com/feberation/wechat/oauth/mp-1/callback", ct: CancellationToken.None);

        Assert.Equal("https://open.weixin.qq.com/connect/oauth2/authorize?appid=wx&scope=snsapi_base", url);
        Assert.Equal("snsapi_base", _channel.LastScope);
    }

    /// <summary>通道不支持授权（BuildAuthorizeUrlAsync DIM null）→ null（fail-closed 降级不抛）。</summary>
    [Fact]
    public async Task BuildSilentAuthorizeUrl_ChannelNotSupport_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var url = await user.Use<ILoginEntryService>().BuildSilentAuthorizeUrlAsync("mp-1", "https://cb.example.com", ct: CancellationToken.None);

        Assert.Null(url);
    }

    // ── 6. URL 组装逐字节等价（CombineUrl——EventReplyHandlerBase 委托复用）──

    /// <summary>CombineUrl：baseUrl 空 → 相对路径 /{routePrefix}/{path}；baseUrl 有 → {baseUrl}/{routePrefix}/{path}（逐字节）。</summary>
    [Theory]
    [InlineData(null, "/feberation", "wechat/test/mp-1/t-1", "/feberation/wechat/test/mp-1/t-1")]
    [InlineData("", "/feberation", "wechat/test/mp-1/t-1", "/feberation/wechat/test/mp-1/t-1")]
    [InlineData("https://api.example.com", "/feberation", "wechat/test/mp-1/t-1", "https://api.example.com/feberation/wechat/test/mp-1/t-1")]
    [InlineData("https://api.example.com/", "/feberation/", "wechat/test/mp-1/t-1", "https://api.example.com/feberation/wechat/test/mp-1/t-1")]
    public void CombineUrl_AssemblesByteIdentical(string? baseUrl, string routePrefix, string path, string expected)
    {
        Assert.Equal(expected, LoginEntryService.CombineUrl(baseUrl, routePrefix, path));
    }

    // ── 7. 启动期 fail-fast 校验（方案 §6.4——TicketTtlSeconds ≤ MaxTicketTtlSeconds）──

    /// <summary>TicketTtlSeconds 超过安全上限 → Options 校验抛 OptionsValidationException（启动期 fail-fast）。</summary>
    [Fact]
    public void LoginEntryOptions_TtlExceedsMax_FailsValidation()
    {
        using var _ = this;
        var (provider, _) = BindTestScope(services =>
        {
            services.Configure<FederationLoginEntryOptions>(o =>
            {
                o.TicketTtlSeconds = 9999;
                o.MaxTicketTtlSeconds = 300;
            });
        });

        Assert.Throws<OptionsValidationException>(
            () => provider.GetRequiredService<IOptions<FederationLoginEntryOptions>>().Value);
    }

    /// <summary>默认配置（120 ≤ 120）通过校验（P1-2——默认值对齐 TrustCenter SealedTicketExpirationSeconds 120）。</summary>
    [Fact]
    public void LoginEntryOptions_DefaultTtl_PassesValidation()
    {
        using var _ = this;
        var (provider, _) = BindTestScope();

        var options = provider.GetRequiredService<IOptions<FederationLoginEntryOptions>>().Value;
        Assert.Equal(120, options.TicketTtlSeconds);
        Assert.Equal(120, options.MaxTicketTtlSeconds);
        Assert.False(options.Enabled);
    }

    // ── 测试设施 ──

    /// <summary>可签发票务服务（Fake ISealedTicketService&lt;FederatedIdentity&gt;——返回固定 ticket + 记录签发载荷）。</summary>
    private sealed class IssuingTicketService : ISealedTicketService<FederatedTicketPayload>
    {
        public string? IssueResult { get; set; } = "ticket-fixed";
        public bool ThrowOnIssue { get; set; }
        public List<(FederatedTicketPayload Payload, TimeSpan Ttl)> Issued { get; } = [];

        public Task<SealedTicketResult> IssueAsync(FederatedTicketPayload payload, TimeSpan ttl, SealedTicketOptions? options = null, CancellationToken ct = default)
        {
            if (ThrowOnIssue) throw new InvalidOperationException("票据后端不可用模拟");
            Issued.Add((payload, ttl));
            return Task.FromResult(new SealedTicketResult(IssueResult ?? "ticket-fixed", 300));
        }

        public Task<FederatedTicketPayload?> RedeemAsync(string ticket, TicketRedeemContext? context = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> CleanupExpiredAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>通道工厂 Fake（CreateAsync 返回 Fake 通道——静默授权分支测试）。</summary>
    private sealed class FakeFactory : ISsoChannelFactory
    {
        private readonly ISsoChannel _channel;
        public FakeFactory(ISsoChannel channel) => _channel = channel;

        public Task<ISsoChannel?> CreateAsync(string channelId, string? channelType = null, CancellationToken ct = default)
            => Task.FromResult<ISsoChannel?>(_channel);

        public Task<ISsoChannel?> CreateDefaultAsync(CancellationToken ct = default)
            => Task.FromResult<ISsoChannel?>(_channel);
    }

    /// <summary>静默授权通道 Fake（BuildAuthorizeUrlAsync 可配置；记录 Scope）。</summary>
    private sealed class FakeOAuthChannel : ISsoChannel
    {
        public string ChannelType => "wechat_oauth";
        public string ChannelId => "mp-1";
        public SsoChannelAuthorizeResult? AuthorizeResult { get; set; }
        public string? LastScope { get; private set; }

        public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
            => Task.FromResult(new SsoChannelAuthResult(false, null, "NOT_IMPLEMENTED", 0));

        public Task<ChannelEventOutcome> HandleEventAsync(ChannelInboundContext context, CancellationToken ct = default)
            => Task.FromResult(ChannelEventOutcome.NotApplicable);

        public Task<string?> VerifyUrlAsync(ChannelInboundContext context, CancellationToken ct = default)
            => Task.FromResult<string?>(null);

        public Task<SsoChannelAuthorizeResult?> BuildAuthorizeUrlAsync(SsoChannelAuthorizeContext context, CancellationToken ct = default)
        {
            LastScope = context.Scope;
            return Task.FromResult(AuthorizeResult);
        }
    }
}
