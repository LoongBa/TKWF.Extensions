using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.Cryptography;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Federation.Alipay.Tests;

/// <summary>
/// 支付宝开放平台平台网关库测试公共设施（N4 T5——生产路径宿主 + Stub gateway.do 协议链）。
/// <para>对齐 <c>Extension.Federation.QQ.Tests</c> 事实源 + N1 模板：真实 DI（<c>AddAlipayFederationChannels</c>）+
/// C 基座 <see cref="TestHostBase"/> + 探针门面（帧内枚举 <see cref="ISsoChannel"/> 集合，ADR92 守卫工厂供给）+
/// Stub gateway.do handler（RSA2 签名响应——测试私钥真实签名，篡改用错误私钥）。</para>
/// </summary>
internal static class AlipayTestHost
{
    /// <summary>测试回调地址（redirect_uri——授权时装配层决定，通道仅校验存在；支付宝 code→token 换取不经 redirect_uri）。</summary>
    public const string TestRedirectUri = "https://test.local/sso/oauth/alipay/callback";

    /// <summary>测试用户标识（支付宝 user_id 为 16 位 2088 开头）。</summary>
    public const string TestUserId = "2088000000000000";

    /// <summary>测试用 RSA 2048 密钥对（Lazy——进程内单份；私有密钥真签 + 公钥验签，错误私钥篡改负路径）。</summary>
    private sealed class TestKeySet
    {
        public required string PrivateKeyPem { get; init; }
        public required string PublicKeyPem { get; init; }
        public required string WrongPrivateKeyPem { get; init; }
    }

    private static readonly Lazy<TestKeySet> Keys = new(() =>
    {
        using var rsa = RSA.Create(2048);
        using var wrong = RSA.Create(2048);
        return new TestKeySet
        {
            PrivateKeyPem = rsa.ExportPkcs8PrivateKeyPem(),
            PublicKeyPem = rsa.ExportSubjectPublicKeyInfoPem(),
            WrongPrivateKeyPem = wrong.ExportPkcs8PrivateKeyPem(),
        };
    });

    /// <summary>测试商户私钥 PEM（签请求/桩签名——与 <see cref="PublicKeyPem"/> 配对）。</summary>
    public static string PrivateKeyPem => Keys.Value.PrivateKeyPem;

    /// <summary>测试支付宝公钥 PEM（验签响应——与 <see cref="PrivateKeyPem"/> 配对）。</summary>
    public static string PublicKeyPem => Keys.Value.PublicKeyPem;

    /// <summary>临时 PEM 密钥文件（进程内单份——ApiClient 经 File.ReadAllText 读路径）。</summary>
    private static readonly Lazy<(string Private, string Public, string Wrong)> KeyFiles = new(() =>
    {
        var dir = Path.Combine(Path.GetTempPath(), "TKWF-Federation-Alipay-Tests");
        Directory.CreateDirectory(dir);
        var privatePath = Path.Combine(dir, "app-private.pem");
        var publicPath = Path.Combine(dir, "alipay-public.pem");
        var wrongPath = Path.Combine(dir, "wrong-private.pem");
        File.WriteAllText(privatePath, Keys.Value.PrivateKeyPem);
        File.WriteAllText(publicPath, Keys.Value.PublicKeyPem);
        File.WriteAllText(wrongPath, Keys.Value.WrongPrivateKeyPem);
        return (privatePath, publicPath, wrongPath);
    });

    /// <summary>商户私钥 PEM 文件路径（CreateChannelConfig 默认值）。</summary>
    public static string PrivateKeyPath => KeyFiles.Value.Private;

    /// <summary>支付宝公钥 PEM 文件路径（CreateChannelConfig 默认值）。</summary>
    public static string PublicKeyPath => KeyFiles.Value.Public;

    /// <summary>错误私钥 PEM 文件路径（篡改签名负路径——桩用错误私钥签，验签必败）。</summary>
    public static string WrongPrivateKeyPath => KeyFiles.Value.Wrong;

    /// <summary>测试支付宝 channel 配置（Channels 单实例——ChannelId 选区默认首项；PEM 路径指向临时文件）。</summary>
    public static AlipayChannelConfig CreateChannelConfig(
        string channelId = "alipay-main",
        string appId = "test-appid",
        string? privateKeyPath = null,
        string? publicKeyPath = null,
        bool enableMobile = false)
        => new()
        {
            ChannelId = channelId,
            AppId = appId,
            PrivateKeyPath = privateKeyPath ?? PrivateKeyPath,
            AlipayPublicKeyPath = publicKeyPath ?? PublicKeyPath,
            EnableMobile = enableMobile,
        };

    /// <summary>
    /// gateway.do 出站桩 HttpMessageHandler——按表单 method 路由返回<b>签名后的 JSON</b>（不真打支付宝）。
    /// <para>桩内用测试私钥真实签名（<see cref="AlipaySignUtil.BuildSignature"/> 反向签名——对齐
    /// AlipaySignService.VerifyResponse 同步响应验签算法：<c>{"alipay_xxx_response":{...},"sign":"..."}</c>），
    /// 通道验签通过；<paramref name="tamperSignature"/> 时改用<b>错误私钥</b>签名 → 验签失败（信任根负路径）。</para>
    /// <para>路由：<c>alipay.system.oauth.token</c>（auth_code→user_id/access_token；<paramref name="failTokenApi"/>
    /// 时返回业务错误 code=40004/sub_code=isv.code-invalid）/ <c>alipay.user.info.share</c>（snake_case 字段裁剪）。
    /// 其余 method 404。</para>
    /// </summary>
    public static HttpMessageHandler CreateStubHandler(
        string userId = TestUserId,
        bool failTokenApi = false,
        bool tamperSignature = false)
        => new StubAlipayHandler(userId, failTokenApi, tamperSignature);

    /// <summary>构造 SsoChannelAuthContext（IReadOnlyDictionary&lt;string,string?&gt; 参数包）。</summary>
    public static SsoChannelAuthContext CreateContext(params (string Key, string? Value)[] parameters)
        => new(new Dictionary<string, string?>(parameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))));

    /// <summary>Stub HttpMessageHandler 实现（路由 gateway.do——对齐支付宝协议事实链，N4 §二）。</summary>
    private sealed class StubAlipayHandler : HttpMessageHandler
    {
        private readonly string _userId;
        private readonly bool _failTokenApi;   // oauth.token 端点业务错误（code=40004/sub_code=isv.code-invalid）
        private readonly string _signKeyPem;   // 签名私钥（tamperSignature=true 时 = 错误私钥 → 验签必败）

        public StubAlipayHandler(string userId, bool failTokenApi, bool tamperSignature)
        {
            _userId = userId;
            _failTokenApi = failTokenApi;
            _signKeyPem = tamperSignature ? Keys.Value.WrongPrivateKeyPem : Keys.Value.PrivateKeyPem;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            var path = request.RequestUri?.AbsolutePath ?? "";
            if (!path.EndsWith("/gateway.do", StringComparison.Ordinal))
                return Json("{\"error_response\":{\"code\":\"40002\",\"msg\":\"Invalid Parameter\"}}", HttpStatusCode.NotFound);

            var form = await (request.Content?.ReadAsStringAsync(ct) ?? Task.FromResult(""));
            var method = GetFormParam(form, "method");

            if (string.Equals(method, "alipay.system.oauth.token", StringComparison.Ordinal))
            {
                if (_failTokenApi)
                    return Json(SignResponse("alipay_system_oauth_token_response",
                        "{\"code\":\"40004\",\"msg\":\"Business Failed\",\"sub_code\":\"isv.code-invalid\",\"sub_msg\":\"无效的code\"}"));
                return Json(SignResponse("alipay_system_oauth_token_response",
                    $"{{\"user_id\":\"{_userId}\",\"access_token\":\"stub-access-token\",\"expires_in\":3600,\"refresh_token\":\"stub-refresh-token\"}}"));
            }

            if (string.Equals(method, "alipay.user.info.share", StringComparison.Ordinal))
            {
                return Json(SignResponse("alipay_user_info_share_response",
                    $"{{\"code\":\"10000\",\"msg\":\"Success\",\"user_id\":\"{_userId}\",\"open_id\":\"stub-open-id\",\"nick_name\":\"支付宝测试用户\",\"avatar\":\"https://alipay.com/avatar.jpg\",\"province\":\"浙江省\",\"city\":\"杭州市\",\"gender\":\"M\"}}"));
            }

            return Json("{\"error_response\":{\"code\":\"40002\",\"msg\":\"Invalid Parameter\"}}", HttpStatusCode.NotFound);
        }

        /// <summary>RSA2 签名响应（对齐 AlipaySignService.VerifyResponse 算法：剥离 sign 后 <c>key=原始JSON子串</c> 拼接验签）。</summary>
        private string SignResponse(string wrapper, string inner)
        {
            // 反向签名（测试私钥）——single 顶层键（业务体）+ sign 被剥离：签名内容 = wrapper=inner（与 VerifyResponse 自实现拼接一致）
            var sign = AlipaySignUtil.BuildSignature(
                new Dictionary<string, string>(StringComparer.Ordinal) { [wrapper] = inner },
                _signKeyPem);
            return $"{{\"{wrapper}\":{inner},\"sign\":\"{sign}\"}}";
        }

        private static string GetFormParam(string form, string name)
        {
            // form 形如 "a=1&b=2"（FormUrlEncodedContent——%XX 编码 + 空格为 '+' 约定）——手拆避免 System.Web 依赖
            foreach (var pair in form.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                var kv = pair.Split('=', 2);
                if (kv.Length == 2 && kv[0] == name)
                    return Uri.UnescapeDataString(kv[1]).Replace('+', ' ');
            }
            return "";
        }

        private static HttpResponseMessage Json(string json, HttpStatusCode status = HttpStatusCode.OK)
            => new(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
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
/// 测试宿主——真实 DI（AddLogging + AddAlipayFederationChannels + 探针门面 + Stub HttpClientFactory）。
/// <para>继承 <see cref="TestHostBase"/>：BindTestScope 走真实 DomainUser.BindScope；N4 测试类继承本类，
/// 各测试内先设置 <see cref="Handler"/>/<see cref="ConfigureOptions"/> 再调用 <c>BindTestScope()</c>（protected）。</para>
/// </summary>
public class AlipayChannelTestHost : TestHostBase
{
    /// <summary>支付宝 gateway.do 桩 handler（默认正向：user_id=2088000000000000）。</summary>
    public HttpMessageHandler Handler { get; set; } = AlipayTestHost.CreateStubHandler();

    /// <summary>AlipayOptions 编程覆盖（Channels 配置）。</summary>
    public Action<AlipayOptions>? ConfigureOptions { get; set; }

    /// <summary>绑定域作用域（生产路径等价）——public 包装 <see cref="TestHostBase.BindTestScope"/>（protected 不可外部访问；
    /// 测试类不继承 TestHostBase 时经本包装调用）。返回 <c>(provider, user)</c>，经 <c>User.Use&lt;接口&gt;()</c> AOP 解析。</summary>
    public (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindScope()
        => BindTestScope();

    protected override void ConfigureHost(IServiceCollection services)
    {
        // 通道注册（TryAddEnumerableConstructible + IChannelSource 多通道行）——真实生产注册形态
        services.AddAlipayFederationChannels(ConfigureOptions);

        // 多通道联邦核心门面（v0.3.0）——平台库测试宿主不启动 FederationExtensionInitializer，
        // 需补注册"已启用 Federation"的最小生产等价面（对齐 FederationExtensionInitializer.ConfigureServices 对应片段）
        services.AddOptions<FederationStaticChannelOptions>();
        services.AddScoped<StaticChannelRegistry>();
        services.AddConstructibleService<IChannelRegistry, CompositeChannelRegistry>();
        services.AddConstructibleService<ISsoChannelFactory, SsoChannelFactory>();
        services.AddConstructibleService<ISsoLogin, SsoLogin>();

        // 探针门面——帧内枚举 ISsoChannel 集合（守卫工厂供给）
        services.AddConstructibleService<IChannelProbe, ChannelProbe>();

        // Stub IHttpClientFactory——支付宝 gateway.do 桩（AddHttpClient 的 DefaultHttpClientFactory 被覆盖）
        services.AddSingleton<IHttpClientFactory>(new StubHttpClientFactory(Handler));
    }

    /// <summary>Stub IHttpClientFactory（返回固定 handler 的 HttpClient——不真打支付宝）。</summary>
    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;

        public HttpClient CreateClient(string name) => new(_handler);
    }
}
