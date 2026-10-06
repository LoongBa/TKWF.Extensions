using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Federation.QQ;

/// <summary>
/// QQ 互联出站 API 客户端（平台网关库——凭证从 <see cref="QqOptions.Channels"/> 解析，N3 P1-1 凭证自持）。
/// <para>出站能力（对齐 WeChatApiClient 先例形态 + QQ 协议事实，N3 T2）：</para>
/// <para>① <c>GetAccessTokenAsync</c>——<c>graph.qq.com/oauth2.0/token</c>（grant_type=authorization_code +
/// client_secret 对称密钥 + <b>redirect_uri 一致性校验</b>（OAuth RFC 6749 §4.1.3——须与授权时一致，防 code 窃取换
/// token，N3 P1-4）+ <c>fmt=json</c> 强制——<b>QQ 默认 x-www-form-urlencoded 非 JSON</b>，官方 PHP 实证出错还可能
/// 返 JSONP <c>callback(...)</c>，统一剥壳兜底）；code 10min 过期；access_token 60 天；</para>
/// <para>② <c>GetMeAsync</c>——<c>/oauth2.0/me</c> 换 openid（<c>fmt=json</c> 强制——<b>默认 JSONP</b>）+ 可选 unionid
/// （<c>unionid=1</c> 需官网预申请，未申请返 100048——<see cref="QqChannelConfig.EnableUnionId"/> 开关驱动）；
/// ⚠️ **用户级 access_token/refresh_token 不缓存不续期**（Oracle P1-5：QQ token = 用户级 authorization_code grant，
/// 联邦认证面一次性消费；refresh 归 L7 非认证面 YAGNI）；</para>
/// <para>③ <c>GetUserInfoAsync</c>——<c>graph.qq.com/user/get_user_info</c>（access_token + oauth_consumer_key=appId +
/// openid；<b>错误模型 ret/msg 不同于 error/error_description</b>——分端点解析）；响应裁剪 nickname/figureurl 头像/
/// gender/province/city（**无手机号无邮箱**，官方明示性别/省市非真实数据，N3 §3.2），获失败降级 null。</para>
/// <para>HttpClient 经 **typed client** 注入（<c>AddHttpClient&lt;QqApiClient&gt;</c>——ctor 收 HttpClient + IOptions +
/// ILogger；Oracle 评审点 5：库纯逻辑 + HttpClient，不引 AspNetCore）。零持久化零 Store（tkwf-extension 铁律）。</para>
/// </summary>
public sealed class QqApiClient
{
    private const string ApiBase = "https://graph.qq.com";

    private readonly HttpClient _httpClient;
    private readonly IOptions<QqOptions> _options;
    private readonly ILogger<QqApiClient> _logger;

    /// <summary>HttpClient 经 typed client 注入（AddHttpClient&lt;QqApiClient&gt;——DI 生命周期托管，避免 HttpClient 悬挂 socket）。</summary>
    public QqApiClient(HttpClient httpClient, IOptions<QqOptions> options, ILogger<QqApiClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    /// <summary>
    /// code+client_secret → access_token（<c>graph.qq.com/oauth2.0/token</c>，fmt=json）。
    /// <para>redirect_uri 一致性校验（N3 P1-4）：出站显式携带 <paramref name="redirectUri"/>（与 authorize 步骤一致），
    /// QQ 服务端比对，不一致返错误码——缺省抛 <see cref="InvalidOperationException"/>（防 code 窃取换 token 攻击面）。</para>
    /// ⚠️ 用户级 token 不缓存（P1-5）——每次调用独立换取。
    /// </summary>
    public async Task<string> GetAccessTokenAsync(string appId, string code, string redirectUri, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(redirectUri))
            throw new InvalidOperationException("QQ redirect_uri 必填——code 换 token 须与授权时一致（OAuth RFC 6749 §4.1.3）");

        var secret = ResolveCredential(appId);

        // fmt=json 强制（QQ 默认 x-www-form-urlencoded 非 JSON——官方文档明示）
        var url = $"{ApiBase}/oauth2.0/token?grant_type=authorization_code" +
                  $"&client_id={Uri.EscapeDataString(secret.AppId)}" +
                  $"&client_secret={Uri.EscapeDataString(secret.AppSecret)}" +
                  $"&code={Uri.EscapeDataString(code)}" +
                  $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
                  "&fmt=json";
        var json = await GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (TryGetError(root, out var errMsg))
            throw new InvalidOperationException($"QQ access_token 获取失败：{errMsg}");

        return root.GetProperty("access_token").GetString()!;
    }

    /// <summary>
    /// <c>/oauth2.0/me</c> 换 openid（应用维度唯一——external_uid 稳定映射键，N3 P1-2）。
    /// <para>经 <see cref="GetMeAsync"/> 取 OpenId（EnableUnionId 由配置驱动，本方法语义 = 仅 openid）。</para>
    /// </summary>
    public async Task<string> GetOpenIdAsync(string appId, string accessToken, CancellationToken ct = default)
    {
        var me = await GetMeAsync(appId, accessToken, ct);
        return me.OpenId;
    }

    /// <summary>
    /// <c>/oauth2.0/me</c> 换取 openid +（可选）unionid——N3 P1-2：external_uid 始终 = openid；
    /// <see cref="QqChannelConfig.EnableUnionId"/> 开启时额外请求 <c>unionid=1</c> 并随结果返回（联盟锚点写入辅助，
    /// 写 <c>FederationAnchorOpenId</c> 归装配层；未申请权限时 QQ 返 100048 companyid not set → 抛）。
    /// <para>⚠️ 响应默认 JSONP（<c>callback(...)</c>，官方原文）——恒传 <c>fmt=json</c> + 剥壳兜底。</para>
    /// </summary>
    public async Task<QqMeResult> GetMeAsync(string appId, string accessToken, CancellationToken ct = default)
    {
        var secret = ResolveCredential(appId);

        var url = $"{ApiBase}/oauth2.0/me?access_token={Uri.EscapeDataString(accessToken)}&fmt=json" +
                  (secret.EnableUnionId ? "&unionid=1" : "");
        var json = await GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        if (TryGetError(root, out var errMsg))
            throw new InvalidOperationException($"QQ openid 获取失败：{errMsg}");

        var openId = root.GetProperty("openid").GetString()!;
        var unionId = root.TryGetProperty("unionid", out var u) && u.ValueKind == JsonValueKind.String
            ? u.GetString()
            : null;
        return new QqMeResult(openId, secret.EnableUnionId ? unionId : null);
    }

    /// <summary>
    /// 拉取用户信息（<c>graph.qq.com/user/get_user_info</c>——oauth_consumer_key=appId；原生 JSON 无需 fmt）。
    /// <para><b>错误模型 ret/msg</b>（ret≠0 失败，如 1002 请先登录）——不同于 token/me 的 error/error_description，
    /// 分端点解析；失败降级返回 null（对齐 WeChat sns/userinfo 语义）。</para>
    /// </summary>
    public async Task<QqUserInfo?> GetUserInfoAsync(string appId, string accessToken, string openid, CancellationToken ct = default)
    {
        var url = $"{ApiBase}/user/get_user_info" +
                  $"?access_token={Uri.EscapeDataString(accessToken)}" +
                  $"&oauth_consumer_key={Uri.EscapeDataString(appId)}" +
                  $"&openid={Uri.EscapeDataString(openid)}";
        var json = await GetStringAsync(url, ct);
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        if (root.TryGetProperty("ret", out var ret) && ret.ValueKind == JsonValueKind.Number && ret.GetInt32() != 0)
        {
            var msg = root.TryGetProperty("msg", out var m) && m.ValueKind == JsonValueKind.String ? m.GetString() : "";
            _logger.LogWarning("QQ get_user_info 获取失败：ret={Ret} msg={Msg}（返回 null 降级）", ret.GetInt32(), msg);
            return null;
        }

        return new QqUserInfo(
            openid,
            GetStringOrNull(root, "nickname"),
            GetStringOrNull(root, "figureurl_qq_1") ?? GetStringOrNull(root, "figureurl"),
            GetStringOrNull(root, "gender"),
            GetStringOrNull(root, "province"),
            GetStringOrNull(root, "city"));
    }

    /// <summary>凭证解析——从 <see cref="QqOptions.Channels"/> 按 AppId 精确匹配（Oracle P2-4：凭证自持，不经 Authentication）。</summary>
    private QqChannelConfig ResolveCredential(string appId)
    {
        var channels = _options.Value.Channels;
        if (channels == null || channels.Count == 0)
            throw new InvalidOperationException("QQ 凭证未配置：TKWF:Federation:QQ 节 Channels 为空");

        foreach (var channel in channels)
        {
            if (string.Equals(channel.AppId, appId, StringComparison.Ordinal))
                return channel;
        }

        throw new InvalidOperationException($"QQ 凭证未配置：appId={appId}（Channels 无匹配项）");
    }

    /// <summary>GET + 响应剥壳（token/me 端点出错时可能返 JSONP <c>callback(...)</c>——官方 PHP 样例实证兜底）。</summary>
    private async Task<string> GetStringAsync(string url, CancellationToken ct)
    {
        using var resp = await _httpClient.GetAsync(url, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"QQ API HTTP {(int)resp.StatusCode}：{Truncate(body)}");
        return StripJsonp(body);
    }

    /// <summary>剥离 JSONP 壳（callback({...}); 或 callback({...})——官方 token/me 端点历史 JSONP 格式兜底）。</summary>
    internal static string StripJsonp(string json)
    {
        var s = json.Trim();
        if (s.StartsWith("callback(", StringComparison.Ordinal) && s.EndsWith(");", StringComparison.Ordinal))
            return s["callback(".Length..^2].Trim();
        if (s.StartsWith("callback(", StringComparison.Ordinal) && s.EndsWith(')'))
            return s["callback(".Length..^1].Trim();
        return s;
    }

    /// <summary>提取 error/error_description（token/me 端点错误模型——error 为数字，String/Number/其他双路兜底）。</summary>
    private static bool TryGetError(JsonElement root, out string message)
    {
        if (root.TryGetProperty("error", out var err) && err.ValueKind != JsonValueKind.Null)
        {
            message = err.ValueKind switch
            {
                JsonValueKind.Number => $"error={err.GetInt32()}",
                JsonValueKind.String => $"error={err.GetString()}",
                _ => $"error={err.GetRawText()}"
            };
            if (root.TryGetProperty("error_description", out var desc) && desc.ValueKind == JsonValueKind.String)
                message += $"，{desc.GetString()}";
            return true;
        }
        message = "";
        return false;
    }

    private static string? GetStringOrNull(JsonElement root, string name)
        => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static string Truncate(string s, int max = 200)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");
}