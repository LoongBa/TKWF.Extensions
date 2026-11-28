using System;
using System.Collections.Generic;
using System.IO;
using TKW.Framework.Utility.Cryptography;
using Xunit;

namespace TKWF.Federation.Alipay.Tests;

/// <summary>
/// 支付宝同步响应验签 sign_type 剔除回归网（代码审核 2026-11-23 P1 项 5）：
/// 976c5df 让 <see cref="AlipaySignService.VerifyResponse"/> 剥离顶层 sign_type（对齐官方
/// rsaCheckV1/rsaCheckV2）。本测试锁定三种情形：
/// ① 顶层含 sign_type + 正确签名 → 验过；
/// ② 顶层含 sign_type + 篡改签名 → 验败；
/// ③ 签名串<b>包含</b> sign_type → 验败（证明确实剔除了 sign_type）。
/// </summary>
public class AlipaySignTypeVerificationTests
{
    private const string Wrapper = "alipay_system_oauth_token_response";
    private const string Inner = "{\"code\":\"10000\",\"user_id\":\"2088000000000000\"}";

    private static string BuildBody(string sign)
        => $"{{\"{Wrapper}\":{Inner},\"sign_type\":\"RSA2\",\"sign\":\"{sign}\"}}";

    [Fact]
    public void VerifyResponse_WithTopLevelSignType_ValidSignature_ReturnsTrue()
    {
        var sign = AlipaySignUtil.BuildSignature(
            new Dictionary<string, string>(StringComparer.Ordinal) { [Wrapper] = Inner },
            AlipayTestHost.PrivateKeyPem);

        Assert.True(new AlipaySignService().VerifyResponse(
            BuildBody(sign), sign, AlipayTestHost.PublicKeyPem));
    }

    [Fact]
    public void VerifyResponse_WithTopLevelSignType_TamperedSignature_ReturnsFalse()
    {
        // 错误私钥签名 → 验败（负路径 fail-closed）
        var sign = AlipaySignUtil.BuildSignature(
            new Dictionary<string, string>(StringComparer.Ordinal) { [Wrapper] = Inner },
            File.ReadAllText(AlipayTestHost.WrongPrivateKeyPath));

        Assert.False(new AlipaySignService().VerifyResponse(
            BuildBody(sign), sign, AlipayTestHost.PublicKeyPem));
    }

    [Fact]
    public void VerifyResponse_SignTypeIncludedInSignedString_ReturnsFalse_ProvesExcluded()
    {
        // 若 VerifyResponse 未剔除 sign_type，此签名将验过；剔除后必败——锁定 sign_type 不参与签名串
        var sign = AlipaySignUtil.BuildSignature(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                [Wrapper] = Inner,
                ["sign_type"] = "RSA2"
            },
            AlipayTestHost.PrivateKeyPem);

        Assert.False(new AlipaySignService().VerifyResponse(
            BuildBody(sign), sign, AlipayTestHost.PublicKeyPem));
    }
}
