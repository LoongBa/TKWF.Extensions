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

namespace TKWF.Federation.Microsoft.Tests;

/// <summary>Microsoft 测试基础设施——Stub Microsoft IdP（本机 RSA 签发 id_token + JWKS）+ 探针门面 + 宿主。</summary>
internal static class MicrosoftTestHost
{
    /// <summary>Microsoft 平台配置（tenant 模板化——测试默认 organizations）。</summary>
    public static OidcPlatformConfig CreatePlatformConfig(
        string tenant = "organizations",
        string clientId = "microsoft-client-1",
        string clientSecret = "microsoft-secret")
        => new()
        {
            ChannelId = "ms-main",
            Platform = "microsoft",
            ClientId = clientId,
            ClientSecret = clientSecret,
            AuthorizeUri = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/authorize",
            TokenUri = $"https://login.microsoftonline.com/{tenant}/oauth2/v2.0/token",
            UserInfoUri = "https://graph.microsoft.com/oidc/userinfo",
            JwksUri = $"https://login.microsoftonline.com/{tenant}/discovery/v2.0/keys",
            DiscoveryUri = $"https://login.microsoftonline.com/{tenant}/v2.0/.well-known/openid-configuration",
            Scopes = ["openid", "profile", "email"],
            UsePkce = true,
            // ⚠️ common/consumers/organizations 授权后 iss 含实际租户 GUID——正则匹配（M2-P1-1）
            TokenIssuers = [$"https://login.microsoftonline.com/{tenant}/v2.0",
                            "^https://login\\.microsoftonline\\.com/[^/]+/v2\\.0$"],
        };

    /// <summary>签发 RS256 id_token（tenant GUID 模拟——common 授权后 iss 含实际租户 GUID；IssuerOverride 支持 evil iss）。</summary>
    public static string SignIdToken(RSA privateKey, string kid, string sub,
        string clientId = "microsoft-client-1",
        string tenant = "organizations",
        string tenantGuid = "common",
        bool tampered = false, string? algOverride = null, long? expOverrideSecs = null,
        string? issuerOverride = null)
    {
        var header = JsonSerializer.Serialize(new { alg = algOverride ?? "RS256", typ = "JWT", kid });
        var now = DateTimeOffset.UtcNow;
        // iss = 实际租户 GUID（模拟 Microsoft 行为——common 授权后 tid 替换）；IssuerOverride 优先（evil 模拟）
        var actualIssuer = issuerOverride
            ?? (tenant == "common" || tenant == "consumers" || tenant == "organizations"
                ? $"https://login.microsoftonline.com/{tenantGuid}/v2.0"
                : $"https://login.microsoftonline.com/{tenant}/v2.0");
        var claims = new Dictionary<string, object>
        {
            ["iss"] = actualIssuer,
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

    /// <summary>导出 JWKS JSON（单密钥）。</summary>
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
            ["code"] = withCode ? "ms-code-1" : null,
            ["redirect_uri"] = "https://app.example.com/callback",
            ["code_verifier"] = withVerifier ? OidcAuthFlow.GenerateCodeVerifier() : null,
        });

    internal static string B64(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>通道枚举探针门面（帧内枚举 ISsoChannel 集合）。</summary>
public interface IMicrosoftChannelProbe : IDomainService
{
    IReadOnlyList<ISsoChannel> Channels { get; }
}

[DiContractIgnore]
public sealed class MicrosoftChannelProbe : DomainServiceBase, IMicrosoftChannelProbe
{
    private readonly IReadOnlyList<ISsoChannel> _channels;
    public MicrosoftChannelProbe(IDomainUser user, IEnumerable<ISsoChannel> channels) : base(user)
        => _channels = channels?.ToList() ?? throw new ArgumentNullException(nameof(channels));
    public IReadOnlyList<ISsoChannel> Channels => _channels;
}

/// <summary>Stub Microsoft IdP handler——按路径路由 token / jwks / discovery / userinfo（tenant 模板化）。</summary>
public sealed class StubMicrosoftHandler : HttpMessageHandler
{
    private readonly RSA _privateKey;
    private readonly string _kid;
    private readonly Func<StubOptions> _options;

    public sealed record StubOptions(
        string Sub = "ms_stub_sub",
        string ClientId = "microsoft-client-1",
        string Tenant = "organizations",
        string TenantGuid = "guid-11111111-2222-3333-4444-555555555555",
        string? IssuerOverride = null,   // evil iss 模拟（白名单拒）
        bool Tampered = false,
        string? AlgOverride = null,
        long? ExpOverrideSecs = null);

    public StubMicrosoftHandler(RSA privateKey, string kid, Func<StubOptions> options)
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
            var idToken = MicrosoftTestHost.SignIdToken(_privateKey, _kid, opt.Sub,
                opt.ClientId, opt.Tenant, opt.TenantGuid, opt.Tampered, opt.AlgOverride, opt.ExpOverrideSecs,
                opt.IssuerOverride);
            var json = $"{{\"id_token\":\"{idToken}\",\"access_token\":\"stub-at\",\"expires_in\":3600}}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        }
        if (path.EndsWith("/keys", StringComparison.Ordinal) || path.EndsWith("/jwks", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(MicrosoftTestHost.ExportJwks(_privateKey, _kid), Encoding.UTF8, "application/json") });
        }
        if (path.Contains("/.well-known/openid-configuration", StringComparison.Ordinal))
        {
            var baseAddr = $"{request.RequestUri!.Scheme}://{request.RequestUri.Authority}";
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent(JsonSerializer.Serialize(new
            {
                issuer = $"https://login.microsoftonline.com/{opt.TenantGuid}/v2.0",
                authorization_endpoint = $"{baseAddr}/authorize",
                token_endpoint = $"{baseAddr}/token",
                jwks_uri = $"{baseAddr}/keys",
                userinfo_endpoint = $"{baseAddr}/userinfo",
            }), Encoding.UTF8, "application/json") });
        }
        if (path.EndsWith("/userinfo", StringComparison.Ordinal))
        {
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            { Content = new StringContent("{\"sub\":\"stub\",\"email\":\"u@example.com\",\"email_verified\":true,\"name\":\"MS User\"}", Encoding.UTF8, "application/json") });
        }
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

/// <summary>Microsoft 测试宿主——真实 DI（AddMicrosoftFederationChannels + 探针门面 + Stub HttpClientFactory）。</summary>
public class MicrosoftChannelTestHost : TestHostBase
{
    /// <summary>MicrosoftOptions 编程覆盖（Channels 配置——含 Tenant）。</summary>
    public Action<MicrosoftOptions>? ConfigureOptions { get; set; }

    /// <summary>Stub Microsoft handler（默认正向）。</summary>
    public HttpMessageHandler Handler { get; set; } =
        new StubMicrosoftHandler(RSA.Create(2048), "ms-kid", () => new StubMicrosoftHandler.StubOptions());

    /// <summary>绑定域作用域（public 包装）。</summary>
    public (ServiceProvider Provider, DomainUser<TestUserInfo> User) BindScope()
        => BindTestScope();

    protected override void ConfigureHost(IServiceCollection services)
    {
        services.AddMicrosoftFederationChannels(o =>
        {
            // 测试可经 ConfigureOptions 覆盖；默认填充可用配置（对齐 Google/M1 宿主模式）
            ConfigureOptions?.Invoke(o);
            if (o.Channels.Count == 0)
            {
                o.Channels = [new MicrosoftChannelConfig
                {
                    ChannelId = "ms-main",
                    ClientId = "microsoft-client-1",
                    ClientSecret = "microsoft-secret",
                    Tenant = "organizations",
                    TokenIssuers = ["^https://login\\.microsoftonline\\.com/[^/]+/v2\\.0$"],
                }];
            }
        });
        services.AddConstructibleService<IMicrosoftChannelProbe, MicrosoftChannelProbe>();
        services.AddSingleton<IHttpClientFactory>(_ => new StubHttpClientFactory(Handler));
    }

    private sealed class StubHttpClientFactory : IHttpClientFactory
    {
        private readonly HttpMessageHandler _handler;
        public StubHttpClientFactory(HttpMessageHandler handler) => _handler = handler;
        public HttpClient CreateClient(string name) => new(_handler);
    }
}