using System;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 兑换历史 DTO——授权面「兑换历史」API 的返回载体（UserCenter 退役后归本扩展属主）。
/// <para><paramref name="CodeMasked"/> 为<b>已脱敏</b>兑换码——库中仅存脱敏值 + SHA256 哈希（明文不落库）。</para>
/// </summary>
/// <param name="CodeMasked">脱敏 code（本扩展保证脱敏——明文不落库）。</param>
/// <param name="ProductName">商品/载体名。</param>
/// <param name="TargetAppId">兑换目标应用 id。</param>
/// <param name="RedeemedAtUtc">兑换时间（UTC）。</param>
/// <param name="Status">状态（redeemed/expired...——授权面域，本扩展枚举化语义透传）。</param>
public sealed record RedemptionRecordDto(
    string CodeMasked,
    string? ProductName,
    string? TargetAppId,
    DateTime RedeemedAtUtc,
    string Status);

/// <summary>
/// 我的应用 DTO——授权面「我的应用」API 的返回载体（UserCenter 退役后归本扩展属主）。
/// <para><paramref name="UsageSummary"/> 契约约束：<b>不含学习明细</b>（儿童数据红线）。</para>
/// </summary>
/// <param name="AppId">应用/产品 id。</param>
/// <param name="AppName">应用名。</param>
/// <param name="IsAuthorized">当前授权状态。</param>
/// <param name="ExpiresAtUtc">授权到期（可空=永久/未定义）。</param>
/// <param name="UsageSummary">使用情况聚合文本/JSON（契约约束：不含学习明细——儿童数据红线）。</param>
public sealed record UserAppDto(
    string AppId,
    string? AppName,
    bool IsAuthorized,
    DateTime? ExpiresAtUtc,
    string? UsageSummary);