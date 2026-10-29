using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Federation.WeChat.Tests;

/// <summary>
/// 微信平台网关库测试公共设施——真实 DI（<c>AddWeChatFederationChannels</c>）+ C 基座
/// <see cref="TestHostBase"/> + 探针门面（帧内枚举 <see cref="ISsoChannel"/> 集合，ADR92 守卫工厂供给）。
/// <para>对齐 Extension.Federation.Tests 的 TestHostBase 模式：生产路径集成（真实 DI + BindScope +
/// <c>User.Use&lt;接口&gt;()</c> AOP）+ 通道经集合版守卫工厂解析——不手写宿主桩绕过守卫。</para>
/// </summary>
internal static class WeChatTestHost
{
    /// <summary>测试公众号配置（Channels 单实例——ChannelId 选区默认首项）。</summary>
    public static WeChatChannelConfig CreateChannelConfig(
        string channelId = "wechat-mp-main",
        string appId = "wx0000000000000000",
        string appSecret = "test-secret",
        string token = "test-token",
        string? encodingAESKey = null)
        => new()
        {
            ChannelId = channelId,
            AppId = appId,
            AppSecret = appSecret,
            Token = token,
            EncodingAESKey = encodingAESKey ?? GenerateEncodingAESKey(),
        };

    /// <summary>生成合法 43 字符 EncodingAESKey（32 字节 Base64 去 '='）。</summary>
    public static string GenerateEncodingAESKey()
    {
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        return Convert.ToBase64String(key).TrimEnd('=');
    }

    /// <summary>
    /// 微信 API 出站桩 HttpMessageHandler——按 URL 路径路由返回固定 JSON（不真打微信）。
    /// <para>路由 <c>sns/oauth2/access_token</c>（code→openid）、<c>cgi-bin/token</c>（access_token）、
    /// <c>cgi-bin/menu/create</c>（菜单创建——回显 body 校验）+ <c>cgi-bin/get_current_selfmenu_info</c>
    /// （菜单查询——V0.4.1 菜单 API 用例）+ <c>cgi-bin/menu/delete</c>（菜单删除）。
    /// 其余路径 404。</para>
    /// </summary>
    public static HttpMessageHandler CreateStubHandler(
        string openId = "oa_test_openid_1",
        bool failOauth = false,
        string? menuJson = null)
        => new StubWeChatHandler(openId, failOauth, menuJson);

    /// <summary>构造 SsoChannelAuthContext（IReadOnlyDictionary&lt;string,string?&gt; 参数包）。</summary>
    public static SsoChannelAuthContext CreateContext(params (string Key, string? Value)[] parameters)
        => new(new Dictionary<string, string?>(parameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))));

    /// <summary>构造入站报文上下文（I2 传输层——<see cref="ChannelEventDispatch"/> 前身；通道直测
    /// <c>HandleEventAsync</c>/<c>VerifyUrlAsync</c> 用例；Dictionary 实例原地富化跨调用方可见）。</summary>
    public static ChannelInboundContext CreateEventContext(params (string Key, string? Value)[] parameters)
        => new(new Dictionary<string, string?>(parameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))));

    /// <summary>构造语义事件派发（I2 语义层——<see cref="IEventReplyHandler.HandleAsync"/> 用例：
    /// 微信参数包 → 语义 <see cref="ChannelEvent"/>（RawPayload 由 <c>__decrypted_xml</c> 键承载——handler
    /// <see cref="WechatEventContext.FromChannelEvent"/> 重建数据源）。</summary>
    public static ChannelEventDispatch CreateEventDispatch(params (string Key, string? Value)[] parameters)
    {
        var dict = new Dictionary<string, string?>(parameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value)));
        dict.TryGetValue("channel_id", out var channelId);
        dict.TryGetValue("from_user_name", out var fromUserName);
        dict.TryGetValue("external_user_id", out var externalUserId);
        dict.TryGetValue("event", out var eventValue);
        dict.TryGetValue("__decrypted_xml", out var rawXml);
        var evt = new ChannelEvent
        {
            ChannelId = channelId ?? "",
            ChannelType = "wechat_event",
            ExternalUserId = externalUserId ?? fromUserName ?? "",
            EventType = eventValue ?? "unknown",
            RawPayload = rawXml,
            Attributes = dict,
        };
        dict.TryGetValue("uid", out var uid);
        return new ChannelEventDispatch(evt, uid);
    }

    /// <summary>Stub HttpMessageHandler 实现（路由微信 API 路径）。</summary>
    private sealed class StubWeChatHandler : HttpMessageHandler
    {
        private readonly string _openId;
        private readonly bool _failOauth;
        private readonly string? _menuJson;

        public StubWeChatHandler(string openId, bool failOauth, string? menuJson)
        {
            _openId = openId;
            _failOauth = failOauth;
            _menuJson = menuJson;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            string json;
            if (path.EndsWith("/sns/oauth2/access_token", StringComparison.Ordinal))
            {
                json = _failOauth
                    ? "{\"errcode\":40029,\"errmsg\":\"invalid code\"}"
                    : $"{{\"access_token\":\"stub-token\",\"expires_in\":7200,\"openid\":\"{_openId}\"}}";
            }
            else if (path.EndsWith("/cgi-bin/token", StringComparison.Ordinal))
            {
                json = "{\"access_token\":\"stub-token\",\"expires_in\":7200}";
            }
            else if (path.EndsWith("/cgi-bin/menu/create", StringComparison.Ordinal))
            {
                json = _menuJson ?? "{\"errcode\":0,\"errmsg\":\"ok\"}";
            }
            else if (path.EndsWith("/cgi-bin/get_current_selfmenu_info", StringComparison.Ordinal))
            {
                json = _menuJson
                    ?? "{\"is_menu_open\":1,\"selfmenu_info\":{\"button\":[{\"type\":\"click\",\"name\":\"测试菜单\",\"key\":\"test_key\"}]}}";
            }
            else if (path.EndsWith("/cgi-bin/menu/delete", StringComparison.Ordinal))
            {
                json = "{\"errcode\":0,\"errmsg\":\"ok\"}";
            }
            else if (path.EndsWith("/cgi-bin/user/info", StringComparison.Ordinal))
            {
                json = $"{{\"subscribe\":1,\"openid\":\"{_openId}\",\"subscribe_time\":1730000000}}";
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
        }
    }
}

/// <summary>
/// 通道枚举探针门面——帧内 <c>User.Use&lt;IChannelProbe&gt;()</c> AOP 路径创建，
/// ctor 注入 <c>IEnumerable&lt;ISsoChannel&gt;</c>（集合版守卫工厂帧内供给——对齐 WechatLoginService 门面模式）。
/// </summary>
public interface IChannelProbe : IDomainService
{
    /// <summary>已注册的 ISsoChannel 集合（按 ChannelType 选区）。</summary>
    IReadOnlyList<ISsoChannel> Channels { get; }
}

[DiContractIgnore]
public sealed class ChannelProbe : DomainServiceBase, IChannelProbe
{
    private readonly IReadOnlyList<ISsoChannel> _channels;

    public ChannelProbe(IDomainUser user, IEnumerable<ISsoChannel> channels) : base(user)
    {
        _channels = channels?.ToList() ?? throw new ArgumentNullException(nameof(channels));
    }

    /// <inheritdoc />
    public IReadOnlyList<ISsoChannel> Channels => _channels;
}

/// <summary>
/// 测试宿主——真实 DI（AddLogging + AddWeChatFederationChannels + 探针门面 + Stub HttpClientFactory）。
/// <para>继承 <see cref="TestHostBase"/>：BindTestScope 走真实 DomainUser.BindScope；测试类继承本类，
/// 各测试内先设置 <see cref="Handler"/>/<see cref="ConfigureOptions"/> 再调用 <c>BindTestScope()</c>（protected）。</para>
/// </summary>
public class WeChatChannelTestHost : TestHostBase
{
    /// <summary>微信 API 桩 handler（默认正向：openid=oa_test_openid_1）。</summary>
    public HttpMessageHandler Handler { get; set; } = WeChatTestHost.CreateStubHandler();

    /// <summary>WeChatOptions 编程覆盖（Channels 配置）。</summary>
    public Action<WeChatOptions>? ConfigureOptions { get; set; }

    /// <summary>绑定域作用域（生产路径等价）——public 包装 <see cref="TestHostBase.BindTestScope"/>（protected 不可外部访问；
    /// 测试类不继承 TestHostBase 时经本包装调用）。返回 <c>(provider, user)</c>，经 <c>User.Use&lt;接口&gt;()</c> AOP 解析。</summary>
    public (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindScope()
        => BindTestScope();

    protected override void ConfigureHost(IServiceCollection services)
    {
        // 通道注册（TryAddEnumerableConstructible）——真实生产注册形态
        services.AddWeChatFederationChannels(ConfigureOptions);

        // 多通道联邦核心门面（v0.3.0）——平台库测试宿主不启动 FederationExtensionInitializer，
        // 需补注册"已启用 Federation"的最小生产等价面（对齐 FederationExtensionInitializer.ConfigureServices
        // 对应片段）：IMemoryCache（D03 1.7 Composite 缓存层依赖）+ StaticChannelRegistry +
        // CompositeChannelRegistry + SsoChannelFactory + SsoLogin
        services.TryAddSingleton<Microsoft.Extensions.Caching.Memory.IMemoryCache, Microsoft.Extensions.Caching.Memory.MemoryCache>();
        services.AddOptions<FederationStaticChannelOptions>();
        services.AddScoped<StaticChannelRegistry>();
        services.AddConstructibleService<IChannelRegistry, CompositeChannelRegistry>();
        services.AddConstructibleService<ISsoChannelFactory, SsoChannelFactory>();
        services.AddConstructibleService<ISsoLogin, SsoLogin>();

        // 免登入口门面（I2-8——平台库测试宿主聚焦事件回复/解析，免登签票路径不在本宿主断言：
        // Fake 返回 null = LoginEntry.Enabled=false 等价——模板 {ticket} → 空串替换，其余文本照常渲染）
        services.AddScoped<ILoginEntryService>(_ => new NullLoginEntryService());

        // 探针门面——帧内枚举 ISsoChannel 集合（守卫工厂供给）
        services.AddConstructibleService<IChannelProbe, ChannelProbe>();

        // Stub IHttpClientFactory——微信 API 桩（AddHttpClient 的 DefaultHttpClientFactory 被覆盖）
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(Handler));
    }

    /// <summary>Stub IHttpClientFactory（返回固定 handler 的 HttpClient——不真打微信）。</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler);
    }

    /// <summary>免登入口 Fake（返回 null = 未启用语义——平台库测试宿主免登签票断言归 Federation.Tests）。</summary>
    private sealed class NullLoginEntryService : ILoginEntryService
    {
        public Task<string?> BuildTicketUrlAsync(string channelId, string channelType, string externalUserId, int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null)
            => Task.FromResult<string?>(null);

        public Task<string?> BuildTestTicketUrlAsync(string channelId, string platformId, string channelType, string externalUserId, int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default, string? uId = null)
            => Task.FromResult<string?>(null);

        public Task<string?> BuildSilentAuthorizeUrlAsync(string channelId, string returnUrl, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }
}
