using System;

namespace TKWF.Ext.UserCenter;

/// <summary>
/// 手机号脱敏器——用户中心门面输出<b>强制脱敏</b>的单一真相源。
/// <para>规则（§5.4）：保留前 3 后 4，中间 <c>*</c>；长度 ≤7 → 全 <c>*</c>；null/空白 → null；含空格先 Trim。</para>
/// <para>实现方（认证中心/装配实例）不需要也不得自行脱敏——门面统一处理，防漏。</para>
/// </summary>
public static class PhoneMasker
{
    /// <summary>脱敏：<c>13812345678</c> → <c>138****5678</c>；<c>12345</c>（≤7 位）→ <c>*****</c>；null/空白 → null；先 Trim。</summary>
    public static string? Mask(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone))
            return null;

        var trimmed = phone.Trim();

        // 长度 ≤7：全 *（保留位数不足，直接隐藏）
        if (trimmed.Length <= 7)
            return new string('*', trimmed.Length);

        // 保留前 3 后 4，中间 *（标准 11 位手机号 → 138****5678；更长号码同样适用）
        const int keepHead = 3;
        const int keepTail = 4;
        return string.Concat(
            trimmed.AsSpan(0, keepHead),
            new string('*', trimmed.Length - keepHead - keepTail),
            trimmed.AsSpan(trimmed.Length - keepTail));
    }
}