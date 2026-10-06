namespace TKWF.Federation.DingTalk;

/// <summary>
/// 钉钉用户信息（响应裁剪——B 端员工组织字段；两步填充 Oracle 评审 P1-4/P2-5）：
/// <para>① <c>/contact/users/{unionId}</c> → Nick/Avatar/Mobile（三方手机号脱敏，完整号须申请权限）/OpenId/UnionId；</para>
/// <para>② <c>topapi/user/getbyunionid</c> → Userid（企业维度员工 id，<c>contact_type</c> 0=内部/1=外部）。</para>
/// <para>Userid 仅 DTO 增强字段（组织身份展示）——<b>不入联邦映射键、不进 <c>SsoChannelAuthResult</c></b>
/// （Oracle 评审 P1-4：external_uid 恒 = openId；userid 依赖企业内成员关系，随组织变动不稳定）。</para>
/// </summary>
/// <param name="OpenId">openId（应用维度——external_uid 恒 = openId）。</param>
/// <param name="UnionId">unionId（开发者企业账号维度——联盟锚点辅助，非 external_uid 主键）。</param>
/// <param name="Userid">userid（企业维度员工 id——仅 DTO 展示，经 getbyunionid 换取；未换取时 null）。</param>
/// <param name="Nick">昵称。</param>
/// <param name="Avatar">头像 URL。</param>
/// <param name="Mobile">手机号（三方脱敏，完整号须申请权限）。</param>
public sealed record DingTalkUserInfo(
    string? OpenId,
    string? UnionId,
    string? Userid,
    string? Nick,
    string? Avatar,
    string? Mobile);
