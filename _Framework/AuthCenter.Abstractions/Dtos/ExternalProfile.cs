using System;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部身份档案负载（D03 §5.2 表——可空全集，事件/oauth 授权拉取两种来源统一承载）。
/// <para><b>零凭据</b>：不含 Token/Secret 等任何凭据字段（对齐 ExternalIdentityDto 安全姿态）。</para>
/// <para>字段语义：<see cref="SubscribeStatus"/> 为订阅号关注状态（0=未知/已取关 1=关注中）——事件路径
/// 幂等合并关注域数据（I2 关注域在 Federation 侧，档案侧仅快照）；<see cref="Phone"/> 联系方式角色
/// （A.5 双角色——凭据锚点在 AuthFactor Sms 识别符，本字段仅档案展示）。</para>
/// </summary>
/// <param name="Nickname">昵称（可空——事件来源可缺）。</param>
/// <param name="AvatarUrl">头像 URL（可空）。</param>
/// <param name="Gender">性别（宽松自由文本，A.7）。</param>
/// <param name="City">城市（可空）。</param>
/// <param name="Province">省份（可空）。</param>
/// <param name="Country">国家（可空）。</param>
/// <param name="Language">语言（可空）。</param>
/// <param name="SubscribeStatus">关注状态（0/1，可空）。</param>
/// <param name="SubscribeTime">关注时刻（UTC，可空）。</param>
/// <param name="Phone">联系方式手机号（可空——非登录凭据）。</param>
public sealed record ExternalProfile(
    string? Nickname,
    string? AvatarUrl,
    string? Gender,
    string? City,
    string? Province,
    string? Country,
    string? Language,
    int? SubscribeStatus,
    DateTime? SubscribeTime,
    string? Phone);

/// <summary>
/// 档案来源（D03 §5.2 合并优先级：user_filled &gt; merchant_set(仅L3) &gt; oauth_userinfo &gt; event）。
/// <para>字符串常量类（开放——不枚举死；存储于 <c>UserProfileEntity.ProfileSource</c> 列）。</para>
/// </summary>
public static class ProfileSource
{
    /// <summary>用户自填（最高优先级——绝不被他源覆盖）。</summary>
    public const string UserFilled = "user_filled";

    /// <summary>商家维护（仅 L3 场景，本迭代预留）。</summary>
    public const string MerchantSet = "merchant_set";

    /// <summary>OAuth 授权拉取（userinfo 接口）。</summary>
    public const string OAuthUserInfo = "oauth_userinfo";

    /// <summary>事件路径（subscribe/scan 等）携带。</summary>
    public const string Event = "event";
}