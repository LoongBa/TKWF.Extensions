namespace TKWF.Federation.{Namespace};

/// <summary>{平台}用户信息（响应裁剪 record——对齐 Authentication 既有语义）。</summary>
/// <param name="OpenId">平台用户标识（openid）。</param>
/// <param name="Nickname">昵称（未授权时 null）。</param>
/// <param name="Avatar">头像 URL（未授权时 null）。</param>
/// ⚠️ 平台差异：字段集（QQ 无手机号无邮箱——N3 方案 §3.2；敏感字段标注防日志泄露——WeCom P2-3 先例）
public sealed record {Platform}UserInfo(string OpenId, string? Nickname, string? Avatar);
