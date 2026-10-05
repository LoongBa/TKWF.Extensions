using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信公众平台出站 API 客户端（平台网关库——凭证从 <see cref="WeChatOptions.Channels"/> 解析，不经
/// Authentication <c>IPlatformCredentialService</c>，Oracle P2-4 凭证自持）。
/// <para>出站能力（对齐 Authentication 既有 WeChatApiClient 模式）：</para>
/// <para>① <c>GetAccessTokenAsync</c>——<c>cgi-bin/token</c> access_token L1 缓存
/// （ConcurrentDictionary + SemaphoreSlim 并发锁防 stampede + 提前 5 分钟过期刷新）；</para>
/// <para>② <c>GetOpenIdAsync</c>——<c>sns/oauth2/access_token</c> 网页授权 code 换 openid
/// （AppSecret 服务端持有，结果不缓存——一次性 code）；</para>
/// <para>③ <c>GetUserInfoAsync</c>——<c>sns/userinfo</c> 拉取用户信息（响应裁剪，snsapi_base 场景降级 null）。</para>
/// <para>HttpClient 经 **typed client** 注入（<c>AddHttpClient&lt;WeChatApiClient&gt;</c>——ctor 收
/// <see cref="HttpClient"/> + IOptions + ILogger，对齐 Microsoft.Extensions.Http 惯例；Oracle 评审点 5：
/// 库纯逻辑 + HttpClient，不引 AspNetCore，入站端点归装配层）。</para>
/// <para>零持久化零 Store（tkwf-extension 铁律）——纯内存态 access_token 缓存。</para>
/// </summary>
public sealed class WeChatApiClient
{
    private const string ApiBase = "https://api.weixin.qq.com";
    private static readonly TimeSpan TokenEarlyRefresh = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IOptions<WeChatOptions> _options;
    private readonly ILogger<WeChatApiClient> _logger;

    // L1 缓存：appId → (accessToken, expiresAtUtc)；SemaphoreSlim 并发锁防 stampede（对齐 Authentication 语义）
    private readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    /// <summary>HttpClient 经 typed client 注入（AddHttpClient&lt;WeChatApiClient&gt;——DI 生命周期托管，避免 HttpClient 悬挂 socket）。</summary>
    public WeChatApiClient(HttpClient httpClient, IOptions<WeChatOptions> options, ILogger<WeChatApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// 获取 access_token（<c>cgi-bin/token</c>——L1 缓存 + 并发锁 + 提前 5 分钟过期刷新）。
    /// <para>凭证从 <see cref="WeChatOptions.Channels"/> 按 <paramref name="appId"/> 精确解析（Oracle P2-4 自持）。
    /// <paramref name="appType"/> 保留签名对齐 Authentication 既有 <c>IWeChatApiClient</c>——本库渠道均为公众号
    /// mp 形态（配置节 <c>TKWF:Federation:WeChat</c>），解析不依赖 appType。</para>
    /// </summary>
    public async Task<string> GetAccessTokenAsync(string appId, string appType, CancellationToken ct = default)
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

            // cgi-bin/token?grant_type=client_credential&appid=&secret=
            var url = $"{ApiBase}/cgi-bin/token?grant_type=client_credential&appid={Uri.EscapeDataString(secret.AppId)}&secret={Uri.EscapeDataString(secret.AppSecret)}";
            using var resp = await _httpClient.GetAsync(url, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
                throw new InvalidOperationException($"微信 access_token 获取失败：{json}");

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
    /// 用微信授权 code 换 openid（<c>sns/oauth2/access_token</c>——按发起授权绑定的 AppId 精确解析凭证，
    /// 微信 code 与 AppId 绑定；结果不缓存——一次性 code）。
    /// </summary>
    public async Task<string> GetOpenIdAsync(string appId, string code, CancellationToken ct = default)
    {
        var secret = ResolveCredential(appId);

        // sns/oauth2/access_token?appid=&secret=&code=&grant_type=authorization_code（网页授权 code 换 openid）
        var url = $"{ApiBase}/sns/oauth2/access_token?appid={Uri.EscapeDataString(secret.AppId)}&secret={Uri.EscapeDataString(secret.AppSecret)}&code={Uri.EscapeDataString(code)}&grant_type=authorization_code";
        using var resp = await _httpClient.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
            throw new InvalidOperationException($"微信授权 code 换取 openid 失败：{json}");
        return root.GetProperty("openid").GetString()!;
    }

    /// <summary>
    /// 拉取用户信息（<c>sns/userinfo</c>——需 snsapi_userinfo 授权；snsapi_base 仅 openid 无用户信息，降级返回 null）。
    /// </summary>
    public async Task<WeChatUserInfo?> GetUserInfoAsync(string accessToken, string openid, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/sns/userinfo?access_token={Uri.EscapeDataString(accessToken)}&openid={Uri.EscapeDataString(openid)}&lang=zh_CN";
        using var resp = await _httpClient.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
        {
            _logger.LogWarning("微信 sns/userinfo 获取失败：{Json}（snsapi_base 场景无用户信息——返回 null 降级）", json);
            return null;
        }
        return new WeChatUserInfo(
            root.GetProperty("openid").GetString()!,
            root.TryGetProperty("nickname", out var nick) ? nick.GetString() : null,
            root.TryGetProperty("headimgurl", out var av) ? av.GetString() : null,
            root.TryGetProperty("unionid", out var uni) ? uni.GetString() : null);
    }

    /// <summary>凭证解析——从 <see cref="WeChatOptions.Channels"/> 按 AppId 精确匹配（Oracle P2-4：凭证自持，不经 Authentication）。</summary>
    private WeChatChannelConfig ResolveCredential(string appId)
    {
        var channels = _options.Value.Channels;
        if (channels == null || channels.Count == 0)
            throw new InvalidOperationException("微信凭证未配置：TKWF:Federation:WeChat 节 Channels 为空");

        foreach (var channel in channels)
        {
            if (string.Equals(channel.AppId, appId, StringComparison.Ordinal))
                return channel;
        }

        throw new InvalidOperationException($"微信凭证未配置：appId={appId}（Channels 无匹配项）");
    }

    private sealed record CachedToken(string AccessToken, DateTime ExpiresAtUtc);
}
