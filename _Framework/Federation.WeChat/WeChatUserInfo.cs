namespace TKWF.Federation.WeChat;

/// <summary>微信用户信息（<c>sns/userinfo</c> 响应裁剪——对齐 Authentication 既有 WeChatUserInfo record 语义）。</summary>
/// <param name="OpenId">openid。</param>
/// <param name="Nickname">昵称（未授权 snsapi_base 时 null）。</param>
/// <param name="Avatar">头像 URL（未授权时 null）。</param>
/// <param name="UnionId">unionid（开放平台归并，未关联时 null）。</param>
public sealed record WeChatUserInfo(string OpenId, string? Nickname, string? Avatar, string? UnionId);
