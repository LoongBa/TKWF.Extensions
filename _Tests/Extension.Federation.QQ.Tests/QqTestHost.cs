using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
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
/// QQ 互联平台网关库测试公共设施（**N1 壳——生产路径宿主骨架，协议测试在 N3 立项填充**）。
/// <para>对齐 <c>Extension.Federation.WeChat.Tests</c> 事实源 + N1 模板（`_Tests/Extension.Federation.Template.Tests/`）：
/// 真实 DI（<c>AddQqFederationChannels</c>）+ C 基座 <see cref="TestHostBase"/> + 探针门面
/// （帧内枚举 <see cref="ISsoChannel"/> 集合，ADR92 守卫工厂供给）+ Stub QQ API handler。</para>
/// <para>⚠️ N1 骨架：本文件仅宿主壳（可编译可运行、零 [Fact] 用例）——OAuth 正负用例（code 缺 / state 伪造
/// （装配层集成测试——库不感知 state，N3 P1-3）/ openid 换取 / redirect_uri 不一致拒（N3 P1-4））在 N3 T5 按
/// 模板 <c>{Platform}OauthChannelTests.cs</c> 填充。</para>
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

    /// <summary>
    /// QQ API 出站桩 HttpMessageHandler——按 URL 路径路由返回固定 JSON（不真打 QQ）。
    /// ⚠️ N1 壳：仅占位（NotFound）——N3 T5 按 QQ 协议事实填充路由（oauth2.0/token → code→access_token；
    /// /me → openid；user/get_user_info → 裁剪）。
    /// </summary>
    public static HttpMessageHandler CreateStubHandler(string openId = "test_openid_1", bool failOauth = false)
        => new StubQqHandler(openId, failOauth);

    /// <summary>构造 SsoChannelAuthContext（IReadOnlyDictionary&lt;string,string?&gt; 参数包）。</summary>
    public static SsoChannelAuthContext CreateContext(params (string Key, string? Value)[] parameters)
        => new(new Dictionary<string, string?>(parameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))));

    /// <summary>Stub HttpMessageHandler 实现（路由 QQ API 路径——N3 按协议事实填充）。</summary>
    private sealed class StubQqHandler : HttpMessageHandler
    {
        private readonly string _openId;
        private readonly bool _failOauth;

        public StubQqHandler(string openId, bool failOauth)
        {
            _openId = openId;
            _failOauth = failOauth;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            // ⚠️ N1 壳：N3 T5 按 QQ 协议路由填充（oauth2.0/token /me user/get_user_info）
            _ = _openId;
            _ = _failOauth;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
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
    /// <summary>QQ API 桩 handler（N1 壳默认占位——N3 按协议填充路由）。</summary>
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
