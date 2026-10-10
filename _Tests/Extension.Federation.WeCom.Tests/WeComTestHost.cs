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

namespace TKWF.Federation.WeCom.Tests;

/// <summary>企业微信成员类型（OAuth 三路径——Oracle 评审 P0-1 双策略测试矩阵）。</summary>
public enum WeComMemberType
{
    /// <summary>自建应用企业成员（getuserinfo → UserId）。</summary>
    SelfMember,

    /// <summary>三方应用企业成员（getuserinfo3rd → Userid + OpenUserID）。</summary>
    ThirdPartyMember,

    /// <summary>非企业成员（getuserinfo → OpenId）。</summary>
    NonMember,
}

/// <summary>
/// 企业微信平台网关库测试公共设施——真实 DI（<c>AddWeComFederationChannels</c>）+ C 基座
/// <see cref="TestHostBase"/> + 探针门面（帧内枚举 <see cref="ISsoChannel"/> 集合，ADR92 守卫工厂供给）。
/// <para>对齐 Extension.Federation.WeChat.Tests 的 TestHostBase 模式：生产路径集成（真实 DI + BindScope +
/// <c>User.Use&lt;接口&gt;()</c> AOP）+ 通道经集合版守卫工厂解析——不手写宿主桩绕过守卫。</para>
/// </summary>
internal static class WeComTestHost
{
    /// <summary>测试企业微信应用配置（Channels 单实例——ChannelId 选区默认首项）。</summary>
    public static WeComChannelConfig CreateChannelConfig(
        string channelId = "wecom-main",
        string corpId = "ww_test_corp",
        string corpSecret = "test-secret",
        string agentId = "1000002",
        string token = "test-token",
        string? encodingAESKey = null,
        bool isThirdParty = false,
        bool enableSensitiveInfo = false)
        => new()
        {
            ChannelId = channelId,
            CorpId = corpId,
            CorpSecret = corpSecret,
            AgentId = agentId,
            Token = token,
            EncodingAESKey = encodingAESKey ?? GenerateEncodingAESKey(),
            IsThirdParty = isThirdParty,
            EnableSensitiveInfo = enableSensitiveInfo,
        };

    /// <summary>生成合法 43 字符 EncodingAESKey（32 字节 Base64 去 '='）。</summary>
    public static string GenerateEncodingAESKey()
    {
        var key = new byte[32];
        System.Security.Cryptography.RandomNumberGenerator.Fill(key);
        return Convert.ToBase64String(key).TrimEnd('=');
    }

    /// <summary>
    /// 企业微信 API 出站桩 HttpMessageHandler——按 URL 路径路由返回固定 JSON（不真打企业微信）。
    /// <para>路由：<c>cgi-bin/gettoken</c>（企业 access_token）/ <c>cgi-bin/auth/getuserinfo</c>（自建——成员
    /// UserId / 非成员 OpenId）/ <c>cgi-bin/auth/getuserinfo3rd</c>（三方——Userid+OpenUserID）/
    /// <c>cgi-bin/auth/getuserdetail</c>（user_ticket 敏感信息，failSensitive→errcode）。其余路径 404。</para>
    /// </summary>
    public static HttpMessageHandler CreateStubHandler(
        WeComMemberType memberType = WeComMemberType.SelfMember,
        string corpId = "ww_test_corp",
        string userId = "usr_123",
        string openUserId = "ou_test_1",
        string openId = "oa_test_openid_1",
        bool failOauth = false,
        bool failSensitive = false,
        bool withUserTicket = false)
        => new StubWeComHandler(memberType, corpId, userId, openUserId, openId, failOauth, failSensitive, withUserTicket);

    /// <summary>构造 SsoChannelAuthContext（IReadOnlyDictionary&lt;string,string?&gt; 参数包）。</summary>
    public static SsoChannelAuthContext CreateContext(params (string Key, string? Value)[] parameters)
        => new(new Dictionary<string, string?>(parameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))));

    /// <summary>Stub HttpMessageHandler 实现（路由企业微信 API 路径）。</summary>
    private sealed class StubWeComHandler : HttpMessageHandler
    {
        private readonly WeComMemberType _memberType;
        private readonly string _corpId;
        private readonly string _userId;
        private readonly string _openUserId;
        private readonly string _openId;
        private readonly bool _failOauth;
        private readonly bool _failSensitive;
        private readonly bool _withUserTicket;

        public StubWeComHandler(WeComMemberType memberType, string corpId, string userId, string openUserId, string openId, bool failOauth, bool failSensitive, bool withUserTicket)
        {
            _memberType = memberType;
            _corpId = corpId;
            _userId = userId;
            _openUserId = openUserId;
            _openId = openId;
            _failOauth = failOauth;
            _failSensitive = failSensitive;
            _withUserTicket = withUserTicket;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            string json;

            if (path.EndsWith("/cgi-bin/gettoken", StringComparison.Ordinal))
            {
                json = "{\"errcode\":0,\"access_token\":\"stub-token\",\"expires_in\":7200}";
            }
            else if (path.EndsWith("/cgi-bin/auth/getuserinfo", StringComparison.Ordinal))  // 自建端点
            {
                if (_failOauth)
                {
                    json = "{\"errcode\":40014,\"errmsg\":\"invalid access_token\"}";
                }
                else if (_memberType == WeComMemberType.SelfMember)
                {
                    json = _withUserTicket
                        ? $"{{\"errcode\":0,\"UserId\":\"{_userId}\",\"DeviceId\":\"dev1\",\"UserTicket\":\"stub-ticket\"}}"
                        : $"{{\"errcode\":0,\"UserId\":\"{_userId}\",\"DeviceId\":\"dev1\"}}";
                }
                else
                {
                    json = $"{{\"errcode\":0,\"OpenId\":\"{_openId}\"}}";  // NonMember
                }
            }
            else if (path.EndsWith("/cgi-bin/auth/getuserinfo3rd", StringComparison.Ordinal))  // 三方端点
            {
                if (_failOauth)
                {
                    json = "{\"errcode\":40014,\"errmsg\":\"invalid access_token\"}";
                }
                else
                {
                    json = _withUserTicket
                        ? $"{{\"errcode\":0,\"Corpid\":\"{_corpId}\",\"Userid\":\"{_userId}\",\"OpenUserID\":\"{_openUserId}\",\"UserTicket\":\"stub-ticket\"}}"
                        : $"{{\"errcode\":0,\"Corpid\":\"{_corpId}\",\"Userid\":\"{_userId}\",\"OpenUserID\":\"{_openUserId}\"}}";
                }
            }
            else if (path.EndsWith("/cgi-bin/auth/getuserdetail", StringComparison.Ordinal))
            {
                json = _failSensitive
                    ? "{\"errcode\":60011,\"errmsg\":\"no privilege to access\"}"
                    : "{\"errcode\":0,\"userid\":\"usr_123\",\"name\":\"张三\",\"mobile\":\"138****1234\",\"email\":\"a@b.c\",\"avatar\":\"https://avatar.test/a.png\",\"gender\":1}";
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
/// 测试宿主——真实 DI（AddLogging + AddWeComFederationChannels + 探针门面 + Stub HttpClientFactory）。
/// <para>继承 <see cref="TestHostBase"/>：BindTestScope 走真实 DomainUser.BindScope；测试类继承本类，
/// 各测试内先设置 <see cref="Handler"/>/<see cref="ConfigureOptions"/> 再调用 <c>BindTestScope()</c>（protected）。</para>
/// </summary>
public class WeComChannelTestHost : TestHostBase
{
    /// <summary>企业微信 API 桩 handler（默认正向：自建成员 usr_123）。</summary>
    public HttpMessageHandler Handler { get; set; } = WeComTestHost.CreateStubHandler();

    /// <summary>WeComOptions 编程覆盖（Channels 配置）。</summary>
    public Action<WeComOptions>? ConfigureOptions { get; set; }

    /// <summary>绑定域作用域（生产路径等价）——public 包装 <see cref="TestHostBase.BindTestScope"/>（protected 不可外部访问；
    /// 测试类不继承 TestHostBase 时经本包装调用）。返回 <c>(provider, user)</c>，经 <c>User.Use&lt;接口&gt;()</c> AOP 解析。</summary>
    public (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindScope()
        => BindTestScope();

    protected override void ConfigureHost(IServiceCollection services)
    {
        // 通道注册（TryAddEnumerableConstructible）——真实生产注册形态
        services.AddWeComFederationChannels(ConfigureOptions);

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

        // Stub IHttpClientFactory——企业微信 API 桩（AddHttpClient 的 DefaultHttpClientFactory 被覆盖）
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(Handler));
    }

    /// <summary>Stub IHttpClientFactory（返回固定 handler 的 HttpClient——不真打企业微信）。</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler);
    }
}
