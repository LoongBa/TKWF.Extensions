using System;

namespace TKWF.Ext.UserCenter;

/// <summary>
/// 公共档案 DTO——用户中心公共 Profile API 的返回载体。
/// <para>⚠️ <paramref name="Phone"/> 为<b>原始</b>手机号（仅源/门面内部流转）——
/// <c>IUserCenterQueryService.GetProfileAsync</c> 输出前经 PhoneMasker <b>强制脱敏</b>。
/// 源实现方/门面均不得将含原始 Phone 的 DTO、异常消息、异常栈写日志或序列化（安全约束 C1）。</para>
/// </summary>
/// <param name="UserId">平台内部 id（JWT sub / DomainUser 标识语义）。</param>
/// <param name="Phone">⚠️ 原始手机号（门面输出前强制脱敏；源/门面流转约束见类注释）。</param>
/// <param name="IsWechatBound">微信绑定状态（双形态任一即 true）。</param>
/// <param name="Nickname">昵称。</param>
/// <param name="AvatarUrl">头像 URL string（DMP 实践——非二进制）。</param>
/// <param name="IsTeacherVerified">teacher_verified 认证声明（不含业务权限）。</param>
/// <param name="AuthLevel">认证强度（1=手机号 / 2=微信便捷 / 3=教师核实）。</param>
public sealed record UserProfileDto(
    string UserId,
    string? Phone,
    bool IsWechatBound,
    string? Nickname,
    string? AvatarUrl,
    bool IsTeacherVerified,
    int AuthLevel);

/// <summary>
/// 兑换历史 DTO——用户中心「兑换历史」API 的返回载体。
/// <para><paramref name="CodeMasked"/> 为<b>已脱敏</b>兑换码——契约约束：实现方（授权面/装配实例）
/// 输出前自行脱敏（授权面已脱敏存储，UserCenter 不持有/不存储明文，仅透传）。</para>
/// </summary>
/// <param name="CodeMasked">脱敏 code（契约约束——实现方输出前脱敏，UserCenter 透传不存储）。</param>
/// <param name="ProductName">商品/载体名（授权面填充）。</param>
/// <param name="TargetAppId">兑换目标应用 id。</param>
/// <param name="RedeemedAtUtc">兑换时间（UTC）。</param>
/// <param name="Status">状态（redeemed/expired...——授权面域，UserCenter 透传不枚举化）。</param>
public sealed record RedemptionRecordDto(
    string CodeMasked,
    string? ProductName,
    string? TargetAppId,
    DateTime RedeemedAtUtc,
    string Status);

/// <summary>
/// 我的应用 DTO——用户中心「我的应用」API 的返回载体。
/// <para><paramref name="UsageSummary"/> 契约约束：<b>不含学习明细</b>（儿童数据红线——授权面保证）。</para>
/// </summary>
/// <param name="AppId">应用/产品 id。</param>
/// <param name="AppName">应用名。</param>
/// <param name="IsAuthorized">当前授权状态。</param>
/// <param name="ExpiresAtUtc">授权到期（可空=永久/未定义）。</param>
/// <param name="UsageSummary">使用情况聚合文本/JSON（契约约束：不含学习明细——儿童数据红线，授权面保证）。</param>
public sealed record UserAppDto(
    string AppId,
    string? AppName,
    bool IsAuthorized,
    DateTime? ExpiresAtUtc,
    string? UsageSummary);