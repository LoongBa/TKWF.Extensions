using System;
using System.Security.Cryptography;
using System.Text;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 兑换码生成器（内部静态——CSPRNG 高熵 + 脱敏 + SHA256 哈希）。
/// <para>格式：<c>EDU-XXXX-XXXX-XXXX</c>（12 位 base32 字符，去易混淆 <c>0O1lI</c>——熵约 30bit；
/// 频控 10 次/窗口下爆破概率极低）。明文生成时一次性返回（不落库不日志）；库中仅存哈希 + 脱敏值。</para>
/// </summary>
internal static class RedemptionCodeGenerator
{
    /// <summary>去易混淆字符的 base32 字母表（Crockford 变体）。</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    /// <summary>生成高熵兑换码（EDU-XXXX-XXXX-XXXX）。</summary>
    public static string Generate(string prefix = "EDU")
    {
        var random = RandomNumberGenerator.GetBytes(12);   // CSPRNG
        var sb = new StringBuilder(prefix.Length + 15);
        sb.Append(prefix).Append('-');
        for (var i = 0; i < 12; i++)
        {
            if (i is 4 or 8) sb.Append('-');
            sb.Append(Alphabet[random[i] % Alphabet.Length]);
        }
        return sb.ToString();
    }

    /// <summary>脱敏展示值——保留前 6 后 4，中间 *（如 EDU-AB12-****-EF56）。</summary>
    public static string Mask(string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return "";
        var cleaned = code.Trim();
        if (cleaned.Length <= 10) return new string('*', cleaned.Length);
        return cleaned[..6] + new string('*', cleaned.Length - 10) + cleaned[^4..];
    }

    /// <summary>SHA256 十六进制小写（64 字符）——CodeHash 存储（明文不落库）。</summary>
    public static string Hash(string code)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code))).ToLowerInvariant();
}
