using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;

namespace TKWF.Ext.MFA;

/// <summary>
/// RFC 6238 TOTP 自研实现（internal static——零第三方 NuGet，ADR-MFA-TOTP自研与密钥存储 §决策1）。
/// <para>算法：HMAC-SHA1(secret, 大端序 8 字节计数器 = unixSeconds/timeStep) → 动态截断（HMAC 末字节低 4 位为偏移量，
/// 取 4 字节 → 31 位掩码 → mod 10^digits）→ 定长数字串（前导零补足）。正确性三锚（方案 §五）：RFC 6238 附录 B SHA1
/// 向量 + 时间步边界用例 + 运行时自洽交叉验证。</para>
/// <para>secret：16 字节加密随机 → RFC 4648 Base32 大写无 padding（Google Authenticator 兼容）；<see cref="Validate"/>
/// 时窗验证（当前步 ±skewWindows 任一步匹配，恒定时间比较防时序侧信道）。</para>
/// </summary>
internal static class TotpGenerator
{
    /// <summary>Base32 字母表（RFC 4648 大写）。</summary>
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>生成 16 字节随机 secret → Base32 编码（RFC 4648 大写、无 padding）。</summary>
    public static string GenerateSecret()
        => Base32Encode(RandomNumberGenerator.GetBytes(16));

    /// <summary>Base32 编码（RFC 4648 大写、无 padding——Google Authenticator 兼容）。</summary>
    public static string Base32Encode(byte[] data)
    {
        if (data is null) throw new ArgumentNullException(nameof(data));

        var sb = new StringBuilder((data.Length * 8 + 4) / 5);
        int buffer = 0, bits = 0;
        foreach (var b in data)
        {
            buffer = (buffer << 8) | b;
            bits += 8;
            while (bits >= 5)
            {
                sb.Append(Base32Alphabet[(buffer >> (bits - 5)) & 0x1F]);
                bits -= 5;
            }
        }
        if (bits > 0)
            sb.Append(Base32Alphabet[(buffer << (5 - bits)) & 0x1F]);
        return sb.ToString();
    }

    /// <summary>Base32 解码（容忍小写与尾部 padding——解码后尾部不足 1 字节的零头丢弃）。</summary>
    public static byte[] Base32Decode(string base32)
    {
        if (string.IsNullOrWhiteSpace(base32))
            throw new ArgumentException("base32 不能为空", nameof(base32));

        var clean = base32.Trim().TrimEnd('=');
        int buffer = 0, bits = 0;
        var bytes = new List<byte>(clean.Length * 5 / 8);
        foreach (var c in clean)
        {
            var index = Base32Alphabet.IndexOf(char.ToUpperInvariant(c));
            if (index < 0)
                throw new ArgumentException($"包含非 Base32 字符：'{c}'", nameof(base32));
            buffer = (buffer << 5) | index;
            bits += 5;
            if (bits >= 8)
            {
                bytes.Add((byte)((buffer >> (bits - 8)) & 0xFF));
                bits -= 8;
            }
        }
        return bytes.ToArray();
    }

    /// <summary>按当前时间步计算 TOTP 码（RFC 6238：计数器 counter = unixSeconds / timeStepSeconds）。</summary>
    public static string ComputeCode(string base32Secret, long unixSeconds, int timeStepSeconds, int digits)
    {
        if (string.IsNullOrWhiteSpace(base32Secret)) throw new ArgumentException("base32Secret 不能为空", nameof(base32Secret));
        if (timeStepSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(timeStepSeconds));
        if (digits is < 6 or > 10) throw new ArgumentOutOfRangeException(nameof(digits));
        return ComputeForCounter(base32Secret, unixSeconds / timeStepSeconds, digits);
    }

    /// <summary>按固定计数器计算 TOTP 码（动态截断核心——<see cref="Validate"/> 时窗验证共享）。</summary>
    private static string ComputeForCounter(string base32Secret, long counter, int digits)
    {
        var key = Base32Decode(base32Secret);

        // 计数器大端序编码（RFC 6238 §5.1——最高有效字节在前）
        var counterBytes = new byte[8];
        for (int i = 7; i >= 0; i--)
        {
            counterBytes[i] = (byte)(counter & 0xFF);
            counter >>= 8;
        }

        // HMAC-SHA1 → 动态截断（RFC 4226 §5.3：末字节低 4 位为偏移，取 4 字节 → 31 位掩码）
        var hash = HMACSHA1.HashData(key, counterBytes);
        var offset = hash[^1] & 0x0F;
        var binary = ((hash[offset] & 0x7F) << 24)
                   | ((hash[offset + 1] & 0xFF) << 16)
                   | ((hash[offset + 2] & 0xFF) << 8)
                   | (hash[offset + 3] & 0xFF);

        // mod 10^digits + 定长（前导零补足）
        var otp = (int)(binary % (long)Math.Pow(10, digits));
        return otp.ToString("D" + digits);
    }

    /// <summary>时窗验证——当前步 ± skewWindows 任一步匹配（恒定时间比较 <see cref="CryptographicOperations.FixedTimeEquals"/>）。</summary>
    public static bool Validate(string code, string base32Secret, long unixSeconds, int timeStepSeconds, int digits, int skewWindows)
    {
        if (string.IsNullOrWhiteSpace(code) || code.Length != digits)
            return false;
        if (string.IsNullOrWhiteSpace(base32Secret) || timeStepSeconds <= 0 || skewWindows < 0)
            return false;

        var currentStep = unixSeconds / timeStepSeconds;
        for (long step = currentStep - skewWindows; step <= currentStep + skewWindows; step++)
        {
            var expected = ComputeForCounter(base32Secret, step, digits);
            if (CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(code), Encoding.UTF8.GetBytes(expected)))
                return true;
        }
        return false;
    }

    /// <summary>构建 otpauth provisioning URI（供前端 QR——标签 "{issuer}:{accountName}"，issuer/accountName URL 编码）。</summary>
    public static string BuildProvisioningUri(string issuer, string accountName, string base32Secret, int digits, int timeStepSeconds)
    {
        if (string.IsNullOrWhiteSpace(issuer)) throw new ArgumentException("issuer 不能为空", nameof(issuer));
        if (string.IsNullOrWhiteSpace(accountName)) throw new ArgumentException("accountName 不能为空", nameof(accountName));
        if (string.IsNullOrWhiteSpace(base32Secret)) throw new ArgumentException("base32Secret 不能为空", nameof(base32Secret));

        var label = Uri.EscapeDataString(issuer) + ":" + Uri.EscapeDataString(accountName);
        return "otpauth://totp/" + label
            + "?secret=" + Uri.EscapeDataString(base32Secret)
            + "&issuer=" + Uri.EscapeDataString(issuer)
            + "&digits=" + digits
            + "&period=" + timeStepSeconds;
    }
}