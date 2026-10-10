using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Federation.Oidc.Tests;

/// <summary>
/// OIDC 通道基座测试公共设施——真实 DI（<c>AddOidcFederationChannel</c>/<c>AddOidcDerivedChannels</c>）
/// + C 基座 <see cref="TestHostBase"/> + 探针门面（帧内枚举 <see cref="ISsoChannel"/> 集合）+
/// **本机 RSA 密钥对 + JWKS mock + id_token 签名助手**（信任根正负路径核心设施）。
/// <para>对齐 WeChat 测试宿主模式：生产路径集成（真实 DI + BindScope + <c>User.Use&lt;接口&gt;()</c> AOP）
/// + 集合版守卫工厂——不手写宿主桩绕过守卫。JWKS/id_token 由测试内部真实 RSA 签发（非 mock 验签）。</para>
/// </summary>
internal static class OidcTestHost
{
    /// <summary>生成 RSA 密钥对（测试签发 id_token + 导出 JWKS）。</summary>
    public static (RSA PrivateKey, RSA PublicKey) CreateRsaKeyPair()
    {
        var rsa = RSA.Create(2048);
        return (rsa, rsa);   // RSA 实例同时可签可验——导出参数区分
    }

    /// <summary>导出 JWKS JSON（含 n/e——RFC 7517，单密钥）。</summary>
    public static string ExportJwks(RSA publicKey, string kid)
    {
        var p = publicKey.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[]
            {
                new
                {
                    kty = "RSA",
                    kid,
                    use = "sig",
                    alg = "RS256",
                    n = Base64UrlEncode(p.Modulus!),
                    e = Base64UrlEncode(p.Exponent!),
                }
            }
        });
    }

    /// <summary>签发 RS256 id_token（测试信任根正路径——真实签名）。tampered=true 模拟攻击者篡改：
    /// 载荷被改（sub→tampered-sub）+ 用**错误密钥**签名（攻击者无原私钥）→ 验签必失败（JWKS 公钥不匹配）。</summary>
    public static string SignIdToken(RSA privateKey, string kid, string issuer, string clientId, string sub,
        string? audOverride = null, string? azp = null, long? expOverrideSecs = null, double? nbfOverride = null,
        bool tampered = false, string? algOverride = null)
    {
        var header = JsonSerializer.Serialize(new { alg = algOverride ?? "RS256", typ = "JWT", kid });
        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["aud"] = audOverride ?? clientId,
            ["sub"] = tampered ? "tampered-sub" : sub,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expOverrideSecs ?? now.AddMinutes(5).ToUnixTimeSeconds(),
        };
        if (azp != null) claims["azp"] = azp;
        if (nbfOverride.HasValue) claims["nbf"] = nbfOverride.Value;

        var payload = JsonSerializer.Serialize(claims);
        var signingInput = $"{Base64UrlEncode(Encoding.UTF8.GetBytes(header))}.{Base64UrlEncode(Encoding.UTF8.GetBytes(payload))}";

        // 签名密钥：篡改场景用攻击者密钥（错误私钥）→ 验签失败；正常场景用原私钥
        using var signingKey = tampered ? RSA.Create(2048) : null;   // 攻击者新密钥
        var key = tampered ? signingKey! : privateKey;
        var sig = key.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Base64UrlEncode(sig)}";
    }

    /// <summary>构造 SsoChannelAuthContext（code + 可选 verifier/redirect_uri）。</summary>
    public static SsoChannelAuthContext CreateContext(params (string Key, string? Value)[] parameters)
        => new(new Dictionary<string, string?>(parameters.Select(p => new KeyValuePair<string, string?>(p.Key, p.Value))));

    internal static string Base64UrlEncode(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>通道枚举探针门面（对齐 WeChat ChannelProbe——帧内枚举 ISsoChannel 集合）。</summary>
public interface IOidcChannelProbe : IDomainService
{
    IReadOnlyList<ISsoChannel> Channels { get; }
}

[DiContractIgnore]
public sealed class OidcChannelProbe : DomainServiceBase, IOidcChannelProbe
{
    private readonly IReadOnlyList<ISsoChannel> _channels;
    public OidcChannelProbe(IDomainUser user, IEnumerable<ISsoChannel> channels) : base(user)
        => _channels = channels?.ToList() ?? throw new ArgumentNullException(nameof(channels));
    public IReadOnlyList<ISsoChannel> Channels => _channels;
}

/// <summary>
/// OIDC Stub HttpMessageHandler——按路径路由：token 端点（code→id_token+access_token）/ JWKS 端点 / Discovery 端点 /
/// userinfo 端点。注入测试密钥（私钥签发 id_token、JwksUri 提供公钥）。
/// </summary>
public sealed class StubOidcHandler : HttpMessageHandler
{
    private readonly RSA _privateKey;
    private readonly string _kid;
    private readonly Func<OidcStubOptions> _options;
    private int _jwkFetchCount;

    public sealed record OidcStubOptions(
        string Issuer,
        string ClientId,
        string Sub,
        bool FailCode = false,
        bool OmitIdToken = false,
        string? AudOverride = null,
        string? Azp = null,
        long? ExpOverrideSecs = null,
        double? NbfOverride = null,
        bool Tampered = false,
        string? AlgOverride = null,
        string SubOverride = "oidc_stub_sub");

    public StubOidcHandler(RSA privateKey, string kid, Func<OidcStubOptions> options)
    {
        _privateKey = privateKey;
        _kid = kid;
        _options = options;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        var path = request.RequestUri?.AbsolutePath ?? "";
        var opt = _options();

        if (path.EndsWith("/token", StringComparison.Ordinal))
        {
            if (opt.FailCode)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.BadRequest)
                { Content = new StringContent("{\"error\":\"invalid_grant\"}", Encoding.UTF8, "application/json") });

            string json;
            if (opt.OmitIdToken)
                json = "{\"access_token\":\"stub-at\",\"expires_in\":3600}";   // 缺 id_token → 拒绝
            else
            {
                var idToken = OidcTestHost.SignIdToken(
                    _privateKey, _kid, opt.Issuer, opt.ClientId, opt.Sub,
                    opt.AudOverride, opt.Azp, opt.ExpOverrideSecs, opt.NbfOverride,
                    opt.Tampered, opt.AlgOverride);
                // 记录访问 token 对（tests 断言用）——简化：返回固定 stub token
                json = $"{{\"id_token\":\"{idToken}\",\"access_token\":\"stub-at\",\"expires_in\":3600}}";
            }
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
        if (path.EndsWith("/jwks", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _jwkFetchCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(OidcTestHost.ExportJwks(_privateKey, _kid), Encoding.UTF8, "application/json") });
        }
        if (path.EndsWith("/.well-known/openid-configuration", StringComparison.Ordinal) || path.EndsWith("well-known/openid-configuration", StringComparison.Ordinal))
        {
            var baseAddr = $"{request.RequestUri!.Scheme}://{request.RequestUri.Authority}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(new
            {
                issuer = _options().Issuer,
                authorization_endpoint = $"{baseAddr}/authorize",
                token_endpoint = $"{baseAddr}/token",
                jwks_uri = $"{baseAddr}/jwks",
                userinfo_endpoint = $"{baseAddr}/userinfo",
            }), Encoding.UTF8, "application/json") });
        }
        if (path.EndsWith("/userinfo", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"sub\":\"stub-sub\",\"email\":\"u@example.com\",\"email_verified\":true,\"name\":\"Stub User\"}", Encoding.UTF8, "application/json") });
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    public int JwkFetchCount => Volatile.Read(ref _jwkFetchCount);
}

/// <summary>测试宿主——真实 DI（AddLogging + AddOidcFederationChannel + 探针门面 + Stub HttpClientFactory）。</summary>
public class OidcChannelTestHost : TestHostBase
{
    /// <summary>直配通道编程配置（<c>OidcPlatformConfig</c> 覆盖——测试设置默认端点后可覆盖增补）。</summary>
    public Action<OidcPlatformConfig>? ConfigureOptions { get; set; }

    /// <summary>OIDC IdP 桩 handler（默认正向：token/JWKS/discovery/userinfo 全 stub）。</summary>
    public HttpMessageHandler TestOidcHandler { get; set; } =
        new StubOidcHandler(RSA.Create(2048), "test-kid",
            () => new StubOidcHandler.OidcStubOptions("https://stub.example.com", "stub-client", "oidc_stub_sub"));

    /// <summary>注册形态开关：true=AddOidcFederationChannel（直配）/ false=AddOidcDerivedChannels（派生，需 TestDerivedChannel）。</summary>
    public bool UseDirectRegistration { get; set; } = true;

    /// <summary>绑定域作用域（public 包装）——经 User.Use&lt;接口&gt;() AOP 解析。</summary>
    public (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindScope()
        => BindTestScope();

    protected override void ConfigureHost(IServiceCollection services)
    {
        if (UseDirectRegistration)
        {
            services.AddOidcFederationChannel("test-oidc", o =>
            {
                // 测试可经 ConfigureOptions 覆盖/增补；默认给全可用端点（直配通道完全配置驱动）
                ConfigureOptions?.Invoke(o);
                if (string.IsNullOrEmpty(o.Platform)) o.Platform = "stub";
                if (string.IsNullOrEmpty(o.ClientId)) o.ClientId = "stub-client";
                if (string.IsNullOrEmpty(o.ClientSecret)) o.ClientSecret = "stub-secret";
                if (string.IsNullOrEmpty(o.AuthorizeUri)) o.AuthorizeUri = "https://stub.example.com/authorize";
                if (string.IsNullOrEmpty(o.TokenUri)) o.TokenUri = "https://stub.example.com/token";
                if (string.IsNullOrEmpty(o.JwksUri)) o.JwksUri = "https://stub.example.com/jwks";
                if (string.IsNullOrEmpty(o.UserInfoUri)) o.UserInfoUri = "https://stub.example.com/userinfo";
                if (o.Scopes.Count == 0) o.Scopes = ["openid", "email"];
                if (o.TokenIssuers.Count == 0) o.TokenIssuers = ["https://stub.example.com"];
            });
        }
        else
        {
            // 派生通道测试（M2 形态预验证）——TestDerivedChannel ctor 经 AddOidcDerivedChannels
            services.AddSingleton<IOptions<TestDerivedOptions>>(_ => Options.Create(
                new TestDerivedOptions { Channels = [new TestDerivedConfig { ChannelId = "derived-1", ClientId = "derived-client" }] }));
            services.AddOidcDerivedChannels<TestDerivedChannel>();
            // 测试派生通道静态来源（TestDerivedOptions.Channels 投影——registry 聚合选区）
            services.TryAddEnumerable(ServiceDescriptor.Scoped<IChannelSource, TestDerivedChannelSource>());
        }

        // 多通道联邦核心门面（v0.3.0）——平台库测试宿主不启动 FederationExtensionInitializer，
        // 需补注册"已启用 Federation"的最小生产等价面（对齐 FederationExtensionInitializer.ConfigureServices
        // 对应片段）：StaticChannelRegistry + CompositeChannelRegistry + SsoChannelFactory + SsoLogin
        services.AddOptions<FederationStaticChannelOptions>();
        services.AddScoped<StaticChannelRegistry>();
        services.AddConstructibleService<IChannelRegistry, CompositeChannelRegistry>();
        services.AddConstructibleService<ISsoChannelFactory, SsoChannelFactory>();
        services.AddConstructibleService<ISsoLogin, SsoLogin>();

        services.AddConstructibleService<IOidcChannelProbe, OidcChannelProbe>();
        services.AddSingleton<IHttpClientFactory>(_ => new StubHttpClientFactory(TestOidcHandler));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }
}

/// <summary>派生通道测试形态（M2 GoogleOidcChannel 骨架验证——Defaults 填端点 + BuildChannelId 覆写）。</summary>
public sealed class TestDerivedConfig
{
    public string ChannelId { get; set; } = "";
    public string ClientId { get; set; } = "";
    public List<string> TokenIssuers { get; set; } = [];
}

public sealed class TestDerivedOptions
{
    public List<TestDerivedConfig> Channels { get; set; } = [];
}

/// <summary>
/// 测试派生通道静态来源（多通道联邦 v0.3.0）——<see cref="TestDerivedOptions.Channels"/> 投影为统一
/// <see cref="ChannelConfig"/>（ClientId→公共列 AppId、TokenIssuers 进 Extra），供 registry 聚合选区。
/// PlatformType = "derived"（与 <c>derived_oidc</c> 前缀匹配——工厂 null 选区推导兼容）。
/// </summary>
public sealed class TestDerivedChannelSource : IChannelSource
{
    private readonly IOptions<TestDerivedOptions> _options;

    public TestDerivedChannelSource(IOptions<TestDerivedOptions> options)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public string PlatformType => "derived";

    public IReadOnlyList<ChannelConfig> GetChannels()
        => _options.Value.Channels
            .Select(c => new ChannelConfig
            {
                ChannelId = c.ChannelId,
                PlatformType = PlatformType,
                AppId = string.IsNullOrWhiteSpace(c.ClientId) ? null : c.ClientId,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [OidcChannelConfigKeys.Platform] = "derived",
                    [OidcChannelConfigKeys.TokenIssuers] = JsonSerializer.Serialize(c.TokenIssuers),
                },
            })
            .ToList();
}

public sealed class TestDerivedChannel : OidcChannelBase
{
    /// <summary>构造——channel 由工厂预取（POCO 非域服务）；null = 集合模板实例（Defaults 填端点）。</summary>
    public TestDerivedChannel(IDomainUser user, OidcChannelFlow flow, ChannelConfig? channel = null)
        : base(user, flow, channel)
    {
    }

    public override string ChannelType => "derived_oidc";
    protected override string BuildChannelId(OidcPlatformConfig config) => $"{ChannelType}:{config.ClientId}";   // pairwise 复合编码
    protected override OidcPlatformConfig Defaults() => new()
    {
        Platform = "derived",
        TokenUri = "https://stub.example.com/token",
        JwksUri = "https://stub.example.com/jwks",
        AuthorizeUri = "https://stub.example.com/authorize",
        Scopes = ["openid"],
        TokenIssuers = ["https://stub.example.com"],
    };
}