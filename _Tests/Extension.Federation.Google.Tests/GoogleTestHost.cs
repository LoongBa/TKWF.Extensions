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
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using TKWF.Federation.Oidc;

namespace TKWF.Federation.Google.Tests;

/// <summary>Google 测试基础设施——Stub Google IdP（本机 RSA 签发 id_token + JWKS）+ 探针门面 + 宿主。</summary>
internal static class GoogleTestHost
{
    /// <summary>Google 平台配置（OidcPlatformConfig 直接构造——测试直配形态，简化宿主）。</summary>
    public static OidcPlatformConfig CreatePlatformConfig(
        string clientId = "google-client-1",
        string clientSecret = "google-secret",
        string? channelId = null)
        => new()
        {
            ChannelId = channelId ?? "google-main",
            Platform = "google",
            ClientId = clientId,
            ClientSecret = clientSecret,
            AuthorizeUri = "https://accounts.google.com/o/oauth2/v2/auth",
            TokenUri = "https://oauth2.googleapis.com/token",
            UserInfoUri = "https://openidconnect.googleapis.com/v1/userinfo",
            JwksUri = "https://www.googleapis.com/oauth2/v3/certs",
            DiscoveryUri = "https://accounts.google.com/.well-known/openid-configuration",
            Scopes = ["openid", "email", "profile"],
            UsePkce = true,
            TokenIssuers = ["https://accounts.google.com"],
        };

    /// <summary>签发 RS256 id_token（测试信任根正路径——真实签名）。tampered=true 用攻击者密钥（验签必失败）。</summary>
    public static string SignIdToken(RSA privateKey, string kid, string sub,
        string clientId = "google-client-1", string issuer = "https://accounts.google.com",
        bool tampered = false, string? algOverride = null, long? expOverrideSecs = null)
    {
        var header = JsonSerializer.Serialize(new { alg = algOverride ?? "RS256", typ = "JWT", kid });
        var now = DateTimeOffset.UtcNow;
        var claims = new Dictionary<string, object>
        {
            ["iss"] = issuer,
            ["aud"] = clientId,
            ["sub"] = tampered ? "tampered-sub" : sub,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = expOverrideSecs ?? now.AddMinutes(5).ToUnixTimeSeconds(),
        };
        var payload = JsonSerializer.Serialize(claims);
        var signingInput = $"{B64(Encoding.UTF8.GetBytes(header))}.{B64(Encoding.UTF8.GetBytes(payload))}";
        using var attackerKey = tampered ? RSA.Create(2048) : null;
        var key = tampered ? attackerKey! : privateKey;
        var sig = key.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{B64(sig)}";
    }

    /// <summary>导出 JWKS JSON（单密钥——RFC 7517）。</summary>
    public static string ExportJwks(RSA publicKey, string kid)
    {
        var p = publicKey.ExportParameters(false);
        return JsonSerializer.Serialize(new
        {
            keys = new[] { new { kty = "RSA", kid, use = "sig", alg = "RS256", n = B64(p.Modulus!), e = B64(p.Exponent!) } }
        });
    }

    /// <summary>构造 SsoChannelAuthContext（code + redirect_uri + verifier）。</summary>
    public static SsoChannelAuthContext CreateContext(bool withVerifier = true, bool withCode = true)
        => new(new Dictionary<string, string?>
        {
            ["code"] = withCode ? "google-code-1" : null,
            ["redirect_uri"] = "https://app.example.com/callback",
            ["code_verifier"] = withVerifier ? OidcChannelFlow.GenerateCodeVerifier() : null,
        });

    internal static string B64(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>通道枚举探针门面（帧内枚举 ISsoChannel 集合）。</summary>
public interface IGoogleChannelProbe : IDomainService
{
    IReadOnlyList<ISsoChannel> Channels { get; }
}

[DiContractIgnore]
public sealed class GoogleChannelProbe : DomainServiceBase, IGoogleChannelProbe
{
    private readonly IReadOnlyList<ISsoChannel> _channels;
    public GoogleChannelProbe(IDomainUser user, IEnumerable<ISsoChannel> channels) : base(user)
        => _channels = channels?.ToList() ?? throw new ArgumentNullException(nameof(channels));
    public IReadOnlyList<ISsoChannel> Channels => _channels;
}

/// <summary>Stub Google IdP handler——按路径路由 token（code→id_token+access_token）/ jwks / discovery / userinfo。</summary>
public sealed class StubGoogleHandler : HttpMessageHandler
{
    private readonly RSA _privateKey;
    private readonly string _kid;
    private readonly Func<StubOptions> _options;
    private int _jwkFetchCount;

    public sealed record StubOptions(
        string Sub = "google_stub_sub",
        bool Tampered = false,
        string? AlgOverride = null,
        long? ExpOverrideSecs = null);

    public StubGoogleHandler(RSA privateKey, string kid, Func<StubOptions> options)
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
            var idToken = GoogleTestHost.SignIdToken(_privateKey, _kid, opt.Sub, tampered: opt.Tampered,
                algOverride: opt.AlgOverride, expOverrideSecs: opt.ExpOverrideSecs);
            var json = $"{{\"id_token\":\"{idToken}\",\"access_token\":\"stub-at\",\"expires_in\":3600}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
        if (path.EndsWith("/certs", StringComparison.Ordinal) || path.EndsWith("/jwks", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref _jwkFetchCount);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(GoogleTestHost.ExportJwks(_privateKey, _kid), Encoding.UTF8, "application/json") });
        }
        if (path.Contains("/.well-known/openid-configuration", StringComparison.Ordinal))
        {
            var baseAddr = $"{request.RequestUri!.Scheme}://{request.RequestUri.Authority}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(new
            {
                issuer = "https://accounts.google.com",
                authorization_endpoint = $"{baseAddr}/auth",
                token_endpoint = $"{baseAddr}/token",
                jwks_uri = $"{baseAddr}/certs",
                userinfo_endpoint = $"{baseAddr}/userinfo",
            }), Encoding.UTF8, "application/json") });
        }
        if (path.EndsWith("/userinfo", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"sub\":\"stub\",\"email\":\"u@example.com\",\"email_verified\":true,\"name\":\"Google User\"}", Encoding.UTF8, "application/json") });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }

    public int JwkFetchCount => Volatile.Read(ref _jwkFetchCount);
}

/// <summary>Google 测试宿主——真实 DI（AddGoogleFederationChannels + 探针门面 + Stub HttpClientFactory）。</summary>
public class GoogleChannelTestHost : TestHostBase
{
    /// <summary>GoogleOptions 编程覆盖（Channels 配置）。</summary>
    public Action<GoogleOptions>? ConfigureOptions { get; set; }

    /// <summary>Stub Google handler（默认正向）。</summary>
    public HttpMessageHandler Handler { get; set; } =
        new StubGoogleHandler(RSA.Create(2048), "google-kid", () => new StubGoogleHandler.StubOptions());

    /// <summary>绑定域作用域（public 包装）。</summary>
    public (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindScope()
        => BindTestScope();

    protected override void ConfigureHost(IServiceCollection services)
    {
        services.AddGoogleFederationChannels(o =>
        {
            // 测试可经 ConfigureOptions 覆盖；默认填充可用配置（对齐 M1 OidcTestHost 模式）
            ConfigureOptions?.Invoke(o);
            if (o.Channels.Count == 0)
            {
                o.Channels = [new GoogleChannelConfig
                {
                    ChannelId = "google-main",
                    ClientId = "google-client-1",
                    ClientSecret = "google-secret",
                    TokenIssuers = ["https://accounts.google.com"],
                }];
            }
        });

        // 多通道联邦核心门面（v0.3.0）——平台库测试宿主不启动 FederationExtensionInitializer，
        // 需补注册"已启用 Federation"的最小生产等价面（对齐 FederationExtensionInitializer.ConfigureServices
        // 对应片段）：StaticChannelRegistry + CompositeChannelRegistry + SsoChannelFactory + SsoLogin
        services.AddOptions<FederationStaticChannelOptions>();
        services.AddScoped<StaticChannelRegistry>();
        services.AddConstructibleService<IChannelRegistry, CompositeChannelRegistry>();
        services.AddConstructibleService<ISsoChannelFactory, SsoChannelFactory>();
        services.AddConstructibleService<ISsoLogin, SsoLogin>();

        services.AddConstructibleService<IGoogleChannelProbe, GoogleChannelProbe>();
        services.AddSingleton<IHttpClientFactory>(_ => new StubHttpClientFactory(Handler));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }
}