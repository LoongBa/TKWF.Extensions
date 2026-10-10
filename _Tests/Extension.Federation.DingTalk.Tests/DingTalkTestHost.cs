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
using TKWF.Ext.TrustCenter;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Federation.DingTalk.Tests;

/// <summary>
/// 钉钉平台网关库测试公共设施——真实 DI（<c>AddDingTalkFederationChannels</c>）+ C 基座
/// <see cref="TestHostBase"/> + 探针门面（帧内枚举 <see cref="ISsoChannel"/> 集合，ADR92 守卫工厂供给）。
/// <para>对齐 Extension.Federation.WeChat.Tests 的 TestHostBase 模式：生产路径集成（真实 DI + BindScope +
/// <c>User.Use&lt;接口&gt;()</c> AOP）+ 通道经集合版守卫工厂解析——不手写宿主桩绕过守卫。</para>
/// </summary>
internal static class DingTalkTestHost
{
    /// <summary>测试钉钉应用配置（Channels 单实例——ChannelId 选区默认首项）。</summary>
    public static DingTalkChannelConfig CreateChannelConfig(
        string channelId = "dingtalk-main",
        string corpId = "ding_test_corp",
        string appKey = "dingAppKey000000000000",
        string appSecret = "test-secret",
        string token = "test-token",
        string? encodingAESKey = null)
        => new()
        {
            ChannelId = channelId,
            CorpId = corpId,
            AppKey = appKey,
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
    /// 钉钉 API 出站桩 HttpMessageHandler——按 URL 路径路由返回固定 JSON（不真打钉钉）。
    /// <para>路由：<c>/v1.0/oauth2/userAccessToken</c>（code→用户级 token，failOauth→400）/
    /// <c>/gettoken</c>（旧版企业 token）/<c>/v1.0/oauth2/accessToken</c>（新版企业 token）/
    /// <c>/topapi/user/getbyunionid</c>（unionId→userid，failGetUserId→errcode）/
    /// <c>/v1.0/contact/users/{unionId}</c>（用户信息，failGetUserInfo→404）。其余路径 404。</para>
    /// </summary>
    public static HttpMessageHandler CreateStubHandler(
        string? openId = "oa_test_openid_1",
        string unionId = "uni_test_1",
        string corpId = "ding_test_corp",
        bool failOauth = false,
        bool failGetUserId = false,
        bool failGetUserInfo = false)
        => new StubDingTalkHandler(openId, unionId, corpId, failOauth, failGetUserId, failGetUserInfo);

    /// <summary>构造 SsoChannelAuthContext（IReadOnlyDictionary&lt;string,string?&gt; 参数包）。</summary>
    public static SsoChannelAuthContext CreateContext(params (string Key, string? Value)[] parameters)
        => new(new Dictionary<string, string?>(parameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))));

    /// <summary>Stub HttpMessageHandler 实现（路由钉钉 API 路径）。</summary>
    private sealed class StubDingTalkHandler : HttpMessageHandler
    {
        private readonly string? _openId;
        private readonly string _unionId;
        private readonly string _corpId;
        private readonly bool _failOauth;
        private readonly bool _failGetUserId;
        private readonly bool _failGetUserInfo;

        public StubDingTalkHandler(string? openId, string unionId, string corpId, bool failOauth, bool failGetUserId, bool failGetUserInfo)
        {
            _openId = openId;
            _unionId = unionId;
            _corpId = corpId;
            _failOauth = failOauth;
            _failGetUserId = failGetUserId;
            _failGetUserInfo = failGetUserInfo;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            string json;
            var status = HttpStatusCode.OK;

            if (path.EndsWith("/v1.0/oauth2/userAccessToken", StringComparison.Ordinal))
            {
                if (_failOauth)
                {
                    status = HttpStatusCode.BadRequest;
                    json = "{\"code\":\"InvalidCode\",\"message\":\"invalid code\"}";
                }
                else
                {
                    // openId=null → 响应省略 openId 字段（测 DINGTALK_OPENID_MISSING 负路径）
                    json = _openId == null
                        ? $"{{\"accessToken\":\"stub-uat\",\"refreshToken\":\"stub-r\",\"expireIn\":7200,\"corpId\":\"{_corpId}\",\"unionId\":\"{_unionId}\"}}"
                        : $"{{\"accessToken\":\"stub-uat\",\"refreshToken\":\"stub-r\",\"expireIn\":7200,\"corpId\":\"{_corpId}\",\"openId\":\"{_openId}\",\"unionId\":\"{_unionId}\"}}";
                }
            }
            else if (path.EndsWith("/gettoken", StringComparison.Ordinal))
            {
                json = "{\"errcode\":0,\"access_token\":\"stub-legacy-token\",\"expires_in\":7200}";
            }
            else if (path.EndsWith("/v1.0/oauth2/accessToken", StringComparison.Ordinal))
            {
                json = "{\"accessToken\":\"stub-v1-token\",\"expireIn\":7200}";
            }
            else if (path.EndsWith("/topapi/user/getbyunionid", StringComparison.Ordinal))
            {
                json = _failGetUserId
                    ? "{\"errcode\":40001,\"errmsg\":\"invalid unionid\"}"
                    : "{\"errcode\":0,\"result\":{\"userid\":\"usr_123\",\"contact_type\":0}}";
            }
            else if (path.Contains("/v1.0/contact/users/", StringComparison.Ordinal))
            {
                if (_failGetUserInfo)
                {
                    status = HttpStatusCode.NotFound;
                    json = "{\"code\":\"UserNotFound\",\"message\":\"user not found\"}";
                }
                else
                {
                    json = $"{{\"nick\":\"张三\",\"avatarUrl\":\"https://avatar.test/a.png\",\"mobile\":\"138****1234\",\"unionId\":\"{_unionId}\",\"openId\":\"{_openId}\"}}";
                }
            }
            else
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }

            return Task.FromResult(new HttpResponseMessage(status)
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
/// 测试宿主——真实 DI（AddLogging + AddDingTalkFederationChannels + 探针门面 + Stub HttpClientFactory）。
/// <para>继承 <see cref="TestHostBase"/>：BindTestScope 走真实 DomainUser.BindScope；测试类继承本类，
/// 各测试内先设置 <see cref="Handler"/>/<see cref="ConfigureOptions"/> 再调用 <c>BindTestScope()</c>（protected）。</para>
/// </summary>
public class DingTalkChannelTestHost : TestHostBase
{
    /// <summary>钉钉 API 桩 handler（默认正向：openid=oa_test_openid_1）。</summary>
    public HttpMessageHandler Handler { get; set; } = DingTalkTestHost.CreateStubHandler();

    /// <summary>DingTalkOptions 编程覆盖（Channels 配置）。</summary>
    public Action<DingTalkOptions>? ConfigureOptions { get; set; }

    /// <summary>绑定域作用域（生产路径等价）——public 包装 <see cref="TestHostBase.BindTestScope"/>（protected 不可外部访问；
    /// 测试类不继承 TestHostBase 时经本包装调用）。返回 <c>(provider, user)</c>，经 <c>User.Use&lt;接口&gt;()</c> AOP 解析。</summary>
    public (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindScope()
        => BindTestScope();

    protected override void ConfigureHost(IServiceCollection services)
    {
        // 通道注册（TryAddEnumerableConstructible）——真实生产注册形态
        services.AddDingTalkFederationChannels(ConfigureOptions);

        // 多通道联邦核心门面（v0.3.0）——平台库测试宿主不启动 FederationExtensionInitializer，
        // 需补注册"已启用 Federation"的最小生产等价面（对齐 FederationExtensionInitializer.ConfigureServices
        // 对应片段）：StaticChannelRegistry + CompositeChannelRegistry + SsoChannelFactory + SsoLogin
        services.AddOptions<FederationStaticChannelOptions>();
        services.AddScoped<StaticChannelRegistry>();
        services.AddConstructibleService<IChannelRegistry, CompositeChannelRegistry>();
        services.AddConstructibleService<ISsoChannelFactory, SsoChannelFactory>();
        services.AddConstructibleService<ISsoLogin, SsoLogin>();

        // 探针门面——帧内枚举 ISsoChannel 集合（守卫工厂供给）
        services.AddConstructibleService<IChannelProbe, ChannelProbe>();

        // Stub IHttpClientFactory——钉钉 API 桩（AddHttpClient 的 DefaultHttpClientFactory 被覆盖）
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(Handler));
    }

    /// <summary>Stub IHttpClientFactory（返回固定 handler 的 HttpClient——不真打钉钉）。</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler);
    }
}
