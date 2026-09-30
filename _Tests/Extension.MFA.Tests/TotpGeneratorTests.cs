using System;
using System.Linq;
using System.Text;
using TKWF.Ext.MFA;

namespace TKWF.Ext.MFA.Tests;

/// <summary>
/// RFC 6238 TOTP 自研实现单元测试（internal TotpGenerator——IVT 直测）。
/// <para>正确性三锚（方案 §五）：RFC 6238 附录 B SHA1 官方向量（8 位 → 6 位按规范 mod 10^6 截取）+
/// 时间步边界用例 + P11 运行时自洽交叉验证；恒定时间比较（错码返回 false 不抛）。</para>
/// </summary>
public class TotpGeneratorTests
{
    /// <summary>RFC 6238 附录 B 测试密钥 ASCII "12345678901234567890" 的 Base32 编码（RFC 4648 大写无 padding）。</summary>
    private const string Rfc6238SecretBase32 = "GEZDGNBVGY3TQOJQGEZDGNBVGY3TQOJQ";

    // ── RFC 6238 附录 B SHA1 官方向量（time=59 等 6 组；8 位期望值 → 6 位 = 8 位值 mod 10^6） ──

    public static TheoryData<long, string> Rfc6238Vectors()
        => new()
        {
            { 59L, "287082" },             // 8 位 94287082 mod 10^6 = 287082
            { 1111111109L, "081804" },     // 8 位 07081804（=7081804）mod 10^6 = 081804
            { 1111111111L, "050471" },     // 8 位 14050471 mod 10^6 = 050471
            { 1234567890L, "005924" },     // 8 位 89005924 mod 10^6 = 005924
            { 2000000000L, "279037" },     // 8 位 69279037 mod 10^6 = 279037
            { 20000000000L, "353130" },    // 8 位 65353130 mod 10^6 = 353130
        };

    [Theory]
    [MemberData(nameof(Rfc6238Vectors))]
    public void ComputeCode_Matches_Rfc6238_AppendixB_Sh1_Vectors(long unixSeconds, string expected6Digit)
    {
        // 计数器 = unixSeconds / timeStepSeconds（时间 59 → T=1）
        var code = TotpGenerator.ComputeCode(Rfc6238SecretBase32, unixSeconds, timeStepSeconds: 30, digits: 6);

        Assert.Equal(expected6Digit, code);
    }

    [Fact]
    public void Base32Encode_OfRfc6238AsciiKey_Matches_KnownBase32()
    {
        // RFC 6238 附录 B 密钥即 ASCII "12345678901234567890"（20 字节）——Base32 编码须为已知标准值
        var encoded = TotpGenerator.Base32Encode(Encoding.UTF8.GetBytes("12345678901234567890"));

        Assert.Equal(Rfc6238SecretBase32, encoded);
    }

    // ── Base32 secret 生成（16 字节随机 → 可逆） ──

    [Fact]
    public void GenerateSecret_Produces26CharBase32_DecodesTo16Bytes()
    {
        var secret = TotpGenerator.GenerateSecret();

        // 16 字节 → ceil(16*8/5)=26 个 Base32 字符，字母表 [A-Z2-7]（RFC 4648 大写、无 padding）
        Assert.Equal(26, secret.Length);
        Assert.All(secret, c => Assert.True(
            (c >= 'A' && c <= 'Z') || (c >= '2' && c <= '7'),
            $"字符 '{c}' 不在 Base32 字母表"));
        // Base32 可逆 → 解码回 16 字节
        Assert.Equal(16, TotpGenerator.Base32Decode(secret).Length);
    }

    [Fact]
    public void GenerateSecret_IsRandom_ProducesDifferentSecrets()
    {
        // 加密安全随机——连续两次生成不同（碰撞概率 2^-128 可忽略）
        var first = TotpGenerator.GenerateSecret();
        var second = TotpGenerator.GenerateSecret();

        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Base32Decode_ToleratesLowercaseAndPadding()
    {
        // 容忍小写 + 尾部 padding（解码后尾部不足 1 字节的零头丢弃——16 字节无零头）
        var bytes = TotpGenerator.Base32Decode(Rfc6238SecretBase32.ToLowerInvariant() + "======");

        Assert.Equal(20, bytes.Length);
    }

    // ── provisioning URI 格式 ──

    [Fact]
    public void BuildProvisioningUri_Format_OtpAuthLabelSecretParams()
    {
        var secret = TotpGenerator.GenerateSecret();
        var uri = TotpGenerator.BuildProvisioningUri("MyCo", "alice@example.com", secret, digits: 6, timeStepSeconds: 30);

        // 前缀 + 标签（issuer:accountName URL 编码）
        Assert.StartsWith("otpauth://totp/", uri);
        Assert.Contains("MyCo:alice%40example.com", uri);
        // 参数齐备
        Assert.Contains("?secret=" + secret, uri);
        Assert.Contains("&issuer=MyCo", uri);
        Assert.Contains("&digits=6", uri);
        Assert.Contains("&period=30", uri);
    }

    [Fact]
    public void BuildProvisioningUri_UrlEncodesIssuerAccountName()
    {
        var uri = TotpGenerator.BuildProvisioningUri("My Company", "a b@c.com", "JBSWY3DPEHPK3PXP", 6, 30);

        // 空格/@ 均 URL 编码（Uri.EscapeDataString）
        Assert.Contains("My%20Company:a%20b%40c.com", uri);
        Assert.Contains("&issuer=My%20Company", uri);
    }

    // ── 时间步边界（±1 窗口内通过、±2 失败） ──

    [Fact]
    public void Validate_TimeStepBoundary_SkewOne_AdjacentStepsPass_TwoStepsFail()
    {
        var secret = TotpGenerator.GenerateSecret();
        const int step = 30;
        const int digits = 6;
        const int skew = 1;
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var currentStep = now / step;

        // 当前步码 → 通过
        var currentCode = TotpGenerator.ComputeCode(secret, currentStep * step, step, digits);
        Assert.True(TotpGenerator.Validate(currentCode, secret, now, step, digits, skew));

        // ±1 步（时窗容差内）→ 通过
        var prevCode = TotpGenerator.ComputeCode(secret, (currentStep - 1) * step, step, digits);
        var nextCode = TotpGenerator.ComputeCode(secret, (currentStep + 1) * step, step, digits);
        Assert.True(TotpGenerator.Validate(prevCode, secret, now, step, digits, skew));
        Assert.True(TotpGenerator.Validate(nextCode, secret, now, step, digits, skew));

        // ±2 步（超出容差）→ 失败
        var prev2Code = TotpGenerator.ComputeCode(secret, (currentStep - 2) * step, step, digits);
        var next2Code = TotpGenerator.ComputeCode(secret, (currentStep + 2) * step, step, digits);
        Assert.False(TotpGenerator.Validate(prev2Code, secret, now, step, digits, skew));
        Assert.False(TotpGenerator.Validate(next2Code, secret, now, step, digits, skew));
    }

    // ── P11 运行时自洽交叉验证：生成 secret → 当前时间步算码 → 立即验证通过 ──

    [Fact]
    public void Runtime_GeneratedSecret_ComputeCurrentStep_ValidateImmediately_Passes()
    {
        var secret = TotpGenerator.GenerateSecret();
        var unixSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        var code = TotpGenerator.ComputeCode(secret, unixSeconds, 30, 6);

        Assert.True(TotpGenerator.Validate(code, secret, unixSeconds, 30, 6, skewWindows: 1));
    }

    // ── 恒定时间：错码返回 false（不抛） ──

    [Fact]
    public void Validate_WrongCode_ReturnsFalse_NoThrow()
    {
        var secret = TotpGenerator.GenerateSecret();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        // 恒定时间比较——错码不抛异常，统一 false（防时序侧信道）
        Assert.False(TotpGenerator.Validate("000000", secret, now, 30, 6, skewWindows: 1));
    }

    [Fact]
    public void Validate_EmptyOrWrongLengthCode_ReturnsFalse()
    {
        var secret = TotpGenerator.GenerateSecret();
        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Assert.False(TotpGenerator.Validate("", secret, now, 30, 6, 1));
        Assert.False(TotpGenerator.Validate("12345", secret, now, 30, 6, 1));   // 5 位
        Assert.False(TotpGenerator.Validate("1234567", secret, now, 30, 6, 1)); // 7 位
    }
}
