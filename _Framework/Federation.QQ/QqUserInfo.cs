namespace TKWF.Federation.QQ;

/// <summary>
/// QQ 用户信息（get_user_info 响应裁剪 record）。
/// <para>⚠️ N1 骨架——N3 按 QQ 协议事实补充字段。QQ get_user_info 默认 scope 返回 nickname/figureurl 头像/
/// gender/province/city/year/星座（**无手机号无邮箱**，官方明示性别/省市非真实数据——N3 方案 §3.2）。</para>
/// </summary>
/// <param name="OpenId">openid（应用维度唯一——external_uid 稳定映射键，N3 P1-2）。</param>
/// <param name="Nickname">昵称。</param>
/// <param name="Avatar">头像 URL（figureurl）。</param>
public sealed record QqUserInfo(string OpenId, string? Nickname, string? Avatar);