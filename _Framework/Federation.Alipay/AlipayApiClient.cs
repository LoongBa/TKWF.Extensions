using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Federation.Alipay;

/// <summary>
/// 支付宝开放平台出站 API 客户端（平台网关库——凭证从 <see cref="AlipayOptions.Channels"/> 解析，N4 T2 + 凭证自持 Oracle P2-4）。
/// <para>统一网关 <c>https://openapi.alipay.com/gateway.do</c>（POST URL 编码表单 + charset 在查询串）——RSA2 双向签名：</para>
/// <para>① <see cref="GetAccessTokenAsync"/>——<c>alipay.system.oauth.token</c>（grant_type=authorization_code +
/// <b>code 顶层参数、无 biz_content</b>（librarian 协议事实）——app_id 承载身份 + 商户私钥 RSA2 签名认证，无 client_id/client_secret）；
/// 响应 <c>alipay_system_oauth_token_response</c> 包装（user_id 16 位 2088 开头 / open_id / access_token 40 /
/// expires_in 秒 / refresh_token）+ 顶层 sign——<b>先验签后解析</b>（信任根完备）；</para>
/// <para>② <see cref="GetUserInfoAsync"/>——<c>alipay.user.info.share</c>（⚠️ token 字段名 <b><c>auth_token</c></b>
/// 顶层公共参数 = access_token，非 access_token 字段；无业务参数不传 biz_content；scope=auth_base 调用必报
/// <c>aop.invalid-auth-token</c>——单独调用方负责 scope=auth_user 保证，channel 层不入库）；</para>
/// <para>③ <see cref="RefreshAccessTokenAsync"/>——grant_type=refresh_token 续期封装（本库按 QQ P1-5 不缓存用户级
/// token，方法保留供未来 L7 非认证面）；</para>
/// <para>④ <see cref="BuildAuthorizeUrl"/>——openauth 授权 URL 构造（N4 §三三种授权形态边界：authorize 构造归
/// 装配层；扫码页 <c>alipay.user.info.auth</c> 生成 QR 码 URL 须签名调用网关取回——Phase 1 未落为独立方法，
/// 装配层可经本方法拼标准授权 URL（扫码二维码 = 授权页二维码）。</para>
/// <para>HttpClient 经 **typed client** 注入（<c>AddHttpClient&lt;AlipayApiClient&gt;</c>——ctor 收 HttpClient +
/// IOptions + AlipaySignService + ILogger；Oracle 评审点 5：库纯逻辑 + HttpClient，不引 AspNetCore）。
/// 零持久化零 Store（tkwf-extension 铁律）。</para>
/// </summary>
public sealed class AlipayApiClient
{
    private const string GatewayUrl = "https://openapi.alipay.com/gateway.do";
    private static readonly Uri GatewayUri = new(GatewayUrl);

    private readonly HttpClient _httpClient;
    private readonly IOptions<AlipayOptions> _options;
    private readonly AlipaySignService _signService;
    private readonly ILogger<AlipayApiClient> _logger;
    private readonly ConcurrentDictionary<string, string> _pemCache = new(StringComparer.Ordinal);

    /// <summary>HttpClient 经 typed client 注入（AddHttpClient&lt;AlipayApiClient&gt;——DI 生命周期托管，避免 HttpClient 悬挂 socket）。</summary>
    public AlipayApiClient(
        HttpClient httpClient,
        IOptions<AlipayOptions> options,
        AlipaySignService signService,
        ILogger<AlipayApiClient> logger)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _signService = signService ?? throw new ArgumentNullException(nameof(signService));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// auth_code → access_token（<c>alipay.system.oauth.token</c>——统一网关，RSA2 双向签名）。
    /// <para>⚠️ 无 biz_content：grant_type/code 为顶层参数；无 client_id/client_secret：app_id 承载身份 +
    /// 商户私钥 RSA2 签名认证（librarian 协议事实）。auth_code 3min~24h 动态、一次性。
    /// ⚠️ 用户级 token 不缓存（对齐 QQ P1-5）——每次调用独立换取。</para>
    /// </summary>
    public async Task<AlipayTokenResult> GetAccessTokenAsync(string appId, string code, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code))
            throw new InvalidOperationException("支付宝 auth_code 必填——一次性换取 access_token（3min~24h 动态时效）");

        var credential = ResolveCredential(appId);
        using var doc = await PostAndVerifyAsync(
            "alipay.system.oauth.token",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
            },
            credential, ct);

        var biz = RequireBusiness(doc, "alipay_system_oauth_token_response");
        return ParseTokenResult(biz);
    }

    /// <summary>
    /// 拉取用户信息（<c>alipay.user.info.share</c>——⚠️ token 字段名 <c>auth_token</c> 顶层公共参数 = access_token，
    /// 非 access_token 字段；无业务参数不传 biz_content）。
    /// <para>⚠️ scope=auth_base 调用必报 <c>aop.invalid-auth-token</c>——仅身份场景不调此接口（单独调用方负责
    /// scope=auth_user 保证，channel 层不入库）；业务错误抛 <see cref="InvalidOperationException"/>（sub_code 优先）；
    /// 响应缺 user_id 降级返回 null（对齐 QQ get_user_info 语义）。</para>
    /// </summary>
    public async Task<AlipayUserInfo?> GetUserInfoAsync(string appId, string accessToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("支付宝 auth_token 必填（access_token——顶层公共参数名 auth_token）");

        var credential = ResolveCredential(appId);
        using var doc = await PostAndVerifyAsync(
            "alipay.user.info.share",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["auth_token"] = accessToken,
            },
            credential, ct);

        var biz = RequireBusiness(doc, "alipay_user_info_share_response");
        var userId = GetStringOrNull(biz, "user_id");
        if (userId is null)
        {
            _logger.LogWarning("支付宝 user.info.share 响应缺少 user_id（返回 null 降级）");
            return null;
        }

        return new AlipayUserInfo(
            UserId: userId,
            OpenId: GetStringOrNull(biz, "open_id"),
            Nickname: GetStringOrNull(biz, "nick_name"),
            Avatar: GetStringOrNull(biz, "avatar"),
            Province: GetStringOrNull(biz, "province"),
            City: GetStringOrNull(biz, "city"),
            Gender: GetStringOrNull(biz, "gender"));
    }

    /// <summary>
    /// access_token 续期（<c>grant_type=refresh_token</c>——同样 RSA2 签名换取）。
    /// <para>⚠️ 本库按 QQ N3 P1-5 不缓存用户级 token（联邦认证面一次性消费）；refresh_token 长续期归
    /// L7 非认证面——方法保留供未来（YAGNI 不预设调用链）。</para>
    /// </summary>
    public async Task<AlipayTokenResult> RefreshAccessTokenAsync(string appId, string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken))
            throw new InvalidOperationException("支付宝 refresh_token 必填（grant_type=refresh_token 续期）");

        var credential = ResolveCredential(appId);
        using var doc = await PostAndVerifyAsync(
            "alipay.system.oauth.token",
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
            },
            credential, ct);

        var biz = RequireBusiness(doc, "alipay_system_oauth_token_response");
        return ParseTokenResult(biz);
    }

    /// <summary>
    /// 构造支付宝 authorize URL（<c>openauth.alipay.com/oauth2/publicAppAuthorize.htm</c>——N4 三种授权形态
    /// 之一：跳转（PC 拼接授权链接）+ 扫码（二维码 = 授权页二维码）。authorize 构造归装配层（Oracle P1-1）。
    /// <para>⚠️ 扫码页专用 <c>alipay.user.info.auth</c>（返回 <c>qr_code_url</c>）须经 gateway.do 签名调用取回——
    /// 属装配层扫码编排（Phase 1 未落为独立方法，YAGNI 不预设）；本方法返回标准授权 URL 供装配层拼接授权链。
    /// <paramref name="scope"/>：auth_base（静默仅 user_id）/ auth_user（主动授权可取用户信息）；
    /// <paramref name="state"/>：CSRF（base64 ≤100 位）——N4 §3.2 强制要求，装配层生成 + 回调校验。</para>
    /// </summary>
    public static string BuildAuthorizeUrl(string appId, string redirectUri, string scope, string? state = null)
    {
        if (string.IsNullOrWhiteSpace(appId))
            throw new ArgumentException("支付宝 app_id 必填", nameof(appId));
        if (string.IsNullOrWhiteSpace(redirectUri))
            throw new ArgumentException("支付宝 redirect_uri 必填（须与支付宝控制台配置一致——N4 P1-2 防开放重定向）", nameof(redirectUri));
        if (string.IsNullOrWhiteSpace(scope))
            throw new ArgumentException("支付宝 scope 必填（auth_base / auth_user）", nameof(scope));

        return "https://openauth.alipay.com/oauth2/publicAppAuthorize.htm"
            + $"?app_id={Uri.EscapeDataString(appId)}"
            + $"&scope={Uri.EscapeDataString(scope)}"
            + $"&redirect_uri={Uri.EscapeDataString(redirectUri)}"
            + (string.IsNullOrWhiteSpace(state) ? "" : $"&state={Uri.EscapeDataString(state)}");
    }

    /// <summary>凭证解析——从 <see cref="AlipayOptions.Channels"/> 按 AppId 精确匹配（Oracle P2-4：凭证自持，不经 Authentication）。</summary>
    private AlipayChannelConfig ResolveCredential(string appId)
    {
        var channels = _options.Value.Channels;
        if (channels == null || channels.Count == 0)
            throw new InvalidOperationException("支付宝凭证未配置：TKWF:Federation:Alipay 节 Channels 为空");

        foreach (var channel in channels)
        {
            if (string.Equals(channel.AppId, appId, StringComparison.Ordinal))
                return channel;
        }

        throw new InvalidOperationException($"支付宝凭证未配置：appId={appId}（Channels 无匹配项）");
    }

    /// <summary>gateway.do POST（URL 编码表单）→ 验签 → 业务 code 检查 → 返回已解析 JsonDocument（调用方 using 释放）。
    /// 抛错路径内部释放 doc（防 JsonDocument 池化缓冲区泄漏）。</summary>
    private async Task<JsonDocument> PostAndVerifyAsync(
        string method,
        IReadOnlyDictionary<string, string> topLevelParams,
        AlipayChannelConfig credential,
        CancellationToken ct)
    {
        var body = await PostGatewayAsync(method, topLevelParams, credential, ct);

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(body);
        }
        catch (JsonException)
        {
            throw new InvalidOperationException($"支付宝 {method} 响应非 JSON：{Truncate(body)}");
        }

        try
        {
            var root = doc.RootElement;

            // 信任根完备（N4 §3.2 强双向签名）：先验签（保留原始 body 自实现——AlipaySignService.VerifyResponse），
            // 签名不符/缺失 → 拒绝（librarian §5）
            var sign = GetStringOrNull(root, "sign");
            if (string.IsNullOrEmpty(sign)
                || !_signService.VerifyResponse(body, sign!, LoadAlipayPublicKeyPem(credential.AlipayPublicKeyPath)))
                throw new InvalidOperationException($"支付宝 {method} 响应验签失败：FAIL_SIGNATURE（拒绝信任）");

            // 业务体 code/msg：10000 成功；非 10000 抛 InvalidOperationException（sub_code 优先——librarian 协议事实）
            var wrapper = method.Replace('.', '_') + "_response";
            if (root.TryGetProperty(wrapper, out var biz))
            {
                var code = GetStringOrNull(biz, "code");
                if (!string.IsNullOrEmpty(code) && !string.Equals(code, "10000", StringComparison.Ordinal))
                {
                    var subCode = GetStringOrNull(biz, "sub_code");
                    var msg = GetStringOrNull(biz, "msg") ?? "";
                    var detail = GetStringOrNull(biz, "sub_msg");
                    if (detail is not null) msg = $"{msg}（{detail}）";
                    throw new InvalidOperationException($"支付宝 {method} 失败：{(!string.IsNullOrEmpty(subCode) ? subCode! : code)} {msg}");
                }
            }

            return doc; // 所有权移交调用方（调用方 using var 释放）
        }
        catch
        {
            doc.Dispose();
            throw;
        }
    }

    /// <summary>gateway.do 出站：组装公共参数 + 方法特定顶层参数 → RSA2 签名（商户私钥）→ POST 表单。</summary>
    private async Task<string> PostGatewayAsync(
        string method,
        IReadOnlyDictionary<string, string> topLevelParams,
        AlipayChannelConfig credential,
        CancellationToken ct)
    {
        // 公共参数（librarian §4）：app_id/method/format(JSON)/charset(utf-8)/sign_type(RSA2)/timestamp/version(1.0)
        // + 方法特定顶层参数（code/grant_type/auth_token——支付宝 oauth token 无 biz_content；user.info.share 无业务参数）
        var parameters = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["app_id"] = credential.AppId,
            ["method"] = method,
            ["format"] = "JSON",
            ["charset"] = "utf-8",
            ["sign_type"] = "RSA2",
            ["timestamp"] = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"), // 支付宝按北京时间校验；对齐官方 SDK 本地时间
            ["version"] = "1.0",
        };
        foreach (var (key, value) in topLevelParams)
            parameters[key] = value;

        // RSA2 签名（商户私钥签请求——AlipaySignService.BuildRequestSignature 委托框架 AlipaySignUtil）
        var sign = _signService.BuildRequestSignature(parameters, LoadPrivateKeyPem(credential.PrivateKeyPath));
        parameters["sign"] = sign;

        // POST URL 编码表单（Content-Type: application/x-www-form-urlencoded）；charset 在查询串
        // （librarian §4：biz_content 与 sign 值 URL Encode；FormUrlEncodedContent 自动编码）
        var content = new FormUrlEncodedContent(parameters);
        using var resp = await _httpClient.PostAsync(GatewayUri + "?charset=utf-8", content, ct);
        var body = await resp.Content.ReadAsStringAsync(ct);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"支付宝 gateway.do HTTP {(int)resp.StatusCode}：{Truncate(body)}");
        return body;
    }

    private static JsonElement RequireBusiness(JsonDocument doc, string wrapper)
    {
        if (doc.RootElement.TryGetProperty(wrapper, out var biz))
            return biz;
        // 网关层 error_response（如 code 40002 INVALID_PARAMETER）——错误体无业务包装，携网关错误信息抛
        var gatewayCode = GetStringOrNull(doc.RootElement, "error_response");
        throw new InvalidOperationException($"支付宝 gateway.do 响应缺少业务体 {wrapper}（网关错误：{gatewayCode ?? "未知"}）");
    }

    private static AlipayTokenResult ParseTokenResult(JsonElement biz)
    {
        var userId = GetStringOrNull(biz, "user_id")
            ?? throw new InvalidOperationException("支付宝响应缺少 user_id（16 位 2088 开头——external_uid 稳定映射键）");
        var accessToken = GetStringOrNull(biz, "access_token")
            ?? throw new InvalidOperationException("支付宝响应缺少 access_token");

        return new AlipayTokenResult(
            UserId: userId,
            OpenId: GetStringOrNull(biz, "open_id"),
            AccessToken: accessToken,
            ExpiresInSeconds: TryGetInt32(biz, "expires_in") ?? 0,
            RefreshToken: GetStringOrNull(biz, "refresh_token"));
    }

    /// <summary>商户私钥 PEM 读取（L1 缓存按路径——生产缺文件抛 FileNotFoundException，fail-fast F7；启动/首用即失败）。</summary>
    private string LoadPrivateKeyPem(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("支付宝商户私钥 PEM 路径未配置（AlipayChannelConfig.PrivateKeyPath）——生产 fail-fast 缺钥拒");
        return _pemCache.GetOrAdd(path, static p => File.ReadAllText(p));
    }

    /// <summary>支付宝公钥 PEM 读取（同上——验签响应必需）。</summary>
    private string LoadAlipayPublicKeyPem(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new InvalidOperationException("支付宝公钥 PEM 路径未配置（AlipayChannelConfig.AlipayPublicKeyPath）——验签不可用，生产 fail-fast");
        return _pemCache.GetOrAdd(path, static p => File.ReadAllText(p));
    }

    private static string? GetStringOrNull(JsonElement element, string name)
        => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? TryGetInt32(JsonElement element, string name)
        => element.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : null;

    private static string Truncate(string s, int max = 200)
        => string.IsNullOrEmpty(s) ? "" : (s.Length <= max ? s : s[..max] + "...");
}
