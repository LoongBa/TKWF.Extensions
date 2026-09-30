using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 微信 API 客户端——公众号网页授权（snsapi_base）/ 开放平台扫码（snsapi_login）双形态。
/// <para>方案 §5.10——凭证经 <see cref="IPlatformCredentialService"/> 解析（不直注入裸 DB）；</para>
/// <para>access_token L1 缓存（ConcurrentDictionary + SemaphoreSlim 并发锁 + 提前 5 分钟过期刷新）；
/// 网页授权 code 换 openid 走 sns/oauth2/access_token（AppSecret 服务端持有，结果不缓存——一次性 code）。</para>
/// </summary>
internal sealed class WeChatApiClient : IWeChatApiClient
{
    private const string ApiBase = "https://api.weixin.qq.com";
    private static readonly TimeSpan TokenEarlyRefresh = TimeSpan.FromMinutes(5);

    private readonly IPlatformCredentialService _credentials;
    private readonly ILogger<WeChatApiClient> _logger;

    // L1 缓存：appId → (accessToken, expiresAtUtc)；SemaphoreSlim 并发锁防 stampede（对齐 DMP 语义）
    private readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    public WeChatApiClient(IPlatformCredentialService credentials, ILogger<WeChatApiClient> logger)
    {
        _credentials = credentials;
        _logger = logger;
    }

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

            var secret = await _credentials.GetSecretAsync(AuthTypes.Wechat, appType, ct);
            if (secret == null) throw new InvalidOperationException($"微信凭证未配置：platform=wechat appType={appType} appId={appId}");

            // cgi-bin/token?grant_type=client_credential&appid=&secret=
            using var client = CreateClient();
            var url = $"{ApiBase}/cgi-bin/token?grant_type=client_credential&appid={Uri.EscapeDataString(secret.AppId)}&secret={Uri.EscapeDataString(secret.AppSecret)}";
            using var resp = await client.GetAsync(url, ct);
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

    public async Task<string> GetOpenIdAsync(string appId, string code, CancellationToken ct = default)
    {
        // Oracle M3：按发起授权绑定的 AppId 精确定位凭证（微信 code 与 AppId 绑定——通配/顺序 fallback 在多应用/双形态
        // （公众号 mp + 扫码 web）并存时会用错凭证致 40029；appId 参数必须参与解析）。
        var secret = await _credentials.GetSecretByAppIdAsync(AuthTypes.Wechat, appId, ct)
            ?? throw new InvalidOperationException($"微信凭证未配置：platform=wechat appId={appId}");

        // sns/oauth2/access_token?appid=&secret=&code=&grant_type=authorization_code（网页授权 code 换 openid——结果不缓存）
        using var client = CreateClient();
        var url = $"{ApiBase}/sns/oauth2/access_token?appid={Uri.EscapeDataString(secret.AppId)}&secret={Uri.EscapeDataString(secret.AppSecret)}&code={Uri.EscapeDataString(code)}&grant_type=authorization_code";
        using var resp = await client.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
            throw new InvalidOperationException($"微信授权 code 换取 openid 失败：{json}");
        return root.GetProperty("openid").GetString()!;
    }

    public async Task<WeChatUserInfo?> GetUserInfoAsync(string accessToken, string openid, CancellationToken ct = default)
    {
        // sns/userinfo?access_token=&openid=（需 snsapi_userinfo 授权；snsapi_base 仅 openid 无用户信息）
        using var client = CreateClient();
        var url = $"{ApiBase}/sns/userinfo?access_token={Uri.EscapeDataString(accessToken)}&openid={Uri.EscapeDataString(openid)}&lang=zh_CN";
        using var resp = await client.GetAsync(url, ct);
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

    private static HttpClient CreateClient() => new();

    private sealed record CachedToken(string AccessToken, DateTime ExpiresAtUtc);
}
