using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Federation.Oidc;

/// <summary>
/// OIDC 出站协议流（M1 基座——国外规划 §五最小职责 = channel 适配 + 配置驱动）：
/// <para>① <c>DiscoverAsync</c>——Discovery 解析（可选，配置驱动 URL——LinkedIn <c>/oauth/.well-known/</c>
/// 非标路径须显式；Apple 双端点并存 issuer 冲突可省略 discovery 纯端点配置）；</para>
/// <para>② <c>BuildAuthorizeUrl</c>——authorize URL 构造（装配层消费；state/redirect_uri 归装配层传递，
/// 本方法只组装协议参数——对齐 WeChat 先例"authorize 构造归装配层"层次）；</para>
/// <para>③ <c>ExchangeCodeAsync</c>——code→token（grant_type=authorization_code + client_secret post /
/// private_key_jwt 断言 + redirect_uri 一致性校验 RFC 6749 §4.1.3 + PKCE code_verifier）+ id_token 验签
/// （JwksManager + OidcIdTokenValidator——信任根核心）；</para>
/// <para>④ <c>FetchUserInfoAsync</c>——userinfo 拉取（标准端点，响应裁剪）。</para>
/// <para>端点覆盖规则：显式配置优先 → Discovery 解析覆盖。HttpClient 经 typed client 注入。</para>
/// </summary>
public sealed class OidcAuthFlow
{
    private readonly HttpClient _httpClient;
    private readonly JwksManager _jwks;
    private readonly OidcIdTokenValidator _validator;
    private readonly ILogger<OidcAuthFlow> _logger;

    public OidcAuthFlow(
        HttpClient httpClient,
        JwksManager jwks,
        OidcIdTokenValidator validator,
        ILogger<OidcAuthFlow> logger)
    {
        _httpClient = httpClient;
        _jwks = jwks ?? throw new ArgumentNullException(nameof(jwks));
        _validator = validator;
        _logger = logger;
    }

    /// <summary>
    /// Discovery 解析结果（端点配置驱动——LinkedIn/Apple 场景显式覆盖）。
    /// </summary>
    public sealed record DiscoveryResult(string? AuthorizeUri, string? TokenUri, string? UserInfoUri, string? JwksUri, string? Issuer);

    /// <summary>Discovery 解析（可选——配置驱动 URL；失败记录 Warning 返回 null，回退显式端点配置）。</summary>
    public async Task<DiscoveryResult?> DiscoverAsync(OidcPlatformConfig config, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.DiscoveryUri))
            return null;

        try
        {
            var url = config.DiscoveryUri;
            using var resp = await _httpClient.GetAsync(url, ct);
            resp.EnsureSuccessStatusCode();
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new DiscoveryResult(
                GetString(root, "authorization_endpoint"),
                GetString(root, "token_endpoint"),
                GetString(root, "userinfo_endpoint"),
                GetString(root, "jwks_uri"),
                GetString(root, "issuer"));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            _logger.LogWarning("OIDC Discovery 解析失败（回退显式端点配置）：{DiscoveryUri} —— {Msg}", config.DiscoveryUri, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 构造 authorize URL（装配层消费——state/redirect_uri 由装配层生成本方法只组装协议参数；
    /// PKCE code_challenge 由调用方生成传入（无状态——每次授权新挑战，装配层持有 verifier 供回调兑换）。
    /// </summary>
    public string BuildAuthorizeUrl(
        OidcPlatformConfig config,
        DiscoveryResult? discovery,
        string redirectUri,
        string state,
        string? codeChallenge,
        string? additionalParams = null)
    {
        var authorizeUri = !string.IsNullOrWhiteSpace(config.AuthorizeUri)
            ? config.AuthorizeUri
            : discovery?.AuthorizeUri
            ?? throw new InvalidOperationException($"authorize 端点未配置：平台 {config.Platform}");

        var query = new StringBuilder();
        query.Append("response_type=code");
        query.Append("&client_id=").Append(Uri.EscapeDataString(config.ClientId));
        query.Append("&redirect_uri=").Append(Uri.EscapeDataString(redirectUri));
        query.Append("&scope=").Append(Uri.EscapeDataString(string.Join(" ", config.Scopes)));
        query.Append("&state=").Append(Uri.EscapeDataString(state));

        if (config.UsePkce)
        {
            if (!string.IsNullOrEmpty(codeChallenge))
            {
                query.Append("&code_challenge=").Append(Uri.EscapeDataString(codeChallenge));
                query.Append("&code_challenge_method=S256");
            }
        }

        if (!string.IsNullOrEmpty(additionalParams))
            query.Append(additionalParams.StartsWith('&') ? additionalParams : $"&{additionalParams}");

        var sep = authorizeUri.Contains('?') ? "&" : "?";
        return $"{authorizeUri}{sep}{query}";
    }

    /// <summary>生成 PKCE code_verifier（RFC 7636——43-128 位 base64url 无填充，CSPRNG 43 字节 → base64url）。</summary>
    public static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(43);
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>S256 code_challenge（SHA256(verifier) → base64url 无填充）。</summary>
    public static string ComputeCodeChallenge(string verifier)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(verifier));
        return Convert.ToBase64String(hash).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>exchange 结果（id_token 验签通过 + sub）。</summary>
    public sealed record ExchangeResult(OidcIdTokenValidationResult IdToken, string? AccessToken, string? RefreshToken, TimeSpan? ExpiresIn);

    /// <summary>
    /// code→token（OAuth 2.1 + PKCE）：POST token 端点（client_secret post 或 private_key_jwt 断言）+
    /// redirect_uri 一致性校验（RFC 6749 §4.1.3——code 窃取防护，发回授权时同名 redirect_uri）+
    /// PKCE code_verifier（UsePkce 时必传，服务端 S256 比对——缺失/不匹配负路径）。返回 id_token 验签结果
    /// + access_token（用户级 token 不缓存——联邦认证面一次性消费，对齐 N3 QQ P1-5；refresh 续票归 L7 YAGNI）。
    /// </summary>
    public async Task<ExchangeResult> ExchangeCodeAsync(
        OidcPlatformConfig config,
        DiscoveryResult? discovery,
        string code,
        string redirectUri,
        string? codeVerifier,
        CancellationToken ct = default)
    {
        var tokenUri = !string.IsNullOrWhiteSpace(config.TokenUri)
            ? config.TokenUri
            : discovery?.TokenUri
            ?? throw new InvalidOperationException($"token 端点未配置：平台 {config.Platform}");

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,   // RFC 6749 §4.1.3 一致性——服务端比对授权时 redirect_uri
        };

        // 认证方式：client_secret post（默认）/ private_key_jwt（企业证书场景——M2-P2-1 预留）
        if (string.IsNullOrWhiteSpace(config.ClientAssertionSigningKeyPath))
        {
            form["client_id"] = config.ClientId;
            form["client_secret"] = config.ClientSecret;
        }
        else
        {
            var assertion = BuildPrivateKeyJwtAssertion(config);
            form["client_id"] = config.ClientId;
            form["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer";
            form["client_assertion"] = assertion;
        }

        // PKCE：S256——服务端按 state 关联的 verifier 比对 code_challenge；缺失/不匹配由 IdP 拒（负路径）
        if (config.UsePkce)
        {
            if (string.IsNullOrEmpty(codeVerifier))
                throw new InvalidOperationException("PKCE 启用但 code_verifier 缺失（授权时生成、回调兑换必须传回）");
            form["code_verifier"] = codeVerifier;
        }

        using var content = new FormUrlEncodedContent(form);
        using var resp = await _httpClient.PostAsync(tokenUri, content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"OIDC token 端点失败 HTTP {(int)resp.StatusCode}：{body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;

        var idToken = root.TryGetProperty("id_token", out var idEl) ? idEl.GetString() : null;
        if (string.IsNullOrEmpty(idToken))
            throw new InvalidOperationException("OIDC token 响应缺 id_token");

        // ⚠️ 信任根——id_token 验签（JWKS + RS256 + iss/aud/azp/exp/nbf/sub）：链路远端对 id_token 的信任核心
        var jwksUri = !string.IsNullOrWhiteSpace(config.JwksUri)
            ? config.JwksUri
            : discovery?.JwksUri
            ?? throw new InvalidOperationException($"JWKS 端点未配置：平台 {config.Platform}");

        var validation = await _validator.ValidateAsync(idToken, jwksUri, config.ClientId, config.TokenIssuers, ct);

        return new ExchangeResult(
            validation,
            root.TryGetProperty("access_token", out var at) ? at.GetString() : null,
            root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var secs) ? TimeSpan.FromSeconds(secs) : null);
    }

    /// <summary>userinfo 拉取（标准端点；响应裁剪为 <see cref="OidcUserInfo"/>——email 仅展示字段不参与身份归一）。</summary>
    public async Task<OidcUserInfo?> FetchUserInfoAsync(
        OidcPlatformConfig config,
        DiscoveryResult? discovery,
        string accessToken,
        CancellationToken ct = default)
    {
        var userInfoUri = !string.IsNullOrWhiteSpace(config.UserInfoUri)
            ? config.UserInfoUri
            : discovery?.UserInfoUri;
        if (string.IsNullOrEmpty(userInfoUri))
            return null;

        using var req = new HttpRequestMessage(HttpMethod.Get, userInfoUri);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        using var resp = await _httpClient.SendAsync(req, ct);
        if (!resp.IsSuccessStatusCode)
        {
            _logger.LogWarning("OIDC userinfo 获取失败 HTTP {(Code)}（懒加载降级 null）", (int)resp.StatusCode);
            return null;
        }

        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new OidcUserInfo(
            GetString(root, "sub") ?? "",
            GetString(root, "email"),
            root.TryGetProperty("email_verified", out var ev) && ev.ValueKind == JsonValueKind.True,
            GetString(root, "name"),
            GetString(root, "preferred_username"));
    }

    /// <summary>private_key_jwt 断言（企业证书认证——RFC 7523；按 config.ClientAssertionSigningKeyPath 加载 RSA 私钥签名）。</summary>
    private string BuildPrivateKeyJwtAssertion(OidcPlatformConfig config)
    {
        const string header = "{\"alg\":\"RS256\",\"typ\":\"JWT\"}";
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = config.ClientId,
            ["sub"] = config.ClientId,
            ["aud"] = config.TokenUri,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
        });

        var signingInput = $"{B64(header)}.{B64(payload)}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(config.ClientAssertionSigningKeyPath));
        var sig = rsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Convert.ToBase64String(sig).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    }

    static string B64(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}