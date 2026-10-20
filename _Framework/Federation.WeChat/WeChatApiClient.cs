using System;
using System.Collections.Concurrent;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信公众平台出站 API 客户端（平台网关库——凭证经 <see cref="IWeChatCredentialResolver"/> 可插拔契约解析，
/// V0.4.1 自 <see cref="WeChatOptions.Channels"/> 直读迁移；Oracle P2-4 凭证自持语义保持，不经
/// Authentication <c>IPlatformCredentialService</c>）。
/// <para>出站能力（对齐 Authentication 既有 WeChatApiClient 模式）：</para>
/// <para>① <c>GetAccessTokenAsync</c>——<c>cgi-bin/token</c> access_token L1 缓存
/// （ConcurrentDictionary + SemaphoreSlim 并发锁防 stampede + 提前 5 分钟过期刷新）；</para>
/// <para>② <c>GetOpenIdAsync</c>——<c>sns/oauth2/access_token</c> 网页授权 code 换 openid
/// （AppSecret 服务端持有，结果不缓存——一次性 code）；</para>
/// <para>③ <c>GetUserInfoAsync</c>——<c>sns/userinfo</c> 拉取用户信息（响应裁剪，snsapi_base 场景降级 null）；</para>
/// <para>④ <b>菜单三件套（V0.4.1——自实现不引 Senparc，借鉴微信官方 JSON 结构；token 自管不动显式传参）</b>：
/// <c>CreateMenuAsync</c>（<c>cgi-bin/menu/create</c> POST 全量覆盖——raw JSON 透传，仅验证合法 JSON 对象 +
/// button 非空）/ <c>GetCurrentSelfMenuInfoAsync</c>（<c>cgi-bin/get_current_selfmenu_info</c> 结构化 poco 返回）/
/// <c>DeleteMenuAsync</c>（<c>cgi-bin/menu/delete</c>）。</para>
/// <para>HttpClient 经 **typed client** 注入（<c>AddHttpClient&lt;WeChatApiClient&gt;</c>——ctor 收
/// <see cref="HttpClient"/> + <see cref="IWeChatCredentialResolver"/> + ILogger，对齐 Microsoft.Extensions.Http
/// 惯例；Oracle 评审点 5：库纯逻辑 + HttpClient，不引 AspNetCore，入站端点归装配层）。</para>
/// <para>零持久化零 Store（tkwf-extension 铁律）——纯内存态 access_token 缓存。</para>
/// </summary>
public sealed class WeChatApiClient
{
    private const string ApiBase = "https://api.weixin.qq.com";
    private static readonly TimeSpan TokenEarlyRefresh = TimeSpan.FromMinutes(5);

    private readonly HttpClient _httpClient;
    private readonly IWeChatCredentialResolver _resolver;
    private readonly ILogger<WeChatApiClient> _logger;

    // L1 缓存：appId → (accessToken, expiresAtUtc)；SemaphoreSlim 并发锁防 stampede（对齐 Authentication 语义）
    private readonly ConcurrentDictionary<string, CachedToken> _tokenCache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _tokenLock = new(1, 1);

    /// <summary>
    /// 构造——HttpClient 经 typed client 注入（AddHttpClient&lt;WeChatApiClient&gt;——DI 生命周期托管，避免
    /// HttpClient 悬挂 socket）；<paramref name="resolver"/> <b>非空</b>（DI 容器注入——Oracle P1：
    /// <c>AddWeChatFederationChannels()</c> 已注册缺省 <see cref="OptionsWeChatCredentialResolver"/>，
    /// 不做 ctor 内 fallback 防绕过 DI 生命周期管理）。
    /// </summary>
    public WeChatApiClient(HttpClient httpClient, IWeChatCredentialResolver resolver, ILogger<WeChatApiClient> logger)
    {
        _httpClient = httpClient;
        _resolver = resolver ?? throw new ArgumentNullException(nameof(resolver));
        _logger = logger;
    }

    /// <summary>
    /// 获取 access_token（<c>cgi-bin/token</c>——L1 缓存 + 并发锁 + 提前 5 分钟过期刷新）。
    /// <para>凭证经 <see cref="IWeChatCredentialResolver"/> 按 <paramref name="appId"/> 精确解析（Oracle P2-4 自持）。
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

            var secret = await ResolveCredentialAsync(appId, ct);

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
        var secret = await ResolveCredentialAsync(appId, ct);

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

    /// <summary>
    /// 拉取关注状态（<c>cgi-bin/user/info</c>——subscribe 字段；I2 §3.5 关注状态反查 IFollowChannelQuery 实现源）。
    /// <para>用户未关注（subscribe=0）→ 返回 IsFollowing=false；关注（1）→ true；API 失败（errcode）→ null
    /// （调用方自行处置——对账路径不打 try/catch 兜底，方案 §3.5）。</para>
    /// </summary>
    public async Task<WeChatFollowStateInfo?> GetFollowStateAsync(string accessToken, string openid, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/cgi-bin/user/info?access_token={Uri.EscapeDataString(accessToken)}&openid={Uri.EscapeDataString(openid)}";
        using var resp = await _httpClient.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
        {
            _logger.LogWarning("微信 cgi-bin/user/info 拉取失败：{Json}（关注状态反查降级 null）", json);
            return null;
        }
        var subscribed = root.TryGetProperty("subscribe", out var sub) && sub.GetInt32() == 1;
        DateTime? subscribeTime = null;
        if (subscribed && root.TryGetProperty("subscribe_time", out var subTime))
            subscribeTime = DateTimeOffset.FromUnixTimeSeconds(subTime.GetInt64()).UtcDateTime;
        return new WeChatFollowStateInfo(subscribed, subscribeTime);
    }

    /// <summary>
    /// 创建/全量覆盖自定义菜单（<c>cgi-bin/menu/create</c> POST——方案 §3.2.1，自实现不引 Senparc）。
    /// <para><paramref name="menuJson"/> 为<b>完整按钮结构 raw JSON 透传</b>（编辑器自由形态——click/view/
    /// miniprogram/media_id/article_id/view_limited 等类型参数各异）；仅验证<b>合法 JSON 对象 + button 非空数组</b>，
    /// 结构自由形态不做类型化校验。</para>
    /// <para>成功（<c>errcode==0</c>）返回 <c>errmsg</c>（如 "ok"）；失败抛
    /// <see cref="InvalidOperationException"/> 含微信错误码（<c>errcode/errmsg</c> 原文）。</para>
    /// </summary>
    public async Task<string> CreateMenuAsync(string accessToken, string menuJson, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(menuJson);

        // 提交 = raw JSON 透传——仅验证合法 JSON 对象 + button 非空（自由形态不做类型化校验）
        JsonDocument validation;
        try
        {
            validation = JsonDocument.Parse(menuJson);
        }
        catch (JsonException ex)
        {
            throw new ArgumentException("菜单 JSON 不是合法 JSON", nameof(menuJson), ex);
        }
        using (validation)
        {
            var root = validation.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ArgumentException("菜单 JSON 必须为合法 JSON 对象（button 数组承载）", nameof(menuJson));
            if (!root.TryGetProperty("button", out var button)
                || button.ValueKind != JsonValueKind.Array
                || button.GetArrayLength() == 0)
                throw new ArgumentException("菜单 JSON 必须包含非空 button 数组", nameof(menuJson));
        }

        var url = $"{ApiBase}/cgi-bin/menu/create?access_token={Uri.EscapeDataString(accessToken)}";
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(menuJson, Encoding.UTF8, "application/json"),
        };
        using var resp = await _httpClient.SendAsync(req, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var rootResp = doc.RootElement;
        if (rootResp.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
            throw new InvalidOperationException($"微信菜单创建失败：{json}");

        return rootResp.TryGetProperty("errmsg", out var msg) ? msg.GetString() ?? string.Empty : string.Empty;
    }

    /// <summary>
    /// 拉取当前生效菜单（<c>cgi-bin/get_current_selfmenu_info</c>——方案 §3.2.1，返回结构化 poco 供展示/回填编辑器）。
    /// <para>成功返回 <see cref="WeChatSelfMenuInfoResult"/>（<see cref="WeChatSelfMenuInfoResult.SelfMenuInfo"/>
    /// 可能为 null——<c>is_menu_open</c>=0 / 微信侧未返回 <c>selfmenu_info</c> 时按"无菜单"处理）；
    /// 失败抛 <see cref="InvalidOperationException"/> 含微信错误码（如 46003 菜单不存在）。</para>
    /// </summary>
    public async Task<WeChatSelfMenuInfoResult?> GetCurrentSelfMenuInfoAsync(string accessToken, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/cgi-bin/get_current_selfmenu_info?access_token={Uri.EscapeDataString(accessToken)}";
        using var resp = await _httpClient.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
            throw new InvalidOperationException($"微信当前菜单查询失败：{json}");

        if (!root.TryGetProperty("selfmenu_info", out _))
            return null;   // 未配置菜单（is_menu_open=0 / 字段缺失）→ null（调用方按"无菜单"处理）

        return root.Deserialize<WeChatSelfMenuInfoResult>();
    }

    /// <summary>
    /// 删除当前自定义菜单（<c>cgi-bin/menu/delete</c>——方案 §3.2.1）。成功（<c>errcode==0</c>）无返回；
    /// 失败抛 <see cref="InvalidOperationException"/> 含微信错误码。
    /// </summary>
    public async Task DeleteMenuAsync(string accessToken, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/cgi-bin/menu/delete?access_token={Uri.EscapeDataString(accessToken)}";
        using var resp = await _httpClient.GetAsync(url, ct);
        var json = await resp.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (root.TryGetProperty("errcode", out var err) && err.GetInt32() != 0)
            throw new InvalidOperationException($"微信菜单删除失败：{json}");
    }

    /// <summary>
    /// 凭证解析——经 <see cref="IWeChatCredentialResolver"/>（V0.4.1 可插拔契约；缺省
    /// <see cref="OptionsWeChatCredentialResolver"/> 读 <see cref="WeChatOptions.Channels"/> 按 AppId 精确匹配——
    /// Oracle P2-4 凭证自持，不经 Authentication；未命中 → 抛凭证未配置异常（现状 <c>ResolveCredential</c> 语义迁移）。
    /// </summary>
    private async Task<WeChatChannelConfig> ResolveCredentialAsync(string appId, CancellationToken ct)
    {
        var config = await _resolver.ResolveAsync(appId, ct);
        return config
            ?? throw new InvalidOperationException($"微信凭证未配置：appId={appId}（IWeChatCredentialResolver 未命中——Channels 无匹配项或为空）");
    }

    private sealed record CachedToken(string AccessToken, DateTime ExpiresAtUtc);
}
