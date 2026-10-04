namespace TKWF.Ext.Authentication;

/// <summary>
/// SSO 账号消费面 DTO（不可变 record）——只含联邦层消费字段，
/// <b>不含</b> TokenVersion/PasswordHash/IsEnabled（敏感字段不出契约包，ADR-SSO Oracle P1-2）。
/// </summary>
/// <param name="UId">平台内部 id（token2 <c>sub</c>）。</param>
/// <param name="FederationAnchorOpenId">联盟锚点 openid（自建 unionid，跨商户稳定锚点；可空——未绑定联盟锚点的账号）。</param>
/// <param name="Nickname">昵称（微信授权拉取；可空）。</param>
/// <param name="AvatarUrl">头像 URL（可空）。</param>
/// <param name="AuthLevel">认证强度（1=手机号 / 2=微信便捷 / 3=教师核实）。</param>
public sealed record SsoAccountDto(
    string UId,
    string? FederationAnchorOpenId,
    string? Nickname,
    string? AvatarUrl,
    int AuthLevel);
