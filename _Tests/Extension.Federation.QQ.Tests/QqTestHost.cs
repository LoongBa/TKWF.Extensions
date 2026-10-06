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

namespace TKWF.Federation.QQ.Tests;

/// <summary>
/// QQ 互联平台网关库测试公共设施（N3 T5——生产路径宿主 + Stub QQ API 协议链）。
/// <para>对齐 <c>Extension.Federation.WeChat.Tests</c> 事实源 + N1 模板：真实 DI（<c>AddQqFederationChannels</c>）+
/// C 基座 <see cref="TestHostBase"/> + 探针门面（帧内枚举 <see cref="ISsoChannel"/> 集合，ADR92 守卫工厂供给）+
/// Stub QQ API handler（路由 QQ 协议事实链：/oauth2.0/token → /oauth2.0/me → /user/get_user_info）。</para>
/// </summary>
internal static class QqTestHost
{
    /// <summary>测试 QQ channel 配置（Channels 单实例——ChannelId 选区默认首项）。</summary>
    public static QqChannelConfig CreateChannelConfig(
        string channelId = "qq-main",
        string appId = "test-appid",
        string appSecret = "test-secret",
        bool enableUnionId = false)
        => new()
        {
            ChannelId = channelId,
            AppId = appId,
            AppSecret = appSecret,
            EnableUnionId = enableUnionId,
        };

    /// <summary>测试回调地址（redirect_uri——授权时装配层决定，测试代码与 stub 服务端比对基准一致）。</summary>
    public const string TestRedirectUri = "https://test.local/sso/oauth/qq/callback";

    /// <summary>
    /// QQ API 出站桩 HttpMessageHandler——按 URL 路径路由返回固定 JSON（不真打 QQ）。
    /// <para>路由（对齐 QQ 协议事实，N3 §二）：<c>oauth2.0/token</c>（code→access_token，含 redirect_uri 一致性
    /// 服务端比对模拟——P1-4）/ <c>oauth2.0/me</c>（access_token→openid，EnableUnionId 时含 unionid）/
    /// <c>user/get_user_info</c>（ret/msg 错误模型裁剪）。其余路径 404。</para>
    /// </summary>
    public static HttpMessageHandler CreateStubHandler(
        string openId = "test_openid_1",
        string? unionId = null,
        bool failOauth = false,
        bool failMe = false,
        bool redirectMismatch = false,
        bool failUserInfo = false)
        => new StubQqHandler(openId, unionId, failOauth, failMe, redirectMismatch, failUserInfo);

    /// <summary>构造 SsoChannelAuthContext（IReadOnlyDictionary&lt;string,string?&gt; 参数包）。</summary>
    public static SsoChannelAuthContext CreateContext(params (string Key, string? Value)[] parameters)
        => new(new Dictionary<string, string?>(parameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))));

    /// <summary>Stub HttpMessageHandler 实现（路由 QQ API 路径——对齐 QQ 协议事实链）。</summary>
    private sealed class StubQqHandler : HttpMessageHandler
    {
        private readonly string _openId;
        private readonly string? _unionId;
        private readonly bool _failOauth;        // token 端点失败（error 模型）
        private readonly bool _failMe;           // /me 端点失败（error 模型）
        private readonly bool _redirectMismatch; // token 端点 redirect_uri 一致性比对失败（P1-4）
        private readonly bool _failUserInfo;     // get_user_info 失败（ret/msg 模型）

        public StubQqHandler(string openId, string? unionId, bool failOauth, bool failMe, bool redirectMismatch, bool failUserInfo)
        {
            _openId = openId;
            _unionId = unionId;
            _failOauth = failOauth;
            _failMe = failMe;
            _redirectMismatch = redirectMismatch;
            _failUserInfo = failUserInfo;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            var query = request.RequestUri?.Query ?? "";

            if (path.EndsWith("/oauth2.0/token", StringComparison.Ordinal))
            {
                // 服务端 redirect_uri 一致性比对模拟（N3 P1-4 —— code 换 token 时须与授权时一致）
                var redirectUri = GetQueryParam(query, "redirect_uri");
                if (_redirectMismatch || !string.Equals(redirectUri, TestRedirectUri, StringComparison.Ordinal))
                    return Json("{\"error\":100030,\"error_description\":\"redirect_uri is not consistent with the authorization request\"}");
                if (_failOauth)
                    return Json("{\"error\":100016,\"error_description\":\"access token check failed\"}");
                return Json($"{{\"access_token\":\"stub-access-token\",\"expires_in\":7776000,\"refresh_token\":\"stub-refresh-token\"}}");
            }

            if (path.EndsWith("/oauth2.0/me", StringComparison.Ordinal))
            {
                if (_failMe)
                    return Json("{\"error\":100016,\"error_description\":\"access token check failed\"}");
                var union = _unionId is null ? "" : $",\"unionid\":\"{_unionId}\"";
                return Json($"{{\"client_id\":\"test-appid\",\"openid\":\"{_openId}\"{union}}}");
            }

            if (path.EndsWith("/user/get_user_info", StringComparison.Ordinal))
            {
                if (_failUserInfo)
                    return Json("{\"ret\":1002,\"msg\":\"请先登录\"}");
                return Json($"{{\"ret\":0,\"msg\":\"\",\"nickname\":\"QQ 测试用户\",\"figureurl\":\"http://q.qlogo.cn/30\",\"figureurl_qq_1\":\"http://q.qlogo.cn/40\",\"figureurl_qq_2\":\"http://q.qlogo.cn/100\",\"gender\":\"男\",\"province\":\"广东\",\"city\":\"深圳\"}}");
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }

        private static string GetQueryParam(string query, string name)
        {
            // query 形如 "?a=1&b=2"——手拆避免 System.Web 依赖
            var s = query.StartsWith('?') ? query[1..] : query;
            foreach (var pair in s.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0] == name)
                    return Uri.UnescapeDataString(kv[1]);
            }
            return "";
        }

        private static Task<HttpResponseMessage> Json(string json)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            });
    }
}

/// <summary>
/// 通道枚举探针门面——帧内 <c>User.Use&lt;IChannelProbe&gt;()</c> AOP 路径创建，
/// ctor 注入 <c>IEnumerable&lt;ISsoChannel&gt;</c>（集合版守卫工厂帧内供给——对齐 WeChat 先例门面模式）。
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
/// 测试宿主——真实 DI（AddLogging + AddQqFederationChannels + 探针门面 + Stub HttpClientFactory）。
/// <para>继承 <see cref="TestHostBase"/>：BindTestScope 走真实 DomainUser.BindScope；N3 测试类继承本类，
/// 各测试内先设置 <see cref="Handler"/>/<see cref="ConfigureOptions"/> 再调用 <c>BindTestScope()</c>（protected）。</para>
/// </summary>
public class QqChannelTestHost : TestHostBase
{
    /// <summary>QQ API 桩 handler（默认正向：openid=test_openid_1）。</summary>
    public HttpMessageHandler Handler { get; set; } = QqTestHost.CreateStubHandler();

    /// <summary>QqOptions 编程覆盖（Channels 配置）。</summary>
    public Action<QqOptions>? ConfigureOptions { get; set; }

    /// <summary>绑定域作用域（生产路径等价）——public 包装 <see cref="TestHostBase.BindTestScope"/>（protected 不可外部访问；
    /// 测试类不继承 TestHostBase 时经本包装调用）。返回 <c>(provider, user)</c>，经 <c>User.Use&lt;接口&gt;()</c> AOP 解析。</summary>
    public (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindScope()
        => BindTestScope();

    protected override void ConfigureHost(IServiceCollection services)
    {
        // 通道注册（TryAddEnumerableConstructible）——真实生产注册形态
        services.AddQqFederationChannels(ConfigureOptions);

        // 探针门面——帧内枚举 ISsoChannel 集合（守卫工厂供给）
        services.AddConstructibleService<IChannelProbe, ChannelProbe>();

        // Stub IHttpClientFactory——QQ API 桩（AddHttpClient 的 DefaultHttpClientFactory 被覆盖）
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(Handler));
    }

    /// <summary>Stub IHttpClientFactory（返回固定 handler 的 HttpClient——不真打 QQ）。</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler);
    }
}