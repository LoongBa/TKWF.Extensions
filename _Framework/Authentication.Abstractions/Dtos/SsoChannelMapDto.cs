namespace TKWF.Ext.Authentication;

/// <summary>
/// SSO 渠道身份映射 DTO（不可变 record）——<c>(channel_id, external_user_id) → uid</c> 联邦身份映射消费面。
/// </summary>
/// <param name="UId">平台内部 id（token2 <c>sub</c>）。</param>
/// <param name="ChannelId">channel 实例 id（微信 = 公众号 id）。</param>
/// <param name="ExternalUserId">渠道身份标识（微信 = openid2）。</param>
public sealed record SsoChannelMapDto(
    string UId,
    string ChannelId,
    string ExternalUserId);
