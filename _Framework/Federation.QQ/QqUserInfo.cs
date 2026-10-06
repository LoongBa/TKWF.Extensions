namespace TKWF.Federation.QQ;

/// <summary>
/// QQ 用户信息（get_user_info 响应裁剪 record，N3 T2）。
/// <para>QQ get_user_info 默认 scope 返回 nickname/figureurl 头像/gender/province/city/year/星座/黄钻
/// （**无手机号无邮箱**，官方明示性别/省市非真实数据——N3 方案 §3.2）。头像取 <c>figureurl_qq_1</c>（40×40 QQ 头像
/// 官方文档明示一定存在），缺省回退 <c>figureurl</c>（30×30 空间头像）。</para>
/// </summary>
/// <param name="OpenId">openid（应用维度唯一——external_uid 稳定映射键，N3 P1-2）。</param>
/// <param name="Nickname">昵称。</param>
/// <param name="Avatar">头像 URL（figureurl_qq_1 优先，缺省回退 figureurl）。</param>
/// <param name="Gender">性别（"男"/"女"——官方明示非真实数据）。</param>
/// <param name="Province">省（官方明示非真实数据）。</param>
/// <param name="City">市（官方明示非真实数据）。</param>
public sealed record QqUserInfo(string OpenId, string? Nickname, string? Avatar, string? Gender, string? Province, string? City);

/// <summary>
/// QQ <c>/oauth2.0/me</c> 换取结果（N3 P1-2）。
/// <para><see cref="OpenId"/> 恒返回（应用维度唯一——external_uid 稳定映射键）；<see cref="UnionId"/> 仅在
/// <see cref="QqChannelConfig.EnableUnionId"/> 开启时请求并返回（联盟锚点写入辅助，未开启恒 null）。
/// 注意：<c>/me</c> 端点默认 JSONP 响应（<c>callback(...)</c>）——<see cref="QqApiClient.GetMeAsync"/> 恒传 fmt=json。</para>
/// </summary>
/// <param name="OpenId">openid（应用维度唯一）。</param>
/// <param name="UnionId">unionid（EnableUnionId 开启时返回，否则 null）。</param>
public sealed record QqMeResult(string OpenId, string? UnionId);