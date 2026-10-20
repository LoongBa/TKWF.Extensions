using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Ticket;
using TKW.Framework.Domain.Web.Extensions;
using TKW.Framework.Domain.Web.Hosting;
using TKW.Framework.Enumerations;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// 测试项目共享 Web 宿主（T3 装配面）——<c>DomainHost&lt;TUserInfo&gt;.Root</c> 进程内单例
/// （"DomainHost 不能重复初始化"），全测试项目**仅可建 1 个 Web 宿主**——FederationWebExtension
/// 端点冒烟（FederationWebExtensionTests）专用。
/// <para>含：<see cref="FederationWebExtension{TUserInfo}"/>（端点映射）+ Fake 门面（端点行为断言——
/// 普通 Scoped 经 RegisterServices 替换守卫工厂——ISsoLogin/ISsoChannelFactory/ISsoSubAppBridge 由 Federation
/// 初始器 AddConstructibleService 注册后替换；IToken2Service 契约在 TrustCenter.Abstractions（经 Federation
/// 传递），测试项目不引 TrustCenter 主包 → AddScoped 直接注册 Fake）+ FakeSsoChannel（AuthenticateAsync 断言
/// SsoChannelAuthContext 参数透传）。宿主参数对齐 AuthCenterEndpointHost 先例：Development
/// （ValidateScopes=true 回归哨兵）+ TKWDomain:Name + UseNoEntityDAC + ExcludedPathPrefixes /sso。</para>
/// <para><b>trust/issue 已认证帧（2026-10-11）</b>：BeforeRouting 哨兵中间件读 <c>X-Test-Auth-User</c> header
/// → <c>guest.LoginAsUserAsync</c>（模拟 AuthCenterWebExtension 中间件写入已认证 DomainUser——真实生产经
/// Bearer 验签 + RestoreUser；测试经 header 标记）。</para>
/// </summary>
internal static class FederationEndpointHost
{
    public static TestServer Server => _lazy.Value;
    private static readonly Lazy<TestServer> _lazy = new(CreateServer);

    // ─── Fake 门面（端点测试断言 Invoked/结果） ───
    public static FakeSsoLogin Login { get; } = new();
    public static FakeSsoChannelFactory Factory { get; } = new();
    public static FakeSsoChannel Channel { get; } = new("wechat-mp");
    public static FakeToken2Service Token2 { get; } = new();
    public static FakeSsoSubAppBridge Bridge { get; } = new();
    public static FakeChannelRegistry Registry { get; } = new();
    public static FakeExternalIdentityService ChannelMap { get; } = new();
    public static FakeEventReplyHandler EventHandler1 { get; } = new("handler-1");
    public static FakeEventReplyHandler EventHandler2 { get; } = new("handler-2");
    public static FakeEventOrchestrator Orchestrator { get; } = new();
    public static FakeSealedTicketService Ticket { get; } = new();

    /// <summary>共享宿主状态复位（跨用例防污染——负向/正向断言前置调用，对齐 AuthCenter V0.8.0 复位范式）。</summary>
    public static void Reset()
    {
        Login.Reset();
        Factory.Reset();
        Channel.Reset();
        Channel.Result = new SsoChannelAuthResult(true, "ext-100", null, 2);
        Factory.Channel = Channel;
        Login.Factory = Factory;    // Fake 登录编排复用宿主工厂（共享实例——默认通道/选区走同一通道源）
        Token2.Reset();
        Bridge.Reset();
        Registry.Reset();
        ChannelMap.Reset();
        EventHandler1.Reset();
        EventHandler2.Reset();
        Orchestrator.Reset();
        Ticket.Reset();
    }

    private static TestServer CreateServer()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Environment.EnvironmentName = "Development";   // ValidateScopes=true 回归哨兵
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["TKWDomain:Name"] = "FederationEndpointSmoke"
        });

        var app = builder.ConfigWebAppDomain<TestUserInfo, ConsumerHostInitializer, DomainWebOptions>(configure: cfg =>
        {
            cfg.UseNoEntityDAC();                               // 冒烟无数据存取（门面 Fake）
            cfg.HttpAuth.ExcludedPathPrefixes.Add("/feberation"); // 匿名端点免认证双保险（路由命名空间 2026-10-11）
        })
        .RegisterServices((svc, _) =>
        {
            svc.Replace(ServiceDescriptor.Scoped<ISsoLogin>(_ => Login));
            svc.Replace(ServiceDescriptor.Scoped<ISsoChannelFactory>(_ => Factory));
            // IChannelRegistry：handler ResolveChannelAsync（平台段校验 + B 守卫）依赖——Federation 初始器
            // AddConstructibleService 注册真实 Composite 后替换为普通 Scoped Fake（单通道 wechat-mp / 可配多通道）
            svc.Replace(ServiceDescriptor.Scoped<IChannelRegistry>(_ => Registry));
            // ISsoSubAppBridge：子应用消费方委托门面（2026-10-11）——Federation 初始器 AddConstructibleService
            // 注册后替换为普通 Scoped Fake（端点行为断言——Start/CompleteOAuth/TrustIssue/Claim 请求透传）
            svc.Replace(ServiceDescriptor.Scoped<ISsoSubAppBridge>(_ => Bridge));
            // IToken2Service：契约在 TrustCenter.Abstractions（经 Federation 传递），测试项目未引 TrustCenter
            // 主包 → 无既有注册，AddScoped 直接注册 Fake（GetGuest.Use<T>() → GetRequiredService 解析）
            svc.AddScoped<IToken2Service>(_ => Token2);
            // IExternalIdentityService：契约在 AuthCenter.Abstractions（uid 只读查询——事件编排 F7 消费；V0.11.0 T4 由
            // ISsoChannelMapService 收口），测试项目未引 AuthCenter 主包 → AddScoped 直接注册 Fake
            // （GetByExternalIdAsync 断言 / LinkAsync 不调）
            svc.AddScoped<IExternalIdentityService>(_ => ChannelMap);
            // IEventReplyHandler 集合（事件编排 first-non-null-wins F8——真实 EventOrchestrator ctor 注入；
            // 测试注册 2 个 Fake handler 供 first-non-null-wins / uid 富化断言）
            svc.AddScoped<IEventReplyHandler>(_ => EventHandler1);
            svc.AddScoped<IEventReplyHandler>(_ => EventHandler2);
            // IEventOrchestrator：事件编排门面（E3——Federation 初始器 AddConstructibleService 注册真实守卫工厂；
            // 测试宿主 Replace 为普通 Scoped Fake 编排（复刻真实行为：通道 HandleEventAsync + uid 只读富化 +
            // handler first-non-null-wins——无守卫依赖，规避守卫工厂与 TestMetaContext 空上下文的发现不确定性）
            svc.Replace(ServiceDescriptor.Scoped<IEventOrchestrator>(_ => Orchestrator));
            // ISealedTicketService<FederatedIdentity>：面① 密封票据（测试端点 HandleTestTicket RedeemAsync 依赖）——
            // 契约在主框架 TKW.Framework.Domain.Ticket；宿主仅启用 Federation（无 TrustCenter 初始器注册）→ AddScoped 直注 Fake
            svc.AddScoped<ISealedTicketService<FederatedIdentity>>(_ => Ticket);
        })
        .UseWebSession()
        .UseWebExtensions<TestUserInfo>(e =>
        {
            // 测试端点开关打开（默认 false——测试端点只在这些用例中冒烟；其余端点不受影响）
            e.Add<FederationWebExtension<TestUserInfo>>(x => x.ConfigureOptions = o => o.TestTicketEndpointEnabled = true);
        })
        .BeforeRouting((app, _) =>
        {
            // 测试哨兵中间件：trust/issue 已认证帧注入（模拟 AuthCenterWebExtension 中间件写入
            // Items["DomainUser"] 已认证用户——真实生产经 Bearer 验签 + RestoreUser；测试经 header 标记）。
            app.Use(async (ctx, next) =>
            {
                if (ctx.Request.Headers.TryGetValue("X-Test-Auth-User", out var uid)
                    && ctx.Items["DomainUser"] is DomainUser<TestUserInfo> guest)
                {
                    await guest.LoginAsUserAsync(new TestUserInfo(uid.ToString(), "授权用户"), EnumLoginFrom.PcWeb);
                }
                await next(ctx);
            });
        })
        .UseAspNetCoreRouting()
        .AfterRouting((_, _) => { })
        .Build();

        app.StartAsync().GetAwaiter().GetResult();
        return app.GetTestServer();
    }

    // ─── Fake 门面 / 通道 ───

    /// <summary>Fake 登录编排门面——镜像真实 SsoLogin 编排（经 Fake 工厂构造通道 → AuthenticateAsync），
    /// 供端点参数透传断言（SsoChannelAuthContext 到通道）。</summary>
    internal sealed class FakeSsoLogin : ISsoLogin
    {
        public FakeSsoChannelFactory Factory { get; set; } = new();
        public bool Invoked { get; private set; }
        public bool DefaultInvoked { get; private set; }
        public string? LastChannelId { get; private set; }
        public SsoChannelAuthContext? LastContext { get; private set; }

        public void Reset() { Invoked = false; DefaultInvoked = false; LastChannelId = null; LastContext = null; }

        public async Task<SsoChannelAuthResult> LoginAsync(string channelId, SsoChannelAuthContext context, CancellationToken ct = default)
        {
            Invoked = true;
            LastChannelId = channelId;
            LastContext = context;
            var channel = await Factory.CreateAsync(channelId, null, ct);
            return channel is null
                ? new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0)
                : await channel.AuthenticateAsync(context, ct);
        }

        public async Task<SsoChannelAuthResult> LoginDefaultAsync(SsoChannelAuthContext context, CancellationToken ct = default)
        {
            DefaultInvoked = true;
            LastContext = context;
            var channel = await Factory.CreateDefaultAsync(ct);
            return channel is null
                ? new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0)
                : await channel.AuthenticateAsync(context, ct);
        }
    }

    /// <summary>Fake 通道工厂——按 ChannelId 精确匹配返回 <see cref="Channel"/>（未知 id → null → CHANNEL_NOT_FOUND 404）；
    /// 记录 LastChannelType（event 通道类型推导断言）。</summary>
    internal sealed class FakeSsoChannelFactory : ISsoChannelFactory
    {
        public FakeSsoChannel? Channel { get; set; }
        public bool CreateInvoked { get; private set; }
        public string? LastChannelId { get; private set; }
        public string? LastChannelType { get; private set; }

        public void Reset() { CreateInvoked = false; LastChannelId = null; LastChannelType = null; }

        public Task<ISsoChannel?> CreateAsync(string channelId, string? channelType = null, CancellationToken ct = default)
        {
            CreateInvoked = true;
            LastChannelId = channelId;
            LastChannelType = channelType;
            return Task.FromResult(Channel is not null && string.Equals(Channel.ChannelId, channelId, StringComparison.Ordinal)
                ? (ISsoChannel?)Channel
                : null);
        }

        public Task<ISsoChannel?> CreateDefaultAsync(CancellationToken ct = default)
            => Task.FromResult(Channel is null ? null : (ISsoChannel)Channel);
    }

    /// <summary>Fake 通道注册表——handler ResolveChannelAsync（平台段校验 + B 守卫）数据源：
    /// 默认单通道（wechat-mp / PlatformType=wechat / IsEnabled）——可配置多通道（B 守卫用例）。
    /// <see cref="GetAllAsync"/> 返回 IsEnabled 通道；<see cref="GetByAliasOrIdAsync"/> 按 ChannelId/Alias 双键；
    /// <see cref="GetDefaultAsync"/> 返回 IsDefault → 首个 IsEnabled。</summary>
    internal sealed class FakeChannelRegistry : IChannelRegistry
    {
        public List<ChannelConfig> Channels { get; set; } = new()
        {
            new() { ChannelId = "wechat-mp", PlatformType = "wechat", IsEnabled = true },
        };

        public void Reset() => Channels = new List<ChannelConfig>
        {
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

    /// <summary>Fake 通道——AuthenticateAsync 记录上下文 + 返回可配置 Result（默认成功 ext-100/AuthLevel 2）；
    /// HandleEventAsync 记录事件上下文 + 返回可配置 EventReply（默认 null——编排转 handler 委托，first-non-null-wins）；
    /// VerifyUrlAsync（URL 验证握手双模式验签）记录执行 + 回显可配置（默认原样 query echostr）。</summary>
    internal sealed class FakeSsoChannel : ISsoChannel
    {
        public FakeSsoChannel(string channelId) => ChannelId = channelId;

        public string ChannelType => "fake_oauth";
        public string ChannelId { get; }
        public SsoChannelAuthResult Result { get; set; } = new(true, "ext-100", null, 2);
        public EventReplyResult? EventReply { get; set; }
        // I2：HandleEventAsync 默认产 Parsed(subscribe)——等价旧「通道返回 null → 编排走 handler」；
        // 非事件通道语义用例显式设 NotApplicable/Rejected
        public ChannelEventOutcome Outcome { get; set; } = ChannelEventOutcome.Parsed(new ChannelEvent
        {
            ChannelId = "wechat-mp",
            ChannelType = "wechat_event",
            ExternalUserId = "ext-100",
            EventType = "subscribe",
            Attributes = new System.Collections.Generic.Dictionary<string, string?> { ["msg_type"] = "event", ["event"] = "subscribe" },
        });
        public SsoChannelAuthContext? LastContext { get; private set; }
        public ChannelInboundContext? LastEventContext { get; private set; }
        public bool Authenticated { get; private set; }
        public bool UrlVerified { get; private set; }
        public string? UrlVerifyEcho { get; set; }

        public void Reset()
        {
            LastContext = null; LastEventContext = null; Authenticated = false; EventReply = null;
            UrlVerified = false; UrlVerifyEcho = null;
        }

        public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
        {
            Authenticated = true;
            LastContext = context;
            return Task.FromResult(Result);
        }

        public Task<ChannelEventOutcome> HandleEventAsync(ChannelInboundContext context, CancellationToken ct = default)
        {
            LastEventContext = context;
            // I2 语义层：Fake 通道默认产 NotApplicable（非事件通道语义——编排跳过 handler）；
            // 断言事件语义的用例经 FakeSsoChannel.Outcome 配置
            return Task.FromResult(Outcome);
        }

        public Task<string?> VerifyUrlAsync(ChannelInboundContext context, CancellationToken ct = default)
        {
            UrlVerified = true;
            LastEventContext = context;
            // 回显可配置（默认取 query echostr 原样——验签通过语义）
            var echo = UrlVerifyEcho ?? (context.Parameters.TryGetValue("echostr", out var e) ? e : null);
            return Task.FromResult(echo);
        }
    }

    /// <summary>Fake token2 服务（信任内核契约——测试项目不引 TrustCenter 主包，Web 层直注 Fake）——JWKS 分发断言。</summary>
    internal sealed class FakeToken2Service : IToken2Service
    {
        public string JwksJson { get; set; } = """{"keys":[{"kid":"sso-key-1","kty":"EC","crv":"P-256"}]}""";
        public bool JwksInvoked { get; private set; }

        public void Reset() => JwksInvoked = false;

        public string GetJwksJson()
        {
            JwksInvoked = true;
            return JwksJson;
        }

        public Task<Token2IssueResult> IssueToken2Async(Token2IssueRequest request, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<Token2ValidationResult> ValidateToken2Async(string token, CancellationToken ct = default)
            => throw new NotSupportedException();
    }

    /// <summary>Fake 子应用消费方桥接门面（2026-10-11）——端点行为断言：请求透传（CallbackBaseUrl 服务端填充/
    /// 回调参数/channelId/platformUid/claim 请求）+ 可配置结果（Start/Callback/TrustIssue/Claim，Reset 重置默认成功）。</summary>
    internal sealed class FakeSsoSubAppBridge : ISsoSubAppBridge
    {
        public AuthorizeStartResult StartResult { get; set; } =
            new(true, "https://open.weixin.qq.com/connect/oauth2/authorize?appid=wx_test&scope=snsapi_base#wechat_redirect", "state-ticket", null);
        public OAuthCallbackResult CallbackResult { get; set; } =
            new(true, "https://sub.example.com/cb?code=ac-1&state=sub-st", "sub-st", null);
        public TrustIssueResult TrustIssueResult { get; set; } =
            new(true, "https://sub.example.com/cb?code=ac-2", "ac-2", null);
        public IdentityClaimResult ClaimResult { get; set; } =
            new(true, "federated", "wx-1", "ext-100", null, null);

        public AuthorizeStartRequest? LastStartRequest { get; private set; }
        public string? LastCallbackChannelId { get; private set; }
        public IReadOnlyDictionary<string, string?>? LastCallbackParams { get; private set; }
        public TrustIssueRequest? LastTrustIssueRequest { get; private set; }
        public string? LastPlatformUid { get; private set; }
        public IdentityClaimRequest? LastClaimRequest { get; private set; }

        public void Reset()
        {
            LastStartRequest = null;
            LastCallbackChannelId = null;
            LastCallbackParams = null;
            LastTrustIssueRequest = null;
            LastPlatformUid = null;
            LastClaimRequest = null;
            // 默认成功结果（各用例前置覆盖 FailReason 负向场景）
            StartResult = new(true, "https://open.weixin.qq.com/connect/oauth2/authorize?appid=wx_test&scope=snsapi_base#wechat_redirect", "state-ticket", null);
            CallbackResult = new(true, "https://sub.example.com/cb?code=ac-1&state=sub-st", "sub-st", null);
            TrustIssueResult = new(true, "https://sub.example.com/cb?code=ac-2", "ac-2", null);
            ClaimResult = new(true, "federated", "wx-1", "ext-100", null, null);
        }

        public Task<AuthorizeStartResult> StartAsync(AuthorizeStartRequest request, CancellationToken ct = default)
        {
            LastStartRequest = request;
            return Task.FromResult(StartResult);
        }

        public Task<OAuthCallbackResult> CompleteOAuthAsync(string channelId, IReadOnlyDictionary<string, string?> parameters, CancellationToken ct = default)
        {
            LastCallbackChannelId = channelId;
            LastCallbackParams = parameters;
            return Task.FromResult(CallbackResult);
        }

        public Task<TrustIssueResult> IssueForPlatformUserAsync(TrustIssueRequest request, string platformUid, CancellationToken ct = default)
        {
            LastTrustIssueRequest = request;
            LastPlatformUid = platformUid;
            return Task.FromResult(TrustIssueResult);
        }

        public Task<IdentityClaimResult> ClaimAsync(IdentityClaimRequest request, CancellationToken ct = default)
        {
            LastClaimRequest = request;
            return Task.FromResult(ClaimResult);
        }
    }

    /// <summary>Fake 外部身份统一契约（AuthCenter.Abstractions——事件编排 uid 只读查询 F7）：
    /// GetByExternalIdAsync 记录调用 + 返回可配置 Map；LinkAsync 计数（uid 只读断言——LinkAsync 不被调）。</summary>
    internal sealed class FakeExternalIdentityService : IExternalIdentityService
    {
        public ExternalIdentityDto? Map { get; set; }
        public int GetInvoked { get; private set; }
        public int LinkInvoked { get; private set; }
        public string? LastChannelId { get; private set; }
        public string? LastExternalUserId { get; private set; }

        public void Reset() { Map = null; GetInvoked = 0; LinkInvoked = 0; LastChannelId = null; LastExternalUserId = null; }

        public Task<ExternalIdentityDto?> GetByExternalIdAsync(string channelId, string externalUserId, CancellationToken ct = default)
        {
            GetInvoked++;
            LastChannelId = channelId;
            LastExternalUserId = externalUserId;
            return Task.FromResult(Map);
        }

        public Task<ExternalIdentityDto> LinkAsync(string uid, string channelId, string externalUserId,
            string? operatorName = null, CancellationToken ct = default)
        {
            LinkInvoked++;
            return Task.FromResult(Map ?? new ExternalIdentityDto(uid, channelId, externalUserId, DateTime.UtcNow));
        }

        public Task UnlinkAsync(string uid, string channelId, string externalUserId,
            string? operatorName = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ExternalIdentityDto>> GetByAccountAsync(string uid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExternalIdentityDto>>(Array.Empty<ExternalIdentityDto>());
    }

    /// <summary>Fake 事件回复处理器（IEventReplyHandler——事件编排 first-non-null-wins F8）：
    /// 记录 Invoked/LastContext + 返回可配置 Result（null = 不处理）。
    /// 支持 W3 绑定（可选实现 <see cref="IChannelSpecificEventReplyHandler"/>——BindChannelId 可配置，默认 null = 类型桶）。</summary>
    internal sealed class FakeEventReplyHandler : IEventReplyHandler, IChannelSpecificEventReplyHandler
    {
        public FakeEventReplyHandler(string name) => Name = name;

        private string Name { get; }
        public string ChannelType { get; set; } = "wechat_event";
        public string? BindChannelId { get; set; }                       // W3：null = 类型桶共用；非 null = 精确绑定
        public EventReplyResult? Result { get; set; }
        public int Invoked { get; private set; }
        public ChannelEventDispatch? LastDispatch { get; private set; }

        public void Reset() { Result = null; Invoked = 0; LastDispatch = null; BindChannelId = null; ChannelType = "wechat_event"; }

        string IChannelSpecificEventReplyHandler.ChannelId => BindChannelId ?? string.Empty;

        public Task<EventReplyResult?> HandleAsync(ChannelEventDispatch dispatch, CancellationToken ct = default)
        {
            Invoked++;
            LastDispatch = dispatch;
            return Task.FromResult(Result);
        }
    }

    /// <summary>Fake 事件编排门面（IEventOrchestrator——复刻真实 EventOrchestrator 行为，无守卫依赖）：
    /// 调通道 HandleEventAsync（平台化回复，默认 null）→ uid 只读查询富化（GetByExternalIdAsync 断言/LinkAsync 不调）→
    /// handler first-non-null-wins（按 ChannelType 匹配）。</summary>
    internal sealed class FakeEventOrchestrator : IEventOrchestrator
    {
        public int Invoked { get; private set; }
        public ChannelInboundContext? LastInbound { get; private set; }

        public void Reset() { Invoked = 0; LastInbound = null; }

        public Task<EventReplyResult?> OrchestrateAsync(string channelId, string channelType, ChannelInboundContext context, CancellationToken ct = default)
        {
            Invoked++;
            LastInbound = context;

            // ① 通道解析（FakeSsoChannel.HandleEventAsync——记录 LastEventContext + 返回可配置 Outcome）
            var outcome = Channel.HandleEventAsync(context, ct).GetAwaiter().GetResult();
            if (outcome.State == ChannelEventOutcomeState.Rejected)
                return Task.FromResult<EventReplyResult?>(null);
            if (outcome.State == ChannelEventOutcomeState.NotApplicable || outcome.Event is null)
                return Task.FromResult<EventReplyResult?>(null);
            var evt = outcome.Event;

            // ② uid 只读查询（评审 F7——GetByExternalIdAsync 只读不建号，LinkAsync 不被调）
            string? uid = null;
            if (!string.IsNullOrEmpty(evt.ExternalUserId))
            {
                var map = ChannelMap.GetByExternalIdAsync(channelId, evt.ExternalUserId, ct).GetAwaiter().GetResult();
                uid = map?.UId;
            }
            var dispatch = new ChannelEventDispatch(evt, uid);

            // ③ handler 委托（W3 双桶解析——同步真实 EventOrchestrator 语义）：
            //    桶 1：通道特定绑定（IChannelSpecificEventReplyHandler 且 ChannelId 精确匹配——first-non-null-wins）
            //    桶 2：ChannelType 匹配（默认共用——绑定桶全 null 时回退）
            foreach (var handler in new[] { EventHandler1, EventHandler2 })
            {
                if (handler is IChannelSpecificEventReplyHandler specific
                    && !string.IsNullOrEmpty(specific.ChannelId)
                    && string.Equals(specific.ChannelId, channelId, StringComparison.Ordinal))
                {
                    var reply = handler.HandleAsync(dispatch, ct).GetAwaiter().GetResult();
                    if (reply is not null)
                        return Task.FromResult(reply);
                }
            }
            foreach (var handler in new[] { EventHandler1, EventHandler2 })
            {
                if (!string.Equals(handler.ChannelType, channelType, StringComparison.Ordinal))
                    continue;
                var reply = handler.HandleAsync(dispatch, ct).GetAwaiter().GetResult();
                if (reply is not null)
                    return Task.FromResult(reply);
            }

            return Task.FromResult<EventReplyResult?>(null);
        }
    }

    /// <summary>Fake 面① 密封票据服务（ISealedTicketService&lt;FederatedIdentity&gt;——测试端点 HandleTestTicket
    /// RedeemAsync 依赖）：记录 ticket/context + 返回可配置 RedeemResult（null = 未发现/过期/已用 → 404）。</summary>
    internal sealed class FakeSealedTicketService : ISealedTicketService<FederatedIdentity>
    {
        public FederatedIdentity? RedeemResult { get; set; }
        public string? LastTicket { get; private set; }
        public TicketRedeemContext? LastContext { get; private set; }

        public void Reset() { RedeemResult = null; LastTicket = null; LastContext = null; }

        public Task<SealedTicketResult> IssueAsync(FederatedIdentity payload, TimeSpan ttl, SealedTicketOptions? options = null, CancellationToken ct = default)
            => throw new NotSupportedException();

        public Task<FederatedIdentity?> RedeemAsync(string ticket, TicketRedeemContext? context = null, CancellationToken ct = default)
        {
            LastTicket = ticket;
            LastContext = context;
            return Task.FromResult(RedeemResult);
        }

        public Task<int> CleanupExpiredAsync(CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
