using System;

namespace TKWF.Ext.UserCenter;

/// <summary>
/// 用户中心配置（<c>TKWF:UserCenter</c> 节）。
/// <para>v0.1.0：PhoneMask 可配性留档（缺省内置规则——前 3 后 4）；本期无其他配置项。</para>
/// </summary>
public sealed class UserCenterOptions
{
    /// <summary>手机号脱敏保留位数（头/尾）。缺省内置规则：前 3 后 4。</summary>
    public int PhoneMaskHead { get; set; } = 3;

    /// <summary>手机号脱敏保留位数（尾）。缺省内置规则：前 3 后 4。</summary>
    public int PhoneMaskTail { get; set; } = 4;
}