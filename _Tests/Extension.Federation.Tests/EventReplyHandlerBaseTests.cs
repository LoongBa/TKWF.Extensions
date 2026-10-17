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
/// 2026-10-16 拆分命名 ADR109 D4 适配——面① 密封票据 <see cref="ISealedTicketService{T}"/>；
/// 2026-10-16 用户裁定——BaseUrl 空 → 相对路径（默认指向本服务同源），URL 形态
/// <c>{BaseUrl?}/{RoutePrefix}/{platformId}/test/{channelId}/{ticket}</c>（platformId 经 registry 反查）；
/// 2026-10-16 W2 方案 B——基类入口预查 <see cref="ChannelConfig"/> → <see cref="EventReplyHandlerBase.ReplyContext"/>
/// 传子类（零 registry 查询）+ 模板驱动两级模型第一级（<c>Extra["news_reply"]</c> 模板——占位符/自定义 link/disabled））。
/// <para>生产路径宿主（真实 DI：<see cref="TestHostBase.BindTestScope"/> + <c>DomainUser&lt;TestUserInfo&gt;.BindScope</c>
/// + <c>User.Use&lt;T&gt;()</c> AOP）+ Fake <see cref="ISealedTicketService{T}"/>（记录 IssueAsync payload/claimant/ttl）
/// + Fake <see cref="IChannelRegistry"/>（channelId → PlatformType + Extra）+ <see cref="IOptions{FederationEventReplyOptions}"/>
/// （开关/BaseUrl 配置）。</para>
/// <para>覆盖：① 模板方法——子类返回载荷 → 签密封票据 + 图文链接（URL 含 /{platformId}/test/{channelId}/{ticket}）；
/// ② null 透传——子类返回 null → HandleAsync null；③ 反查失败——事件 channel_id 不在 registry → 不回复；
/// ④ W2——ReplyContext 携带 ChannelConfig（Extra 可读，零 registry 查询）；⑤ 无 channel_id → null。
/// （TestEventReplyHandler 已随 2026-10-17 方案 docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md
/// 删除——能力迁移平台库 WechatTemplateEventReplyHandler/WechatProbeEventReplyHandler，其测试见
/// _Tests/Extension.Federation.WeChat.Tests；本测试类保留 EventReplyHandlerBase 基类签票→图文能力用例。）</para>
/// </summary>
public sealed class EventReplyHandlerBaseTests : TestHostBase
{
    private readonly FakeSealedTicketService _codes = new();
    private readonly FakeChannelRegistry _registry = new();
    private FederationEventReplyOptions _eventReplyOptions = new();

    protected override TestUserInfo CreateUser() => new("event-reply-tester", "事件回复基类测试用户");

    protected override void ConfigureHost(IServiceCollection services)
    {
        // Fake 密封票据服务（面①——记录 payload/claimant）
        services.AddScoped<ISealedTicketService<FederatedIdentity>>(_ => _codes);

        // Fake 通道注册表（channelId → PlatformType 反查——图文链接 {platformId} 段）
        services.AddScoped<IChannelRegistry>(_ => _registry);

        // 领域 Options（BaseUrl + RoutePrefix——按测试配置；TestEventReplyHandlerEnabled 开关已随
        // 2026-10-17 迁移平台库 WechatEventReplyOptions.ProbeEventReplyHandlerEnabled）
        services.AddOptions<FederationEventReplyOptions>();
        services.Configure<FederationEventReplyOptions>(o =>
        {
            o.TestTicketEndpointBaseUrl = _eventReplyOptions.TestTicketEndpointBaseUrl;
            o.RoutePrefix = _eventReplyOptions.RoutePrefix;
        });

        // 被测 handler：测试子类（集合注册——守卫工厂 ActivatorUtilities 构造）
        services.TryAddEnumerableConstructible<IEventReplyHandler, ProbeEventReplyHandler>();
    }

    private void EnableTicketUrl(string baseUrl = "https://api.example.com", string routePrefix = "/feberation")
    {
        _eventReplyOptions = new FederationEventReplyOptions
        {
            TestTicketEndpointBaseUrl = baseUrl,
            RoutePrefix = routePrefix,
        };
    }

    // ═══════════════════ 1. 模板方法（EventReplyHandlerBase.HandleAsync） ═══════════════════

    /// <summary>模板方法：子类返回载荷 → 签密封票据（FederatedIdentity 载荷反序列化 + ExpectedClaimant）+ 图文链接（URL 含测试端点）。</summary>
    [Fact]
    public async Task HandleAsync_SubclassPayload_IssuesAccessCode_ReturnsNewsArticle()
    {
        EnableTicketUrl();                                       // 配置 BaseUrl（验证完整 URL 拼接）
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<ProbeEventReplyHandler>();

        var reply = await handler.HandleAsync(new SsoChannelEventContext(
            new Dictionary<string, string?>
            {
                ["external_user_id"] = "openid-1",
                ["uid"] = "u-1",
                ["channel_id"] = "probe-channel",                  // 基类入口预查 cfg 必需
            }),
            CancellationToken.None);

        // 签密封票据被调用（FederatedIdentity 载荷反序列化 + ExpectedClaimant 透传）
        Assert.True(_codes.Issued);
        Assert.NotNull(_codes.LastIssuedIdentity);
        Assert.Equal("probe-uid", _codes.LastIssuedIdentity!.UId);
        Assert.Equal("probe-channel", _codes.LastIssuedIdentity.ChannelId);
        Assert.Equal("probe-ext", _codes.LastIssuedIdentity.ExternalUserId);
        Assert.Equal("claimant-1", _codes.LastClaimant);
        // 图文（news）链接返回（platformId 经基类入口预查 cfg：probe-channel → probe）
        Assert.NotNull(reply);
        Assert.NotNull(reply.NewsArticles);
        var article = Assert.Single(reply.NewsArticles);
        Assert.Contains("探针回复", article.Title);                        // 后缀 Extra 断言由 HandleAsync_ReplyContext_ExposesChannelExtra 覆盖
        Assert.Contains("/probe/test/probe-channel/ac-1", article.Url);      // Fake 返回 ticket="ac-1"
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
            new Dictionary<string, string?>
            {
                ["skip"] = "true",
                ["channel_id"] = "probe-channel",                  // 基类入口预查 cfg 必需
            }),
            CancellationToken.None);

        Assert.Null(reply);
        Assert.False(_codes.Issued);                                     // 未签密封票据
    }

    /// <summary>模板方法：W2 方案 B——ReplyContext 携带已解析 ChannelConfig（含 Extra 字典），子类可直接读（零 registry 查询）。</summary>
    [Fact]
    public async Task HandleAsync_ReplyContext_ExposesChannelExtra()
    {
        EnableTicketUrl();
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<ProbeEventReplyHandler>();

        // FakeChannelRegistry 为 probe-channel 配置 Extra["probe_flag"]="1"——子类 Probe 读取并回填 title 后缀
        var reply = await handler.HandleAsync(new SsoChannelEventContext(
            new Dictionary<string, string?>
            {
                ["external_user_id"] = "openid-1",
                ["uid"] = "u-1",
                ["channel_id"] = "probe-channel",
            }),
            CancellationToken.None);

        Assert.NotNull(reply);
        var article = Assert.Single(reply.NewsArticles!);
        Assert.Contains("探针回复", article.Title);
        Assert.EndsWith("[extra]", article.Title);                        // Probe 读 ReplyContext.Channel.Extra
    }

    /// <summary>模板方法：事件无 channel_id（基类入口预查必需）→ 直接 null（不触达子类）。</summary>
    [Fact]
    public async Task HandleAsync_NoChannelId_ReturnsNull()
    {
        EnableTicketUrl();
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<ProbeEventReplyHandler>();

        var reply = await handler.HandleAsync(new SsoChannelEventContext(
            new Dictionary<string, string?> { ["external_user_id"] = "openid-1" }),   // 无 channel_id
            CancellationToken.None);

        Assert.Null(reply);
        Assert.False(_codes.Issued);
    }

    /// <summary>模板方法：载荷 ChannelId 不在 registry（反查失败——无法构造有效图文链接）→ 不签票据不回复
    /// （first-non-null-wins 语义——不拦截其它 handler）。</summary>
    [Fact]
    public async Task HandleAsync_ChannelNotInRegistry_ReturnsNull()
    {
        EnableTicketUrl();                                             // BaseUrl 配
        using var _ = this;
        var (_, user) = BindTestScope();
        // ProbeEventReplyHandler（channel_id=unknown-channel——registry 无映射）验证反查失败语义
        var handler = user.Use<ProbeEventReplyHandler>();
        var reply = await handler.HandleAsync(new SsoChannelEventContext(
            new Dictionary<string, string?> { ["external_user_id"] = "openid-1", ["channel_id"] = "unknown-channel" }),
            CancellationToken.None);

        Assert.Null(reply);
        Assert.False(_codes.Issued);                                     // 反查失败 → 未签密封票据
    }

    // ═══════════════════ 探针子类（模板方法测试） ═══════════════════

    /// <summary>模板方法探针子类——context.Parameters 含 "skip" → null；否则返回固定载荷；
    /// 读 <see cref="ReplyContext.Channel.Extra"/>（W2 方案 B 断言——Extra["probe_flag"]="1" → title 追加 [extra]）。</summary>
    private sealed class ProbeEventReplyHandler : EventReplyHandlerBase
    {
        public override string ChannelType => "probe_event";

        public ProbeEventReplyHandler(IDomainUser user,
            Microsoft.Extensions.Options.IOptions<FederationEventReplyOptions>? options = null) : base(user, options)
        {
        }

        protected override Task<EventReplyPayload?> BuildReplyAsync(
            ReplyContext context, CancellationToken ct)
        {
            if (context.Context.Parameters.TryGetValue("skip", out _))
                return Task.FromResult<EventReplyPayload?>(null);
            var extraSuffix = context.Channel.Extra.TryGetValue("probe_flag", out var flag) && flag == "1"
                ? "[extra]"
                : string.Empty;
            return Task.FromResult<EventReplyPayload?>(
                new EventReplyPayload(
                    Title: $"探针回复{extraSuffix}",
                    Description: "探针载荷",
                    PayloadJson: """{"UId":"probe-uid","ChannelId":"probe-channel","ExternalUserId":"probe-ext"}""",
                    Ttl: TimeSpan.FromMinutes(5),
                    ExpectedClaimant: "claimant-1"));
        }
    }

    /// <summary>Fake 密封票据服务（ISealedTicketService&lt;FederatedIdentity&gt;——面① 记录 payload/claimant/ttl）。</summary>
    private sealed class FakeSealedTicketService : ISealedTicketService<FederatedIdentity>
    {
        public bool Issued { get; private set; }
        public FederatedIdentity? LastIssuedIdentity { get; private set; }
        public string? LastClaimant { get; private set; }
        public int? LastTtlSeconds { get; private set; }

        public Task<SealedTicketResult> IssueAsync(FederatedIdentity payload, TimeSpan ttl, SealedTicketOptions? options = null, CancellationToken ct = default)
        {
            Issued = true;
            LastIssuedIdentity = payload;
            LastClaimant = options?.ExpectedClaimant;
            LastTtlSeconds = (int)ttl.TotalSeconds;
            return Task.FromResult(new SealedTicketResult("ac-1", 120));
        }

        public Task<FederatedIdentity?> RedeemAsync(string ticket, TicketRedeemContext? context = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<int> CleanupExpiredAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>Fake 通道注册表（channelId → PlatformType 反查——图文链接 {platformId} 段；
    /// probe-channel → probe / wx-1 → wechat / wechat-mp → wechat；probe-channel 带 Extra["probe_flag"]——W2 断言）。</summary>
    private sealed class FakeChannelRegistry : IChannelRegistry
    {
        public List<ChannelConfig> Channels { get; set; } = new()
        {
            new() { ChannelId = "probe-channel", PlatformType = "probe", IsEnabled = true,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal) { ["probe_flag"] = "1" } },
            new() { ChannelId = "wx-1", PlatformType = "wechat", IsEnabled = true },
            new() { ChannelId = "wechat-mp", PlatformType = "wechat", IsEnabled = true },
        };

        public Task<ChannelConfig?> GetAsync(string channelId, CancellationToken ct = default)
            => Task.FromResult(Channels.FirstOrDefault(c => c.ChannelId == channelId));

        public Task<ChannelConfig?> GetByAliasOrIdAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Channels.FirstOrDefault(c => c.ChannelId == key || c.Alias == key));

        public Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelConfig>>(Channels);

        public Task<ChannelConfig?> GetDefaultAsync(CancellationToken ct = default)
            => Task.FromResult(Channels.FirstOrDefault(c => c.IsDefault) ?? Channels.FirstOrDefault(c => c.IsEnabled));
    }
}
