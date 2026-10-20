namespace TKWF.Ext.AuthCenter;

/// <summary>
/// SSO 账号消费面 DTO（不可变 record）——只含联邦层消费字段，
/// <b>不含</b> TokenVersion/PasswordHash/IsEnabled（敏感字段不出契约包，ADR-SSO Oracle P1-2）。
/// </summary>
/// <param name="UId">平台内部 id（token2 <c>sub</c>）。</param>
/// <param name="Nickname">昵称（微信授权拉取；可空）。</param>
/// <param name="AvatarUrl">头像 URL（可空）。</param>
/// <param name="AuthLevel">认证强度（1=手机号 / 2=联邦快捷；V0.9.0 起剔除 3=教师核实）。</param>
/// <remarks>
/// V0.11.0（方案 §6.5 L331 / T7）：删 <c>FederationAnchorOpenId</c>——联盟锚点语义随
/// <c>AuthAccount.FederationAnchorOpenId</c> 列退役，联邦身份映射归
/// <see cref="IExternalIdentityService"/>（权威 = AuthFactorEntity）。破坏性契约变更 → AuthCenter.Abstractions 0.2.0。
/// </remarks>
public sealed record SsoAccountDto(
    string UId,
    string? Nickname,
    string? AvatarUrl,
    int AuthLevel);
