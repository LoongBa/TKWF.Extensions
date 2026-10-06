using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Federation.DingTalk;

/// <summary>
/// 钉钉开放平台出站 API 客户端（平台网关库——凭证从 <see cref="DingTalkOptions.Channels"/> 解析，不经
/// Authentication <c>IPlatformCredentialService</c>，Oracle P2-4 凭证自持）。
/// <para>出站能力（M3 钉钉方案 §三 T2——钉钉新 OAuth2 协议）：</para>
/// <para>① <c>GetUserAccessTokenAsync</c>——<c>/v1.0/oauth2/userAccessToken</c> 授权 code 换用户级
/// accessToken（7200s）+ openId/unionId/corpId；<b>一次性交换不缓存</b>（用户级 authorization_code grant，
/// 联邦认证面一次性消费——rationale 对齐 N3 QQ P1-5；refresh_token 续票归 L7 YAGNI——Oracle 评审 P2-8）；</para>
/// <para>② <c>GetLegacyAccessTokenAsync</c>——<c>gettoken</c>（旧版 <c>oapi.dingtalk.com</c>）企业级 token
/// （L1 缓存 + SemaphoreSlim 并发锁防 stampede + 提前 5 分钟过期刷新）——<b>供 getbyunionid（旧版 topapi）</b>；</para>
/// <para>③ <c>GetV1AccessTokenAsync</c>——<c>/v1.0/oauth2/accessToken</c>（新版 <c>api.dingtalk.com</c>）企业级
/// token（同样 L1 缓存 + 并发锁 + 提前刷新）——<b>供 /contact/users/{unionId}（新版 v1.0 contact API）</b>；
/// 两 token 域不同（新旧协议混用为钉钉平台限制——Oracle 评审 P2-4 注记），各自独立缓存；</para>
/// <para>④ <c>GetUserIdAsync</c>——<c>topapi/user/getbyunionid</c> unionId→userid（企业维度员工 id，contact_type
/// 0=内部/1=外部）——<b>供装配层调用，非 channel AuthenticateAsync 路径</b>（Oracle 评审 P1-4 两步流）；</para>
/// <para>⑤ <c>GetUserInfoAsync</c>——<c>/v1.0/contact/users/{unionId}</c> 用户信息裁剪（nick/avatarUrl/mobile
/// 三方脱敏）。</para>
/// <para>企业级 token 凭证（Oracle 评审 P1-5）：钉钉新模式复用 OAuth 凭证——<see cref="DingTalkChannelConfig.AppKey"/>
/// /<see cref="DingTalkChannelConfig.AppSecret"/> 即新旧双协议企业 token 凭证，无独立 enterprise secret。</para>
/// <para>HttpClient 经 typed client 注入（<c>AddHttpClient&lt;DingTalkApiClient&gt;</c>——ctor 收
/// <see cref="HttpClient"/> + IOptions + ILogger；Oracle 评审点 5：库纯逻辑 + HttpClient，不引 AspNetCore）。</para>
/// <para>零持久化零 Store（tkwf-extension 铁律）——纯内存态 token 缓存。</para>
/// </summary>
public sealed class DingTalkApiClient
{
    private const string LegacyApiBase = "https://oapi.dingtalk.com";
    private const string V1ApiBase = "https://api.dingtalk.com";
    private static readonly TimeSpan TokenEarlyRefresh = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IOptions<DingTalkOptions> _options;
    private readonly ILogger<DingTalkApiClient> _logger;

    // 企业级 token L1 缓存：appKey → (token, expiresAtUtc)；新旧协议域各自独立缓存 + 并发锁防 stampede
    private readonly ConcurrentDictionary<string, CachedToken> _legacyTokenCache = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, CachedToken> _v1TokenCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _legacyTokenLock = new(1, 1);
    private readonly SemaphoreSlim _v1TokenLock = new(1, 1);

    /// <summary>HttpClient 经 typed client 注入（AddHttpClient&lt;DingTalkApiClient&gt;——DI 生命周期托管，避免 HttpClient 悬挂 socket）。</summary>
    public DingTalkApiClient(HttpClient httpClient, IOptions<DingTalkOptions> options, ILogger<DingTalkApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// 授权 code 换用户级 accessToken（<c>/v1.0/oauth2/userAccessToken</c>——钉钉新 OAuth2）。
    /// <para>响应含 accessToken(7200s)/refreshToken(30 天)/corpId/openId/unionId；<b>一次性交换不缓存</b>——
    /// 用户级 authorization_code grant，联邦认证面一次性消费（rationale 对齐 N3 QQ P1-5）。</para>
    /// </summary>
    public async Task<DingTalkTokenResult> GetUserAccessTokenAsync(string appKey, string code, CancellationToken ct = default)
    {
        var secret = ResolveCredential(appKey);

        // POST /v1.0/oauth2/userAccessToken——钉钉 token 端点不接受 redirect_uri（Oracle 评审 P1-3：redirect_uri 仅在 authorize 回调用）
        var url = $"{V1ApiBase}/v1.0/oauth2/userAccessToken";
        using var resp = await _httpClient.PostAsJsonAsync(url, new
        {
            clientId = secret.AppKey,
            clientSecret = secret.AppSecret,
            code,
            grantType = "authorization_code",
        }, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"钉钉 userAccessToken 获取失败（{(int)resp.StatusCode}）：{json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (!root.TryGetProperty("accessToken", out var tokenProp) || string.IsNullOrEmpty(tokenProp.GetString()))
            throw new InvalidOperationException($"钉钉 userAccessToken 响应非法（缺 accessToken）：{json}");

        return new DingTalkTokenResult(
            tokenProp.GetString()!,
            root.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null,
            root.TryGetProperty("expireIn", out var exp) ? exp.GetInt32() : 7200,
            root.TryGetProperty("corpId", out var corp) ? corp.GetString() : null,
            root.TryGetProperty("openId", out var oid) ? oid.GetString() : null,
            root.TryGetProperty("unionId", out var uid) ? uid.GetString() : null);
    }

    /// <summary>
    /// 旧版企业级 access_token（<c>gettoken</c>——L1 缓存 + 并发锁 + 提前 5 分钟过期刷新）。
    /// <para>供旧版 topapi（getbyunionid——钉钉新协议未提供替代端点，新旧 API 混用为平台限制——Oracle 评审 P2-4）。</para>
    /// </summary>
    public async Task<string> GetLegacyAccessTokenAsync(string appKey, CancellationToken ct = default)
    {
        if (_legacyTokenCache.TryGetValue(appKey, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
            return cached.AccessToken;

        await _legacyTokenLock.WaitAsync(ct);
        try
        {
            // 双检（锁内复查缓存）
            if (_legacyTokenCache.TryGetValue(appKey, out cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
                return cached.AccessToken;

            var secret = ResolveCredential(appKey);

            // GET /gettoken?appkey=&appsecret=（旧版企业 token）
            var url = $"{LegacyApiBase}/gettoken?appkey={Uri.EscapeDataString(secret.AppKey)}&appsecret={Uri.EscapeDataString(secret.AppSecret)}";
            using var resp = await _httpClient.GetAsync(url, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
                throw new InvalidOperationException($"钉钉旧版企业 token 获取失败：{json}");

            var token = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            _legacyTokenCache[appKey] = new CachedToken(token, DateTime.UtcNow.AddSeconds(expiresIn).Subtract(TokenEarlyRefresh));
            return token;
        }
        finally
        {
            _legacyTokenLock.Release();
        }
    }

    /// <summary>
    /// 新版企业级 access_token（<c>/v1.0/oauth2/accessToken</c>——L1 缓存 + 并发锁 + 提前 5 分钟过期刷新）。
    /// <para>供新版 v1.0 API（/contact/users/{unionId}——header <c>x-acs-dingtalk-access-token</c>）。</para>
    /// </summary>
    public async Task<string> GetV1AccessTokenAsync(string appKey, CancellationToken ct = default)
    {
        if (_v1TokenCache.TryGetValue(appKey, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
            return cached.AccessToken;

        await _v1TokenLock.WaitAsync(ct);
        try
        {
            // 双检（锁内复查缓存）
            if (_v1TokenCache.TryGetValue(appKey, out cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
                return cached.AccessToken;

            var secret = ResolveCredential(appKey);

            // POST /v1.0/oauth2/accessToken（新版企业 token——钉钉新模式复用 OAuth 凭证，无独立 enterprise secret——P1-5）
            var url = $"{V1ApiBase}/v1.0/oauth2/accessToken";
            using var resp = await _httpClient.PostAsJsonAsync(url, new
            {
                appKey = secret.AppKey,
                appSecret = secret.AppSecret,
            }, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            if (!resp.IsSuccessStatusCode)
                throw new InvalidOperationException($"钉钉新版企业 token 获取失败（{(int)resp.StatusCode}）：{json}");

            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (!root.TryGetProperty("accessToken", out var tokenProp) || string.IsNullOrEmpty(tokenProp.GetString()))
                throw new InvalidOperationException($"钉钉新版企业 token 响应非法（缺 accessToken）：{json}");

            var token = tokenProp.GetString()!;
            var expiresIn = root.TryGetProperty("expireIn", out var exp) ? exp.GetInt32() : 7200;
            _v1TokenCache[appKey] = new CachedToken(token, DateTime.UtcNow.AddSeconds(expiresIn).Subtract(TokenEarlyRefresh));
            return token;
        }
        finally
        {
            _v1TokenLock.Release();
        }
    }

    /// <summary>
    /// unionId → userid（<c>topapi/user/getbyunionid</c>——旧版 topapi，企业维度员工 id）。
    /// <para><b>供装配层按需调用，非 channel AuthenticateAsync 路径</b>（Oracle 评审 P1-4 两步流——channel 只返回
    /// <c>SsoChannelAuthResult(ExternalUserId=openId)</c>，userid 经本方法 + <see cref="DingTalkUserInfo"/> 承载）。</para>
    /// </summary>
    public async Task<DingTalkUserInfo> GetUserIdAsync(string unionId, string appKey, CancellationToken ct = default)
    {
        var legacyToken = await GetLegacyAccessTokenAsync(appKey, ct);

        // POST /topapi/user/getbyunionid?access_token= + body {"unionid"}
        var url = $"{LegacyApiBase}/topapi/user/getbyunionid?access_token={Uri.EscapeDataString(legacyToken)}";
        using var resp = await _httpClient.PostAsJsonAsync(url, new { unionid = unionId }, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
            throw new InvalidOperationException($"钉钉 getbyunionid 失败：{json}");
        if (!root.TryGetProperty("result", out var result))
            throw new InvalidOperationException($"钉钉 getbyunionid 响应非法（缺 result）：{json}");

        return new DingTalkUserInfo(
            null,
            unionId,
            result.TryGetProperty("userid", out var uid) ? uid.GetString() : null,
            null, null, null);
    }

    /// <summary>
    /// 拉取用户信息（<c>/v1.0/contact/users/{unionId}</c>——新版 v1.0 API，header
    /// <c>x-acs-dingtalk-access-token</c> 新版企业 token）。mobile 三方脱敏（完整号须申请权限）。
    /// </summary>
    public async Task<DingTalkUserInfo> GetUserInfoAsync(string unionId, string appKey, CancellationToken ct = default)
    {
        var v1Token = await GetV1AccessTokenAsync(appKey, ct);

        // GET /v1.0/contact/users/{unionId} + header x-acs-dingtalk-access-token
        var url = $"{V1ApiBase}/v1.0/contact/users/{Uri.EscapeDataString(unionId)}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        req.Headers.TryAddWithoutValidation("x-acs-dingtalk-access-token", v1Token);
        using var resp = await _httpClient.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new InvalidOperationException($"钉钉 /contact/users 获取失败（{(int)resp.StatusCode}）：{json}");

        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        return new DingTalkUserInfo(
            root.TryGetProperty("openId", out var oid) ? oid.GetString() : null,
            root.TryGetProperty("unionId", out var uid) ? uid.GetString() : unionId,
            null,
            root.TryGetProperty("nick", out var nick) ? nick.GetString() : null,
            root.TryGetProperty("avatarUrl", out var av) ? av.GetString() : null,
            root.TryGetProperty("mobile", out var mobile) ? mobile.GetString() : null);
    }

    /// <summary>凭证解析——从 <see cref="DingTalkOptions.Channels"/> 按 AppKey 精确匹配（Oracle P2-4：凭证自持，不经 Authentication）。</summary>
    private DingTalkChannelConfig ResolveCredential(string appKey)
    {
        var channels = _options.Value.Channels;
        if (channels == null || channels.Count == 0)
            throw new InvalidOperationException("钉钉凭证未配置：TKWF:Federation:DingTalk 节 Channels 为空");

        foreach (var channel in channels)
        {
            if (string.Equals(channel.AppKey, appKey, StringComparison.Ordinal))
                return channel;
        }

        throw new InvalidOperationException($"钉钉凭证未配置：appKey={appKey}（Channels 无匹配项）");
    }

    private sealed record CachedToken(string AccessToken, DateTime ExpiresAtUtc);
}

/// <summary>
/// 钉钉 userAccessToken 响应裁剪（<c>/v1.0/oauth2/userAccessToken</c>——新 OAuth2 用户级 token）。
/// </summary>
/// <param name="AccessToken">用户级 accessToken（7200s——一次性交换不缓存）。</param>
/// <param name="RefreshToken">refreshToken（30 天——续票归 L7 YAGNI，本库不消费）。</param>
/// <param name="ExpireIn">accessToken 有效秒数。</param>
/// <param name="CorpId">企业 CorpId（scope=openid corpid 时返回——getbyunionid 企业上下文）。</param>
/// <param name="OpenId">openId（应用维度——external_uid 恒 = openId）。</param>
/// <param name="UnionId">unionId（开发者企业账号维度——联盟锚点辅助）。</param>
public sealed record DingTalkTokenResult(
    string AccessToken,
    string? RefreshToken,
    int ExpireIn,
    string? CorpId,
    string? OpenId,
    string? UnionId);
