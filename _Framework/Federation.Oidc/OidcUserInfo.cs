namespace TKWF.Federation.Oidc;

/// <summary>
/// OIDC 用户信息 DTO（userinfo 端点响应裁剪——对齐 Federation `SsoChannelAuthResult` 既有语义）。
/// <para>外部登录身份键恒 = id_token sub（<c>SsoChannelAuthResult.ExternalUserId</c>）——email 仅展示字段
/// （Google email 未验证 / Microsoft email 可缺失——不参与身份归一，M2-P2-3）。</para>
/// </summary>
/// <param name="Sub">OIDC subject——不透明字符串整体持久化（P12，不解析结构）。</param>
/// <param name="Email">邮箱（可 null——平台可缺失/未验证）。</param>
/// <param name="EmailVerified">邮箱已验证（Google email_verified claim）。</param>
/// <param name="Name">显示名（profile claim）。</param>
/// <param name="PreferredUsername">用户名（Microsoft preferred_username 等）。</param>
public sealed record OidcUserInfo(
    string Sub,
    string? Email,
    bool EmailVerified,
    string? Name,
    string? PreferredUsername);