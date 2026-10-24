using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TKWF.Ext.AuthCenter;

// ─────────────────────────── 请求/响应 DTO（B3a/B3b MapGenerated 生成化——自 AuthCenterWebExtension 迁出，
// Web 层协议契约；minimal API handler 删除后由门面接口 [ApiExpose] 生成端点消费，双键 JSON 契约保持） ───────────────────────────

/// <summary>令牌响应（标准形态——access_token/refresh_token/token_type/expires_in）。
/// ⚠️ V0.10.1 双发收口：文档契约键（snake_case）自 V0.6.0 起即如此约定、实现此前仅 camelCase——现补 4 个
/// snake_case 别名属性双键并存，两类消费方零破坏；同传同名冲突不存在（响应只读）。</summary>
/// <param name="AccessToken">JWT access token（RS256）。</param>
/// <param name="RefreshToken">Refresh token（rotation——SHA256 落库）。</param>
/// <param name="TokenType">固定 <c>Bearer</c>。</param>
/// <param name="ExpiresIn">access token 有效期（秒）。</param>
public sealed record TokenResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn)
{
    /// <summary>文档契约键 <c>access_token</c>（V0.6.0 起使用指南即如此约定）。</summary>
    [JsonPropertyName("access_token")]
    public string AccessTokenAlias => AccessToken;

    /// <summary>文档契约键 <c>refresh_token</c>。</summary>
    [JsonPropertyName("refresh_token")]
    public string RefreshTokenAlias => RefreshToken;

    /// <summary>文档契约键 <c>token_type</c>。</summary>
    [JsonPropertyName("token_type")]
    public string TokenTypeAlias => TokenType;

    /// <summary>文档契约键 <c>expires_in</c>。</summary>
    [JsonPropertyName("expires_in")]
    public int ExpiresInAlias => ExpiresIn;
}

/// <summary>短信验证码发送请求。</summary>
public sealed record SendSmsCodeRequest(string Phone, string Scene);

/// <summary>短信登录请求。</summary>
public sealed record SmsLoginRequest(string Phone, string Code);
/// <summary>密码登录请求（V0.9.0 B.9 + ADR 决策 1——SecurePassword 协议：Identifier 为 UId 或 Phone 凭据标识；
/// ClientHash/Salt 为客户端算的 PBKDF2 产物，服务端零明文）。</summary>
public sealed record PasswordLoginRequest(string Identifier, string ClientHash, string Salt);

/// <summary>刷新请求（V0.10.1 双收：文档契约 body = <c>{refresh_token}</c>；camelCase <c>refreshToken</c> 亦接受，
/// 同传时 camelCase 优先——默认 Web JSON 绑定大小写不敏感但不做下划线归一，别名键保证文档契约可用）。</summary>
public sealed record RefreshRequest(string RefreshToken)
{
    /// <summary>文档契约键 <c>refresh_token</c>。</summary>
    [JsonPropertyName("refresh_token")]
    public string? RefreshTokenSnakeCase { get; init; }

    /// <summary>生效 refresh token（<c>refreshToken</c> 优先，其次 <c>refresh_token</c>）。</summary>
    [JsonIgnore]
    public string EffectiveRefreshToken
        => !string.IsNullOrWhiteSpace(RefreshToken) ? RefreshToken : RefreshTokenSnakeCase ?? string.Empty;
}

/// <summary>外部 IdP 票据 SPA 兑现请求（V0.10.0——POST {prefix}/login/ticket；channel_id 可选——多通道消歧，
/// 正常由票载荷 ChannelId 签入，提供时须一致否则 TICKET_INVALID）。</summary>
/// <param name="Ticket">32B base64url 密封票据。</param>
/// <param name="ChannelId">通道 id（键 <c>channelId</c>——默认 Web JSON 绑定）。</param>
public sealed record ExternalTicketLoginRequest(string Ticket, string? ChannelId)
{
    /// <summary>snake_case 契约别名（使用指南 body 写作 <c>{"ticket", "channel_id"}</c>；默认绑定大小写不敏感
    /// 但不做下划线归一，<c>channel_id</c> 不会命中 <see cref="ChannelId"/>——显式映射保证文档契约可用，
    /// 同时保留 camelCase 客户端可用：两条键并存，不因客户端命名风格静默丢失消歧参数）。</summary>
    [JsonPropertyName("channel_id")]
    public string? ChannelIdSnakeCase { get; init; }

    /// <summary>生效通道 id（<c>channelId</c> 优先，其次 <c>channel_id</c>；均空 → null = 由票载荷决定）。</summary>
    [JsonIgnore]
    public string? EffectiveChannelId
        => !string.IsNullOrWhiteSpace(ChannelId) ? ChannelId : ChannelIdSnakeCase;
}

/// <summary>票据换令牌请求（PKCE code_verifier 可选——IssueAsync 时若生成则 Exchange 必传；V0.10.1 双收：
/// 文档契约 body = <c>{ticket, code_verifier, app_id, state}</c>；camelCase <c>codeVerifier</c>/<c>appId</c> 亦接受，
/// 同传时 camelCase 优先——别名键保证文档契约可用）。</summary>
public sealed record TicketExchangeRequest(string Ticket, string? CodeVerifier, string AppId, string? State)
{
    /// <summary>文档契约键 <c>code_verifier</c>。</summary>
    [JsonPropertyName("code_verifier")]
    public string? CodeVerifierSnakeCase { get; init; }

    /// <summary>文档契约键 <c>app_id</c>。</summary>
    [JsonPropertyName("app_id")]
    public string? AppIdSnakeCase { get; init; }

    /// <summary>生效 code_verifier（<c>codeVerifier</c> 优先，其次 <c>code_verifier</c>）。</summary>
    [JsonIgnore]
    public string? EffectiveCodeVerifier
        => !string.IsNullOrWhiteSpace(CodeVerifier) ? CodeVerifier : CodeVerifierSnakeCase;

    /// <summary>生效 app id（<c>appId</c> 优先，其次 <c>app_id</c>）。</summary>
    [JsonIgnore]
    public string EffectiveAppId
        => !string.IsNullOrWhiteSpace(AppId) ? AppId : AppIdSnakeCase ?? string.Empty;
}

// ─────────────────────────── V0.8.0 认证 API 补全（Web DTO） ───────────────────────────

/// <summary>令牌验签快照（/verify——已认证内省，非 RFC 7662 fanout；恒 200 于到达后，无效 Bearer → 401）。</summary>
/// <param name="Valid">恒 true（到达即已验签）。</param>
/// <param name="UserId">平台内部 id（JWT sub 同源）。</param>
/// <param name="AuthType">认证方式（sms/federated/password/redeem）。</param>
/// <param name="AuthLevel">认证强度。</param>
/// <param name="Exp">过期时间（unix 秒）。</param>
/// <param name="Jti">令牌唯一 id。</param>
public sealed record VerifyResponse(
    bool Valid,
    string UserId,
    string AuthType,
    [property: System.Text.Json.Serialization.JsonPropertyName("auth_level")] int AuthLevel,  // P1-2：Results.Ok 默认 camelCase（authLevel）与 README snake_case（auth_level）对齐——局部显式指定
    long Exp,
    string Jti);

/// <summary>应用授权视图（/grants 列表项——Scopes 已拆为列表，拆分归 Web 层 Oracle P2-2 部分采纳；
/// B3b 起拆分下沉门面实现——认证语义归契约层，表现层零编排）。</summary>
/// <param name="AppId">授权应用 id。</param>
/// <param name="Scopes">授权范围（逗号串拆分）。</param>
/// <param name="ValidUntil">应用授权有效期（null = 持续至吊销——非会话有效期）。</param>
/// <param name="Source">授权来源（AuthGrantSources：login/redeem）。</param>
/// <param name="Status">授权状态（0 Active / 1 Revoked）。</param>
public sealed record GrantView(
    string AppId,
    IReadOnlyList<string> Scopes,
    System.DateTime? ValidUntil,
    string Source,
    int Status);

/// <summary>应用授权状态响应（/grants）。</summary>
/// <param name="Grants">有效授权列表（app_id 可空 = 全部）。</param>
public sealed record GrantsResponse(IReadOnlyList<GrantView> Grants);

/// <summary>验证码独立校验请求（/sms/verify——匿名端点）。</summary>
/// <param name="Phone">目标手机号。</param>
/// <param name="Code">验证码。</param>
/// <param name="Scene">场景（SmsScenes 白名单：Login/Register/Bind/Reset）。</param>
public sealed record SmsVerifyRequest(string Phone, string Code, string Scene);

/// <summary>验证码独立校验响应（/sms/verify——不返回令牌，独立校验非登录；B3b 起由
/// <see cref="ISmsVerificationService.VerifyCodeAsync"/> 直接返回——单次消费语义）。</summary>
/// <param name="Verified">是否校验通过（单次消费语义）。</param>
public sealed record SmsVerifyResponse(bool Verified);