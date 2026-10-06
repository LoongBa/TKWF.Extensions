using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Federation.WeCom;

/// <summary>
/// 企业微信（WeCom）出站 API 客户端（平台网关库——凭证由 channel 传入，本客户端纯出站无凭证解析）。
/// <para>出站能力（M4 方案 §三 T2——双授权流 + 无独立 token 端点）：</para>
/// <para>① <c>GetAccessTokenAsync</c>——<c>cgi-bin/gettoken</c> 企业 access_token
/// （L1 缓存 + SemaphoreSlim 并发锁防 stampede + 提前 5 分钟过期刷新；<b>缓存键 = corpid + ":" + corpsecret 用途
/// 标识——应用级 vs 通讯录级 token 互不覆盖，Oracle 评审 P1-2</b>）；</para>
/// <para>② <c>GetIdentityAsync</c>——<c>cgi-bin/auth/getuserinfo</c>（自建）/ <c>cgi-bin/auth/getuserinfo3rd</c>
/// （三方）code→身份，<b>一次性不缓存</b>（无独立 token 端点——code 5min 一次性）；<b>按应用类型 + 成员状态解析
/// external_uid 双策略（Oracle 评审 P0-1）</b>：三方成员 open_userid（全局唯一）/ 自建成员 {CorpId}:{userid} 复合
/// / 非成员 openid；</para>
/// <para>③ <c>GetSensitiveInfoAsync</c>——<c>cgi-bin/auth/getuserdetail</c> user_ticket 换敏感信息
/// （<b>即用即弃不落库</b>——P1-4 数据流闭环，1800s 票据；扫码 98177 不返回 user_ticket 降级仅身份）。</para>
/// <para>HttpClient 经 typed client 注入（<c>AddHttpClient&lt;WeComApiClient&gt;</c>——ctor 收
/// <see cref="HttpClient"/> + ILogger；Oracle 评审点 5：库纯逻辑 + HttpClient，不引 AspNetCore）。</para>
/// <para>零持久化零 Store（tkwf-extension 铁律）——纯内存态 access_token 缓存。</para>
/// </summary>
public sealed class WeComApiClient
{
    private const string ApiBase = "https://qyapi.weixin.qq.com";
    private static readonly TimeSpan TokenEarlyRefresh = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly ILogger<WeComApiClient> _logger;

    // L1 缓存：corpid:corpsecret → (accessToken, expiresAtUtc)；SemaphoreSlim 并发锁防 stampede
    // （Oracle 评审 P1-2：缓存键含 corpsecret 用途——应用级 vs 通讯录级 token 互不覆盖）
    private readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    /// <summary>HttpClient 经 typed client 注入（AddHttpClient&lt;WeComApiClient&gt;——DI 生命周期托管，避免 HttpClient 悬挂 socket）。</summary>
    public WeComApiClient(HttpClient httpClient, ILogger<WeComApiClient> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// 获取企业 access_token（<c>cgi-bin/gettoken</c>——L1 缓存 + 并发锁 + 提前 5 分钟过期刷新）。
    /// <para><b>缓存键 = corpid + ":" + corpsecret</b>（Oracle 评审 P1-2）——应用级 vs 通讯录级 token 互不覆盖；
    /// 通讯录回调验签只需 Token+EncodingAESKey（不经本方法）——验签无 access_token 依赖。</para>
    /// </summary>
    public async Task<string> GetAccessTokenAsync(string corpId, string corpSecret, CancellationToken ct = default)
    {
        var cacheKey = $"{corpId}:{corpSecret}";
        if (_tokenCache.TryGetValue(cacheKey, out var cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
            return cached.AccessToken;

        await _tokenLock.WaitAsync(ct);
        try
        {
            // 双检（锁内复查缓存）
            if (_tokenCache.TryGetValue(cacheKey, out cached) && cached.ExpiresAtUtc > DateTime.UtcNow)
                return cached.AccessToken;

            // GET /cgi-bin/gettoken?corpid=&corpsecret=
            var url = $"{ApiBase}/cgi-bin/gettoken?corpid={Uri.EscapeDataString(corpId)}&corpsecret={Uri.EscapeDataString(corpSecret)}";
            using var resp = await _httpClient.GetAsync(url, ct);
            var json = await resp.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
                throw new InvalidOperationException($"企业微信 access_token 获取失败：{json}");

            var token = root.GetProperty("access_token").GetString()!;
            var expiresIn = root.GetProperty("expires_in").GetInt32();
            _tokenCache[cacheKey] = new CachedToken(token, DateTime.UtcNow.AddSeconds(expiresIn).Subtract(TokenEarlyRefresh));
            return token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    /// <summary>
    /// 授权 code 直接换身份（<c>getuserinfo</c> 自建 / <c>getuserinfo3rd</c> 三方——<b>无独立 token 端点</b>，
    /// code 5min 一次性；域名须完全匹配可信域名否则 50001）。
    /// <para><b>external_uid 双策略（Oracle 评审 P0-1）</b>——按应用类型 + 成员状态解析返回：</para>
    /// <para>① 三方应用企业成员 → <see cref="WeComUserInfo.OpenUserid"/>（体系内明示<b>全局唯一</b>——同服务商跨应用
    /// 相同，最佳稳定键，直接作映射主键，完全规避 userid 复用风险）；</para>
    /// <para>② 自建应用企业成员 → <see cref="WeComUserInfo.UserId"/>（须 {CorpId}:{userid} 复合消歧——互联企业）；
    /// 非企业成员 → <see cref="WeComUserInfo.OpenId"/>（对当前企业唯一）。</para>
    /// <para>响应含 <c>UserTicket</c>（snsapi_privateinfo，自建 96442 + 三方 98179 返回；扫码 98177 不返回——P1-4）
    /// 时条件性透出——供装配层按需换取敏感信息。</para>
    /// </summary>
    public async Task<WeComUserInfo> GetIdentityAsync(string corpId, string corpSecret, string code, bool isThirdParty, CancellationToken ct = default)
    {
        var accessToken = await GetAccessTokenAsync(corpId, corpSecret, ct);

        // 自建 /cgi-bin/auth/getuserinfo；三方 /cgi-bin/auth/getuserinfo3rd（Oracle 评审 P0-1：双端点确证）
        var endpoint = isThirdParty ? "/cgi-bin/auth/getuserinfo3rd" : "/cgi-bin/auth/getuserinfo";
        var url = $"{ApiBase}{endpoint}?access_token={Uri.EscapeDataString(accessToken)}&code={Uri.EscapeDataString(code)}";
        using var resp = await _httpClient.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
            throw new InvalidOperationException($"企业微信授权 code 换取身份失败：{json}");

        return new WeComUserInfo(
            root.TryGetProperty("UserId", out var uid) ? uid.GetString()
                : (root.TryGetProperty("Userid", out var uid3) ? uid3.GetString() : null),  // 自建 UserId / 三方 Userid
            root.TryGetProperty("OpenId", out var oid) ? oid.GetString() : null,
            root.TryGetProperty("OpenUserID", out var oui) ? oui.GetString() : null,
            root.TryGetProperty("ExternalUserid", out var eui) ? eui.GetString() : null,
            root.TryGetProperty("UserTicket", out var ut) ? ut.GetString() : null);
    }

    /// <summary>
    /// user_ticket 换敏感信息（<c>cgi-bin/auth/getuserdetail</c>——<b>即用即弃不落库</b>，Oracle 评审 P1-4 数据流闭环：
    /// 换取 → <see cref="WeComSensitiveInfo"/> DTO（敏感标注）→ 编排层帧内消费做绑定 → 帧结束随 GC 回收即弃）。
    /// <para>⚠️ 敏感字段（Name/Mobile/Email）不得写日志/审计/持久化（P2-3 标注）；票据 1800s 一次性，本方法不缓存。</para>
    /// </summary>
    public async Task<WeComSensitiveInfo> GetSensitiveInfoAsync(string userTicket, CancellationToken ct = default)
    {
        // POST /cgi-bin/auth/getuserdetail?user_ticket=（snsapi_privateinfo 增强）
        var url = $"{ApiBase}/cgi-bin/auth/getuserdetail?user_ticket={Uri.EscapeDataString(userTicket)}";
        using var resp = await _httpClient.PostAsync(url, null, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
            throw new InvalidOperationException($"企业微信敏感信息获取失败：{json}");

        return new WeComSensitiveInfo(
            root.TryGetProperty("userid", out var uid) ? uid.GetString() : null,
            root.TryGetProperty("name", out var name) ? name.GetString() : null,
            root.TryGetProperty("mobile", out var mobile) ? mobile.GetString() : null,
            root.TryGetProperty("email", out var email) ? email.GetString() : null,
            root.TryGetProperty("avatar", out var avatar) ? avatar.GetString() : null,
            root.TryGetProperty("gender", out var gender) ? gender.GetInt32() : 0);
    }

    private sealed record CachedToken(string AccessToken, DateTime ExpiresAtUtc);
}
