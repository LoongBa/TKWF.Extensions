using System;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部身份映射 DTO（不可变 record，V0.11.0 Wave 1b——方案 §4.10 D6）——<b>不承载任何凭据字段</b>
/// （SecretHash/MustChange 绝不出契约包）。
/// </summary>
/// <param name="UId">平台内部 id（token2 <c>sub</c>）。</param>
/// <param name="ChannelId">channel 实例 id（微信 = 公众号 id）。</param>
/// <param name="ExternalUserId">渠道身份标识（微信 = openid2）。</param>
/// <param name="LinkedAt">绑定时刻（UTC）。</param>
public sealed record ExternalIdentityDto(
    string UId,
    string ChannelId,
    string ExternalUserId,
    DateTime LinkedAt);
