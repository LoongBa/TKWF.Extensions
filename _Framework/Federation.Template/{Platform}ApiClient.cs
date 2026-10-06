using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Federation.{Namespace};

/// <summary>
/// {平台}出站 API 客户端（平台网关库——凭证从 <see cref="{Platform}Options.Channels"/> 解析，不经
/// Authentication <c>IPlatformCredentialService</c>，Oracle P2-4 凭证自持）。
/// <para>出站能力（对齐 WeChatApiClient 先例形态）：</para>
/// <para>① 令牌获取——L1 缓存（ConcurrentDictionary + SemaphoreSlim 并发锁防 stampede + 提前 5 分钟过期刷新）；
/// ⚠️ 平台差异：应用级 client_credential 令牌缓存（微信 cgi-bin/token）；用户级 authorization_code grant
/// 令牌**不缓存**（QQ /me 换取 openid——N3 Oracle P1-5，联邦认证面一次性消费）；</para>
/// <para>② code→external_uid 一次性（结果不缓存——一次性 code）；</para>
/// <para>③ 用户信息拉取（响应裁剪，缺省降级 null）。</para>
/// <para>HttpClient 经 **typed client** 注入（<c>AddHttpClient&lt;{Platform}ApiClient&gt;</c>——ctor 收
/// <see cref="HttpClient"/> + IOptions + ILogger，对齐 Microsoft.Extensions.Http 惯例；Oracle 评审点 5：
/// 库纯逻辑 + HttpClient，不引 AspNetCore，入站端点归装配层）。</para>
/// <para>零持久化零 Store（tkwf-extension 铁律）——纯内存态令牌缓存。</para>
/// </summary>
public sealed class {Platform}ApiClient
{
    private const string ApiBase = "https://api.{platform}.com"; // ⚠️ 平台差异：API 基址
    private static readonly TimeSpan TokenEarlyRefresh = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IOptions<{Platform}Options> _options;
    private readonly ILogger<{Platform}ApiClient> _logger;

    // L1 缓存：appId → (token, expiresAtUtc)；SemaphoreSlim 并发锁防 stampede（对齐 WeChat 语义）。
    // ⚠️ 平台差异：用户级令牌不缓存（见类注释）——应用级令牌才走缓存
    private readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    /// <summary>HttpClient 经 typed client 注入（AddHttpClient&lt;{Platform}ApiClient&gt;——DI 生命周期托管，避免 HttpClient 悬挂 socket）。</summary>
    public {Platform}ApiClient(HttpClient httpClient, IOptions<{Platform}Options> options, ILogger<{Platform}ApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// 获取访问令牌（L1 缓存 + 并发锁 + 提前 5 分钟过期刷新）。
    /// <para>凭证从 <see cref="{Platform}Options.Channels"/> 按凭证键精确解析（Oracle P2-4 自持）。</para>
    /// ⚠️ 平台差异：令牌端点 URL 与响应结构；用户级令牌不缓存（N3 QQ P1-5）
    /// </summary>
    public async Task<string> GetAccessTokenAsync(string appId, CancellationToken ct = default)
    {
        if (_tokenCache.TryGetValue(appId, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
            return cached.AccessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            // 双检（锁内复查缓存）
            if (_tokenCache.TryGetValue(appId, out cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
                return cached.AccessToken;

            var secret = ResolveCredential(appId);

            // ⚠️ 平台差异：令牌端点 URL（对齐 WeChat cgi-bin/token 形态）
            var url = $"{ApiBase}/token?grant_type=client_credential&appid={Uri.EscapeDataString(secret.AppId)}&secret={Uri.EscapeDataString(secret.AppSecret)}";
            using var resp = await _httpClient.GetAsync(url, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
                throw new InvalidOperationException($"{PlatformName} 令牌获取失败：{json}");

            var token = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            _tokenCache[appId] = new CachedToken(token, DateTime.UtcNow.AddSeconds(expiresIn).Subtract(TokenEarlyRefresh));
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// 用授权 code 换 external_uid（一次性——结果不缓存）。
    /// ⚠️ 平台差异：QQ 经 code→access_token→/me→openid 一次性链（用户级令牌不缓存 P1-5）；
    /// redirect_uri 一致性校验（OAuth RFC 6749 §4.1.3——code 换 token 时须与授权时一致，防 code 窃取换 token）
    /// </summary>
    public async Task<string> GetExternalUserIdAsync(string appId, string code, CancellationToken ct = default)
    {
        var secret = ResolveCredential(appId);

        // ⚠️ 平台差异：code→external_uid 端点 URL 与响应结构（对齐 WeChat sns/oauth2/access_token 形态）
        var url = $"{ApiBase}/oauth2/token?appid={Uri.EscapeDataString(secret.AppId)}&secret={Uri.EscapeDataString(secret.AppSecret)}&code={Uri.EscapeDataString(code)}&grant_type=authorization_code";
        using var resp = await _httpClient.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
            throw new InvalidOperationException($"{PlatformName} code 换取身份失败：{json}");
        return root.GetProperty("openid").GetString()!; // ⚠️ 平台差异：用户标识字段（openid/user_id/userid）
    }

    /// <summary>
    /// 拉取用户信息（响应裁剪，缺省降级 null）。
    /// ⚠️ 平台差异：用户信息端点 URL 与响应结构（QQ get_user_info 无手机号——N3 方案 §3.2）
    /// </summary>
    public async Task<{Platform}UserInfo?> GetUserInfoAsync(string accessToken, string externalUserId, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/user/info?access_token={Uri.EscapeDataString(accessToken)}&openid={Uri.EscapeDataString(externalUserId)}";
        using var resp = await _httpClient.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
        {
            _logger.LogWarning("{PlatformName} 用户信息获取失败：{Json}（降级返回 null）", PlatformName, json);
            return null;
        }
        return new {Platform}UserInfo(
            root.GetProperty("openid").GetString()!,
            root.TryGetProperty("nickname", out var nick) ? nick.GetString() : null,
            root.TryGetProperty("avatar", out var av) ? av.GetString() : null);
    }

    /// <summary>凭证解析——从 <see cref="{Platform}Options.Channels"/> 按凭证键精确匹配（Oracle P2-4：凭证自持，不经 Authentication）。</summary>
    private {Platform}ChannelConfig ResolveCredential(string appId)
    {
        var channels = _options.Value.Channels;
        if (channels == null || channels.Count == 0)
            throw new InvalidOperationException($"{PlatformName} 凭证未配置：TKWF:Federation:{Namespace} 节 Channels 为空");

        foreach (var channel in channels)
        {
            if (string.Equals(channel.AppId, appId, StringComparison.Ordinal))
                return channel;
        }

        throw new InvalidOperationException($"{PlatformName} 凭证未配置：appId={appId}（Channels 无匹配项）");
    }

    private static string PlatformName => "{Namespace}";

    private sealed record CachedToken(string AccessToken, DateTime ExpiresAtUtc);
}
