using System;
using System.Collections.Concurrent;
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
using TKW.Framework.Utility.OAuthClient;

namespace TKWF.Federation.Oidc;

/// <summary>id_token 验签结果（验签通过后归一 <c>SsoChannelAuthResult.ExternalUserId = sub</c>）。</summary>
public sealed record OidcIdTokenValidationResult(string Sub, string Issuer, string Audience, string? AuthorizedParty);

/// <summary>
/// OIDC 出站协议流薄层（OIDC 原语归并 Utility 引擎后——M1 <c>OidcAuthFlow</c> 改造，OIDC 归并方案 §3.1）。
/// <para><b>委托引擎</b>（<c>TKW.Framework.Utility.OAuthClient</c>，v4.10.64——F1 终态）：</para>
/// <para>① <c>BuildAuthorizeUrl</c>——引擎 <c>OAuthClient.BuildAuthorizeUrl()</c>（引擎生成 CSPRNG state + PKCE
/// verifier/challenge——装配层持久化 <c>OAuthAuthorizeResult.State/PkceVerifier</c>，回调经 context 传回）；</para>
/// <para>② <c>ExchangeCodeAsync</c> 常规路径——引擎 <c>OAuthClient.ExchangeCodeAsync</c>（client_secret post 形态，
/// ClientAuth=QueryParameter）+ redirect_uri 一致性（RFC 6749 §4.1.3）+ PKCE S256 + **id_token 验签委托
/// <see cref="IdTokenDecoder.ValidateAsync"/>**（JWKS kid 路由 + RS256 强制 + iss 通配/正则 + aud/azp + exp/iat/nbf leeway + sub）；</para>
/// <para>③ <c>FetchUserInfoAsync</c>——引擎 <c>OAuthClient.FetchUserInfoAsync</c>（懒加载降级 null 语义保留）；</para>
/// <para>④ <c>DiscoverAsync</c>——引擎 <c>OidcDiscoveryClient</c>（v4.10.64 A 类边界扩展——discovery 上移；本层保留
/// 容忍语义：失败降级 null 回退显式端点配置）。</para>
/// <para><b>保留内联</b>（过渡期 + 终态——引擎无对应能力）：private_key_jwt（RFC 7523——引擎
/// <c>ClientAuthenticationScheme</c> 无 <c>PrivateKeyJwt</c>，OIDC 归并方案 P1-8）。</para>
/// <para><b>删内联重复</b>：M1 <c>JwksManager</c>/<c>OidcIdTokenValidator</c> 已上移引擎
/// （<see cref="JwksManager"/>/<see cref="IdTokenDecoder"/>——kid 精确匹配 + 墓园 Dispose + 512KB 钳制 + RSA≥2048 过滤 +
/// iss 白名单 + azp + leeway + nbf + exp 强校验）。验签密钥 L1 缓存：本流自持 <see cref="JwksManager"/> 按 jwksUri 键缓存
/// （对齐 M1 单实例设计——JWKS 缓存 TTL 1h 保留）。</para>
/// <para>端点覆盖规则：显式配置优先 → Discovery 解析覆盖。HttpClient 经 typed client 注入。</para>
/// </summary>
public sealed class OidcChannelFlow : IDisposable
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<OidcChannelFlow> _logger;
    private readonly ConcurrentDictionary<string, JwksManager> _jwksCache = new(StringComparer.Ordinal);
    private bool _disposed;

    /// <summary>构造——HttpClient 经 typed client 注入（AddHttpClient&lt;OidcChannelFlow&gt;；引擎内部共享本 HttpClient）。</summary>
    public OidcChannelFlow(HttpClient httpClient, ILogger<OidcChannelFlow> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _logger = logger;
    }

    /// <summary>
    /// Discovery 解析结果（端点配置驱动——LinkedIn/Apple 场景显式覆盖）。
    /// </summary>
    public sealed record DiscoveryResult(string? AuthorizeUri, string? TokenUri, string? UserInfoUri, string? JwksUri, string? Issuer);

    /// <summary>Discovery 解析（可选——配置驱动 URL；**委托引擎 <see cref="OidcDiscoveryClient"/>**，v4.10.64 A 类边界扩展）。
    /// 容忍语义保留（M1 契约）：解析失败（非 https / 缺必填键 / HTTP 错误 / JSON 损坏）→ 记录 Warning 返回 null，回退显式端点配置。</summary>
    public async Task<DiscoveryResult?> DiscoverAsync(OidcPlatformConfig config, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(config.DiscoveryUri))
            return null;

        try
        {
            var result = await new OidcDiscoveryClient(_httpClient)
                .DiscoverAsync(new Uri(config.DiscoveryUri, UriKind.Absolute), ct)
                .ConfigureAwait(false);
            return new DiscoveryResult(
                result.AuthorizeEndpoint.AbsoluteUri,
                result.TokenEndpoint.AbsoluteUri,
                result.UserInfoEndpoint?.AbsoluteUri,
                result.JwksEndpoint.AbsoluteUri,
                result.Issuer);
        }
        catch (Exception ex) when (ex is OAuthProtocolException or HttpRequestException or JsonException or UriFormatException)
        {
            _logger.LogWarning("OIDC Discovery 解析失败（回退显式端点配置）：{DiscoveryUri} —— {Msg}", config.DiscoveryUri, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// 构造 authorize URL（**委托引擎**——装配层消费；引擎生成 CSPRNG state + PKCE verifier/challenge）。
    /// <para>⚠️ 契约变化（M1 → 引擎）：M1 由装配层生成 state/codeChallenge 传入；引擎 <c>BuildAuthorizeUrl()</c> 自行生成
    /// 并返回 <see cref="OAuthAuthorizeResult"/>——装配层须持久化 <c>State</c>（回调 CSRF 校验经 <c>OAuthClient.ValidateState</c>）
    /// 与 <c>PkceVerifier</c>（回调经 context <c>code_verifier</c> 传回兑换）。</para>
    /// </summary>
    public OAuthAuthorizeResult BuildAuthorizeUrl(OidcPlatformConfig config, DiscoveryResult? discovery, string redirectUri)
    {
        var authorizeUri = Resolve(config.AuthorizeUri, discovery?.AuthorizeUri, $"authorize 端点未配置：平台 {config.Platform}");
        var tokenUri = Resolve(config.TokenUri, discovery?.TokenUri, null) ?? "https://invalid.local/token";
        var userInfoUri = Resolve(config.UserInfoUri, discovery?.UserInfoUri, null);
        var engine = new OAuthClient(_httpClient, BuildEngineOptions(config, authorizeUri, tokenUri, userInfoUri, redirectUri));
        return engine.BuildAuthorizeUrl();
    }

    /// <summary>生成 PKCE code_verifier（RFC 7636——委托引擎 <see cref="Pkce.CreateVerifier"/>，CSPRNG）。</summary>
    public static string GenerateCodeVerifier() => Pkce.CreateVerifier();

    /// <summary>S256 code_challenge（委托引擎 <see cref="Pkce.CreateS256Challenge"/>）。</summary>
    public static string ComputeCodeChallenge(string verifier) => Pkce.CreateS256Challenge(verifier);

    /// <summary>exchange 结果（id_token 验签通过 + sub）。</summary>
    public sealed record ExchangeResult(OidcIdTokenValidationResult IdToken, string? AccessToken, string? RefreshToken, TimeSpan? ExpiresIn);

    /// <summary>
    /// code→token（**委托引擎**）：POST token 端点（client_secret post——引擎 ClientAuth=QueryParameter）+ redirect_uri
    /// 一致性（RFC 6749 §4.1.3——引擎 <c>IncludeRedirectUriInTokenRequest</c>）+ PKCE code_verifier（UsePkce 时必传）+
    /// id_token 验签（引擎 <see cref="IdTokenDecoder.ValidateAsync"/>——JWKS kid 路由 + RS256 + iss/aud/azp/exp/nbf/sub）。
    /// 返回 id_token 验签结果 + access_token（用户级 token 不缓存——联邦认证面一次性消费；refresh 续票归 L7 YAGNI）。
    /// </summary>
    public async Task<ExchangeResult> ExchangeCodeAsync(
        OidcPlatformConfig config,
        DiscoveryResult? discovery,
        string code,
        string redirectUri,
        string? codeVerifier,
        CancellationToken ct = default)
    {
        var tokenUri = Resolve(config.TokenUri, discovery?.TokenUri, $"token 端点未配置：平台 {config.Platform}");
        var authorizeUri = Resolve(config.AuthorizeUri, discovery?.AuthorizeUri, null) ?? "https://invalid.local/authorize";   // exchange 不使用——占位满足引擎必填
        var userInfoUri = Resolve(config.UserInfoUri, discovery?.UserInfoUri, null);
        var jwksUri = Resolve(config.JwksUri, discovery?.JwksUri, $"JWKS 端点未配置：平台 {config.Platform}");

        // private_key_jwt（企业证书——RFC 7523）：过渡期 + 终态均内联（引擎 ClientAuthenticationScheme 无 PrivateKeyJwt——OIDC 归并方案 P1-8）
        if (!string.IsNullOrWhiteSpace(config.ClientAssertionSigningKeyPath))
            return await ExchangeCodePrivateKeyJwtAsync(config, discovery, tokenUri, code, redirectUri, codeVerifier, ct);

        // PKCE 前置校验（清晰消息——引擎 pkce_verifier_required 通用消息不可读）
        if (config.UsePkce && string.IsNullOrEmpty(codeVerifier))
            throw new InvalidOperationException("PKCE 启用但 code_verifier 缺失（授权时生成、回调兑换必须传回）");

        // 委托引擎：code→token（client_secret post 形态——ClientAuth=QueryParameter 入 form body）
        var engine = new OAuthClient(_httpClient, BuildEngineOptions(config, authorizeUri, tokenUri, userInfoUri, redirectUri));
        var token = await engine.ExchangeCodeAsync(code, codeVerifier, ct);

        // id_token 提取（引擎 token 响应 RawResponse 透传口——id_token 缺失拒绝）
        var idToken = token.RawResponse.ValueKind == JsonValueKind.Object
            && token.RawResponse.TryGetProperty("id_token", out var idEl) && idEl.ValueKind == JsonValueKind.String
            ? idEl.GetString()
            : null;
        if (string.IsNullOrEmpty(idToken))
            throw new InvalidOperationException("OIDC token 响应缺 id_token");

        // ⚠️ 信任根——id_token 验签（引擎 IdTokenDecoder.ValidateAsync——JWKS kid 路由 + RS256 + iss/aud/azp/exp/nbf/sub 强校验）
        var payload = await IdTokenDecoder.ValidateAsync(idToken, BuildValidation(config, jwksUri, discovery), GetJwks(jwksUri), ct);

        return new ExchangeResult(
            ExtractValidationResult(payload),
            token.AccessToken,
            token.RefreshToken,
            token.ExpiresIn is int secs ? TimeSpan.FromSeconds(secs) : null);
    }

    /// <summary>userinfo 拉取（**委托引擎**——标准端点 Bearer；响应裁剪为 <see cref="OidcUserInfo"/>——email 仅展示不参与身份归一）。
    /// 懒加载降级 null 语义保留（M1 契约——引擎失败抛异常，此处收敛为降级）。</summary>
    public async Task<OidcUserInfo?> FetchUserInfoAsync(
        OidcPlatformConfig config,
        DiscoveryResult? discovery,
        string accessToken,
        CancellationToken ct = default)
    {
        var userInfoUri = Resolve(config.UserInfoUri, discovery?.UserInfoUri, null);
        if (string.IsNullOrEmpty(userInfoUri))
            return null;

        var authorizeUri = Resolve(config.AuthorizeUri, discovery?.AuthorizeUri, null) ?? "https://invalid.local/authorize";
        var tokenUri = Resolve(config.TokenUri, discovery?.TokenUri, null) ?? "https://invalid.local/token";
        var engine = new OAuthClient(_httpClient, BuildEngineOptions(config, authorizeUri, tokenUri, userInfoUri, ""));

        OAuthUserInfoResult result;
        try
        {
            result = await engine.FetchUserInfoAsync(
                new OAuthTokenResponse(accessToken, null, null, null, null, null, null, default), ct);
        }
        catch (Exception ex) when (ex is OAuthProtocolException or HttpRequestException or JsonException)
        {
            _logger.LogWarning("OIDC userinfo 获取失败（懒加载降级 null）：{Msg}", ex.Message);
            return null;
        }

        // 引擎结构化字段（sub/email/name/picture）+ 方言字段经 Raw 透传口提取（email_verified/preferred_username）
        var raw = result.Raw;
        var emailVerified = raw.ValueKind == JsonValueKind.Object
            && raw.TryGetProperty("email_verified", out var ev) && ev.ValueKind == JsonValueKind.True;
        var preferredUsername = raw.ValueKind == JsonValueKind.Object
            && raw.TryGetProperty("preferred_username", out var pu) && pu.ValueKind == JsonValueKind.String ? pu.GetString() : null;
        return new OidcUserInfo(result.Subject ?? "", result.Email, emailVerified, result.Name, preferredUsername);
    }

    /// <summary>private_key_jwt 断言（企业证书认证——RFC 7523；按 config.ClientAssertionSigningKeyPath 加载 RSA 私钥签名）。<b>保留内联</b>。</summary>
    private string BuildPrivateKeyJwtAssertion(OidcPlatformConfig config, string tokenUri)
    {
        const string header = "{\"alg\":\"RS256\",\"typ\":\"JWT\"}";
        var now = DateTimeOffset.UtcNow;
        var payload = JsonSerializer.Serialize(new Dictionary<string, object>
        {
            ["iss"] = config.ClientId,
            ["sub"] = config.ClientId,
            ["aud"] = tokenUri,
            ["jti"] = Guid.NewGuid().ToString("N"),
            ["exp"] = now.AddMinutes(5).ToUnixTimeSeconds(),
            ["iat"] = now.ToUnixTimeSeconds(),
        });

        var signingInput = $"{B64(header)}.{B64(payload)}";
        using var rsa = RSA.Create();
        rsa.ImportFromPem(File.ReadAllText(config.ClientAssertionSigningKeyPath!));   // 调用侧已守卫非空（private_key_jwt 分支前置校验）
        var sig = rsa.SignData(Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return $"{signingInput}.{Convert.ToBase64String(sig).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";
    }

    /// <summary>private_key_jwt 通道 code→token 内联（client_assertion 形态——引擎无 PrivateKeyJwt，P1-8）。验签仍委托引擎。</summary>
    private async Task<ExchangeResult> ExchangeCodePrivateKeyJwtAsync(
        OidcPlatformConfig config, DiscoveryResult? discovery, string tokenUri,
        string code, string redirectUri, string? codeVerifier, CancellationToken ct)
    {
        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirectUri,   // RFC 6749 §4.1.3 一致性——服务端比对授权时 redirect_uri
            ["client_id"] = config.ClientId,
            ["client_assertion_type"] = "urn:ietf:params:oauth:client-assertion-type:jwt-bearer",
            ["client_assertion"] = BuildPrivateKeyJwtAssertion(config, tokenUri),
        };

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

        var jwksUri = Resolve(config.JwksUri, discovery?.JwksUri, $"JWKS 端点未配置：平台 {config.Platform}");
        var payload = await IdTokenDecoder.ValidateAsync(idToken, BuildValidation(config, jwksUri, discovery), GetJwks(jwksUri), ct);

        return new ExchangeResult(
            ExtractValidationResult(payload),
            root.TryGetProperty("access_token", out var at) ? at.GetString() : null,
            root.TryGetProperty("refresh_token", out var rt) ? rt.GetString() : null,
            root.TryGetProperty("expires_in", out var ei) && ei.TryGetInt32(out var secs) ? TimeSpan.FromSeconds(secs) : null);
    }

    /// <summary>组装引擎协议配置（平台差异全参数化——ClientAuth=QueryParameter 即 client_secret post 形态）。</summary>
    private static OAuthClientOptions BuildEngineOptions(
        OidcPlatformConfig config, string authorizeUri, string tokenUri, string? userInfoUri, string redirectUri)
        => new(
            AuthorizeEndpoint: new Uri(authorizeUri),
            TokenEndpoint: new Uri(tokenUri),
            ClientId: config.ClientId,
            ClientSecret: config.ClientSecret,
            RedirectUri: string.IsNullOrEmpty(redirectUri) ? null : new Uri(redirectUri),
            TokenMethod: TokenRequestMethod.PostForm,
            ClientAuth: ClientAuthenticationScheme.QueryParameter,   // client_secret post：client_id/client_secret 入 form body
            EnablePkce: config.UsePkce,
            IncludeRedirectUriInTokenRequest: true,   // RFC 6749 §4.1.3 redirect_uri 一致性
            UserInfoAuth: UserInfoAuthenticationScheme.HeaderBearer,
            ScopeParameters: new Dictionary<string, string> { ["scope"] = string.Join(" ", config.Scopes) },
            UserInfoEndpoint: string.IsNullOrEmpty(userInfoUri) ? null : new Uri(userInfoUri));

    /// <summary>
    /// 组装 id_token 验签入参（引擎 <see cref="IdTokenValidationParameters"/>——iss 强校验）。
    /// <para>iss 语义（M1 空列表跳过 → 引擎强校验 P7）：TokenIssuers 白名单优先（通配/正则）；空 + Discovery 提供
    /// issuer → 精确匹配；均无 → fail-fast（明示配置缺失——不静默放行）。</para>
    /// </summary>
    private static IdTokenValidationParameters BuildValidation(OidcPlatformConfig config, string jwksUri, DiscoveryResult? discovery)
    {
        IReadOnlyCollection<string>? tokenIssuers = config.TokenIssuers.Count > 0 ? config.TokenIssuers : null;
        string issuer;
        if (tokenIssuers != null)
            issuer = config.TokenIssuers[0];
        else if (!string.IsNullOrWhiteSpace(discovery?.Issuer))
            issuer = discovery!.Issuer!;
        else
            throw new InvalidOperationException(
                $"OIDC iss 校验配置缺失：平台 {config.Platform} 须配置 TokenIssuers 白名单（或启用 Discovery 提供 issuer）");

        return new IdTokenValidationParameters(
            Issuer: issuer,
            Audiences: [config.ClientId],
            VerificationKeyPem: "",   // JWKS 路径（与 JwksUri 二选一——引擎强校验）
            ExpectedNonce: null,
            AllowedAlgorithms: ["RS256"],   // M1 强制 RS256（引擎 JWKS 路径亦仅 RS256）
            JwksUri: jwksUri,
            TokenIssuers: tokenIssuers,
            AuthorizedParty: config.ClientId,   // aud 数组多方 azp 校验（OIDC Core §3.1.3.3）
            ClockSkew: TimeSpan.FromSeconds(30));   // M1 leeway 30s
    }

    /// <summary>验签密钥源——JwksManager 按 jwksUri 键缓存（L1 缓存 TTL 1h 保留，对齐 M1 设计）。</summary>
    private JwksManager GetJwks(string jwksUri)
        => _jwksCache.GetOrAdd(jwksUri, _ => new JwksManager(_httpClient));

    /// <summary>验签通过 payload → 归一结果（sub 不透明字符串——P12；aud 数组拼接展示）。</summary>
    private static OidcIdTokenValidationResult ExtractValidationResult(JsonElement payload)
    {
        var sub = GetString(payload, "sub") ?? throw new InvalidOperationException("id_token 缺 sub");
        var iss = GetString(payload, "iss") ?? "";
        var aud = payload.TryGetProperty("aud", out var audEl)
            ? audEl.ValueKind == JsonValueKind.String ? audEl.GetString()!
                : audEl.ValueKind == JsonValueKind.Array
                    ? string.Join(",", audEl.EnumerateArray().Where(x => x.ValueKind == JsonValueKind.String).Select(x => x.GetString()))
                    : ""
            : "";
        var azp = GetString(payload, "azp");
        return new OidcIdTokenValidationResult(sub, iss, aud, azp);
    }

    /// <summary>端点解析——显式配置优先 → Discovery 解析覆盖；双空 + missingMessage 非空 → 抛（双空 + null → 空串可选端点）。</summary>
    private static string Resolve(string? explicitValue, string? discoveryValue, string? missingMessage)
    {
        if (!string.IsNullOrWhiteSpace(explicitValue)) return explicitValue;
        if (!string.IsNullOrWhiteSpace(discoveryValue)) return discoveryValue!;
        if (missingMessage != null) throw new InvalidOperationException(missingMessage);
        return "";
    }

    /// <summary>释放缓存 JwksManager（RSA 实例归口——消费方生命周期由 DI 托管 typed client 触发）。</summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var jwks in _jwksCache.Values)
        {
            try { jwks.Dispose(); } catch { /* 忽略释放异常 */ }
        }
        _jwksCache.Clear();
    }

    static string B64(string json) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(json)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    static string? GetString(JsonElement root, string name) =>
        root.TryGetProperty(name, out var el) && el.ValueKind == JsonValueKind.String ? el.GetString() : null;
}
