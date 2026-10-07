namespace TKWF.Federation.Alipay;

/// <summary>
/// 支付宝用户信息（<c>alipay.user.info.share</c> 响应裁剪 record，N4 T2）。
/// <para>协议事实（librarian 官方文档核实）：响应业务体 <c>alipay_user_info_share_response</c> 为 snake_case 字段——
/// user_id（16 位 2088 开头）/ open_id（应用维度，启用 openid 配置后返回）/ avatar / province / city /
/// nick_name / gender（F|M）。默认无手机号（须企业认证 + 挂"获取会员信息"功能包 + 申请 mobile 字段——
/// <see cref="AlipayChannelConfig.EnableMobile"/> 可选增强开关，N4 §3.4）；scope=auth_base 调用本接口必报
/// <c>aop.invalid-auth-token</c>（仅身份场景不调此接口——单独调用方负责 scope=auth_user 保证）。</para>
/// </summary>
/// <param name="UserId">user_id（应用维度唯一——external_uid 稳定映射键，N4 §二；open_id 灰度过渡兼容）。</param>
/// <param name="OpenId">open_id（启用 openid 配置后返回，否则 null）。</param>
/// <param name="Nickname">昵称（snake_case <c>nick_name</c> 映射）。</param>
/// <param name="Avatar">头像 URL。</param>
/// <param name="Province">省。</param>
/// <param name="City">市。</param>
/// <param name="Gender">性别（F=女 / M=男）。</param>
public sealed record AlipayUserInfo(
    string UserId,
    string? OpenId,
    string? Nickname,
    string? Avatar,
    string? Province,
    string? City,
    string? Gender);

/// <summary>
/// 支付宝 <c>alipay.system.oauth.token</c> 换取结果（N4 T2——code→user_id(open_id)/access_token 一次性换取）。
/// <para>⚠️ 用户级 token 不缓存不续期（对齐 QQ N3 P1-5 原则）：联邦认证面一次换取即用；refresh_token 长续期归
/// L7 非认证面（YAGNI，<see cref="AlipayApiClient.RefreshAccessTokenAsync"/> 方法保留供未来）。</para>
/// </summary>
/// <param name="UserId">user_id（16 位 2088 开头——external_uid 稳定映射键）。</param>
/// <param name="OpenId">open_id（启用 openid 配置后返回，否则 null）。</param>
/// <param name="AccessToken">access_token（40 位）。</param>
/// <param name="ExpiresInSeconds">access_token 有效期（秒——官方返回 expires_in）。</param>
/// <param name="RefreshToken">refresh_token（续期用；本库认证面不消费）。</param>
public sealed record AlipayTokenResult(
    string UserId,
    string? OpenId,
    string AccessToken,
    int ExpiresInSeconds,
    string? RefreshToken);
