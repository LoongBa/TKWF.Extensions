using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace TKWF.Ext.SmsCode.Tests;

/// <summary>
/// 验证码引擎原语测试（方案 §4.2 + §7 引擎行）——internal 引擎经生产 csproj IVT（TKWF.Ext.SmsCode.Tests）直测。
/// <para>覆盖：GenerateCode（6 位/纯数字/跨调用差异/自定义长度）、HashCode（SHA256 hex 64 小写/确定性/值差异）、
/// VerifyHash（正确真/错误假/大小写敏感——存储散列恒小写 hex，大写输入不匹配）。</para>
/// </summary>
public class VerificationCodeEngineTests
{
    // ── GenerateCode ────────────────────────────────────────────────

    [Fact]
    public void GenerateCode_DefaultLength_SixDigits()
    {
        var code = VerificationCodeEngine.GenerateCode();

        Assert.NotNull(code);
        Assert.Equal(6, code.Length);
        Assert.True(Regex.IsMatch(code, @"^\d{6}$"), $"生成码应全为 6 位数字，实际：{code}");
    }

    [Fact]
    public void GenerateCode_AllCharsAreDigits_Statistical()
    {
        // 统计面：200 个码全数字（纯随机出界符号的概率为 0）
        for (var i = 0; i < 200; i++)
        {
            var code = VerificationCodeEngine.GenerateCode();
            Assert.True(code.All(char.IsDigit), $"非数字字符：{code}");
        }
    }

    [Fact]
    public void GenerateCode_VariesAcrossCalls()
    {
        // 加密安全随机——200 次调用中碰撞显著少于全同（碰撞概率 ~1e-6 量级）
        var codes = Enumerable.Range(0, 200).Select(_ => VerificationCodeEngine.GenerateCode()).ToArray();
        var distinct = codes.Distinct().Count();

        Assert.True(distinct > 190, $"200 次调用应高度互异，实际去重后 {distinct} 个");
    }

    [Theory]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public void GenerateCode_RespectsCustomLength(int length)
    {
        var code = VerificationCodeEngine.GenerateCode(length);

        Assert.Equal(length, code.Length);
        Assert.True(Regex.IsMatch(code, $"^\\d{{{length}}}$"), $"生成码应全为 {length} 位数字，实际：{code}");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(16)]
    public void GenerateCode_OutOfRangeLength_Throws(int length)
    {
        // P2 审核修复：10⁹ 超出 int.MaxValue——显式 ArgumentOutOfRangeException（防 Math.Pow 溢出异常）
        Assert.Throws<ArgumentOutOfRangeException>(() => VerificationCodeEngine.GenerateCode(length));
    }

    // ── HashCode ────────────────────────────────────────────────────

    [Fact]
    public void HashCode_Sha256Hex_64CharsLowercase()
    {
        var hash = VerificationCodeEngine.HashCode("123456");

        Assert.Equal(64, hash.Length);
        Assert.True(Regex.IsMatch(hash, @"^[0-9a-f]{64}$"), $"应为 64 位小写 hex，实际：{hash}");
        Assert.Equal(hash, hash.ToLowerInvariant());
    }

    [Fact]
    public void HashCode_Deterministic()
    {
        var a = VerificationCodeEngine.HashCode("000000");
        var b = VerificationCodeEngine.HashCode("000000");
        Assert.Equal(a, b);
    }

    [Fact]
    public void HashCode_DifferentForDifferentValues()
    {
        var a = VerificationCodeEngine.HashCode("000000");
        var b = VerificationCodeEngine.HashCode("000001");
        Assert.NotEqual(a, b);
    }

    // ── VerifyHash ──────────────────────────────────────────────────

    [Fact]
    public void VerifyHash_CorrectCode_True()
    {
        const string code = "123456";
        var storedHash = VerificationCodeEngine.HashCode(code);
        Assert.True(VerificationCodeEngine.VerifyHash(code, storedHash));
    }

    [Fact]
    public void VerifyHash_WrongCode_False()
    {
        var storedHash = VerificationCodeEngine.HashCode("123456");
        Assert.False(VerificationCodeEngine.VerifyHash("123457", storedHash));
    }

    [Fact]
    public void VerifyHash_UpperCaseHashInput_False()
    {
        // 存储散列恒小写 hex（HashCode 契约）——大写输入经 FixedTimeEquals 字节比对不匹配（防时序侧信道路径不变）
        var storedHash = VerificationCodeEngine.HashCode("654321").ToUpperInvariant();
        Assert.False(VerificationCodeEngine.VerifyHash("654321", storedHash));
    }

    [Fact]
    public void VerifyHash_WrongLengthInput_False()
    {
        var storedHash = VerificationCodeEngine.HashCode("123456");
        Assert.False(VerificationCodeEngine.VerifyHash("12345", storedHash));
        Assert.False(VerificationCodeEngine.VerifyHash("", storedHash));
    }
}