using System.Text.Json.Serialization;

namespace TKWF.Ext.AuthCenter;

/// <summary>令牌响应（标准形态——access_token/refresh_token/token_type/expires_in；V0.10.1 双发 camelCase + snake_case）。</summary>
/// <param name="AccessToken">JWT access token（RS256）。</param>
/// <param name="RefreshToken">Refresh token（rotation——SHA256 落库）。</param>
/// <param name="TokenType">固定 Bearer。</param>
/// <param name="ExpiresIn">access token 有效期（秒）。</param>
public sealed record TokenResponse(string AccessToken, string RefreshToken, string TokenType, int ExpiresIn)
{
    [JsonPropertyName("access_token")]
    public string AccessTokenAlias => AccessToken;

    [JsonPropertyName("refresh_token")]
    public string RefreshTokenAlias => RefreshToken;

    [JsonPropertyName("token_type")]
    public string TokenTypeAlias => TokenType;

    [JsonPropertyName("expires_in")]
    public int ExpiresInAlias => ExpiresIn;
}

/// <summary>密码登录请求（SecurePassword 协议：Identifier 为 UId 或 Phone；ClientHash/Salt 为客户端 PBKDF2 产物，服务端零明文）。</summary>
public sealed record PasswordLoginRequest(string Identifier, string ClientHash, string Salt);

/// <summary>外部 IdP 票据 SPA 兑现请求（POST {prefix}/login/ticket；ChannelId 可选——多通道消歧）。</summary>
/// <param name="Ticket">32B base64url 密封票据。</param>
/// <param name="ChannelId">通道 id（键 channelId——默认 Web JSON 绑定）。</param>
public sealed record ExternalTicketLoginRequest(string Ticket, string? ChannelId)
{
    /// <summary>snake_case 契约别名（channel_id 不命中 ChannelId——显式映射保证文档契约可用，camelCase 仍可用）。</summary>
    [JsonPropertyName("channel_id")]
    public string? ChannelIdSnakeCase { get; init; }

    /// <summary>生效通道 id（channelId 优先，其次 channel_id；均空 → null = 由票载荷决定）。</summary>
    [JsonIgnore]
    public string? EffectiveChannelId
        => !string.IsNullOrWhiteSpace(ChannelId) ? ChannelId : ChannelIdSnakeCase;
}
