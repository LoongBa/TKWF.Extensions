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
    /// <para>仅路由 <c>sns/oauth2/access_token</c>（code→openid）与 <c>cgi-bin/token</c>（access_token）。
    /// 其余路径 404（测试只走 code 换 openid 正向 + errcode 负向）。</para>
    /// </summary>
    public static HttpMessageHandler CreateStubHandler(
        string openId = "oa_test_openid_1",
        bool failOauth = false)
        => new StubWeChatHandler(openId, failOauth);

    /// <summary>构造 SsoChannelAuthContext（IReadOnlyDictionary&lt;string,string?&gt; 参数包）。</summary>
    public static SsoChannelAuthContext CreateContext(params (string Key, string? Value)[] parameters)
        => new(new Dictionary<string, string?>(parameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))));

    /// <summary>Stub HttpMessageHandler 实现（路由微信 API 路径）。</summary>
    private sealed class StubWeChatHandler : HttpMessageHandler
    {
        private readonly string _openId;
        private readonly bool _failOauth;

        public StubWeChatHandler(string openId, bool failOauth)
        {
            _openId = openId;
            _failOauth = failOauth;
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
}
