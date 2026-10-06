using System;
using System.Security.Cryptography;
using System.Text;
using TKWF.Federation.DingTalk;

namespace TKWF.Federation.DingTalk.Tests;

/// <summary>
/// 事件推送信任根测试（M3 方案 T7 验收 F3——signature 验签 + AES 解密 + <b>receiveid=corpId 校验</b>正负路径，
/// 三道闸门一票否决）。
/// <para>构造钉钉官方规范样本：signature = SHA1(字典序排序 token,timestamp,nonce,msg_encrypt)；
/// AES-256-CBC 加密（EncodingAESKey 43 字符 Base64Decode 派生 32 字节密钥，IV=前 16 字节，PKCS7）；
/// 明文 = random(16) + msg_len(4 网络序) + msg(JSON) + receiveid(corpId)。</para>
/// </summary>
public class DingTalkEventCryptoTests
{
    private const string TestEncodingAESKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"; // 43 字符
    private const string TestToken = "test-token";
    private const string TestCorpId = "ding_test_corp";

    /// <summary>构造合法钉钉加密事件（msg_encrypt + 正确 signature），receiveid 默认=TestCorpId。</summary>
    private static (string MsgEncrypt, string Timestamp, string Nonce, string Signature) BuildEncryptedEvent(
        string openId, string? tamperSignature = null, string receiveId = TestCorpId)
    {
        var aesKey = Convert.FromBase64String(TestEncodingAESKey + "=");
        var iv = aesKey.AsSpan(0, 16).ToArray();

        // 明文：random(16) + msg_len(4 网络序) + JSON + receiveid(corpId)（Oracle 评审 P1-6 布局）
        var json = $"{{\"EventType\":\"SCAN\",\"FromUserId\":\"{openId}\",\"CorpId\":\"{receiveId}\",\"CreateAt\":1720000000}}";
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var msgLen = BitConverter.GetBytes(jsonBytes.Length);
        Array.Reverse(msgLen); // 网络序大端
        var receiveIdBytes = Encoding.UTF8.GetBytes(receiveId);
        var plain = new byte[16 + 4 + jsonBytes.Length + receiveIdBytes.Length];
        RandomNumberGenerator.Fill(plain.AsSpan(0, 16));
        msgLen.CopyTo(plain, 16);
        jsonBytes.CopyTo(plain, 20);
        receiveIdBytes.CopyTo(plain, 20 + jsonBytes.Length);

        // AES-256-CBC 加密（PKCS7）
        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        string msgEncrypt;
        using (var encryptor = aes.CreateEncryptor())
        {
            var cipher = encryptor.TransformFinalBlock(plain, 0, plain.Length);
            msgEncrypt = Convert.ToBase64String(cipher);
        }

        // signature = SHA1(字典序排序 token,timestamp,nonce,msg_encrypt)
        var timestamp = "1234567890";
        var nonce = "test-nonce";
        var sorted = new[] { TestToken, timestamp, nonce, msgEncrypt };
        Array.Sort(sorted, StringComparer.Ordinal);
        var raw = string.Concat(sorted);
        var signature = Convert.ToHexStringLower(SHA1.HashData(Encoding.UTF8.GetBytes(raw)));

        return (msgEncrypt, timestamp, nonce, tamperSignature ?? signature);
    }

    [Fact]
    public void VerifySignature_Valid_Succeeds()
    {
        var (msgEncrypt, timestamp, nonce, signature) = BuildEncryptedEvent("openid-abc");
        Assert.True(DingTalkEventCrypto.VerifySignature(TestToken, timestamp, nonce, msgEncrypt, signature));
    }

    [Fact]
    public void VerifySignature_TamperedMsgEncrypt_Rejected()
    {
        var (msgEncrypt, timestamp, nonce, signature) = BuildEncryptedEvent("openid-abc", "deadbeef");
        Assert.False(DingTalkEventCrypto.VerifySignature(TestToken, timestamp, nonce, msgEncrypt, signature));
    }

    [Fact]
    public void VerifySignature_WrongToken_Rejected()
    {
        var (msgEncrypt, timestamp, nonce, signature) = BuildEncryptedEvent("openid-abc");
        Assert.False(DingTalkEventCrypto.VerifySignature("wrong-token", timestamp, nonce, msgEncrypt, signature));
    }

    [Fact]
    public void VerifySignature_MissingParams_Rejected()
    {
        Assert.False(DingTalkEventCrypto.VerifySignature("", "ts", "nonce", "enc", "sig"));
        Assert.False(DingTalkEventCrypto.VerifySignature("tok", "", "nonce", "enc", "sig"));
    }

    [Fact]
    public void DecryptMsg_Valid_ReturnsJsonWithFromUserId_AndReceiveIdMatches()
    {
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("openid-xyz");
        var json = DingTalkEventCrypto.DecryptMsg(TestEncodingAESKey, msgEncrypt, TestCorpId);
        Assert.Contains("FromUserId", json);
        Assert.Contains("openid-xyz", json);
    }

    [Fact]
    public void DecryptMsg_ReceiveIdMismatch_Throws()
    {
        // Oracle 评审 P1-1 第二道闸门——明文 receiveid 与期望（corpId）不符 → CryptographicException（拒 forged org-change event）
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("openid-abc", receiveId: "ding_forged_corp");
        Assert.Throws<CryptographicException>(() => DingTalkEventCrypto.DecryptMsg(TestEncodingAESKey, msgEncrypt, TestCorpId));
    }

    [Fact]
    public void DecryptMsg_ExpectedReceiveIdMissing_Throws()
    {
        // fail-closed：expectedReceiveId（CorpId）未配置 → 抛（防 receiveid 校验被静默跳过）
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("openid-abc");
        Assert.Throws<CryptographicException>(() => DingTalkEventCrypto.DecryptMsg(TestEncodingAESKey, msgEncrypt, ""));
    }

    [Fact]
    public void ExtractFromUserId_FromDecryptedJson()
    {
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("openid-mp1");
        var json = DingTalkEventCrypto.DecryptMsg(TestEncodingAESKey, msgEncrypt, TestCorpId);
        var fromUserId = DingTalkEventCrypto.ExtractFromUserId(json);
        Assert.Equal("openid-mp1", fromUserId);
    }

    [Fact]
    public void ExtractFromUserId_MissingProperty_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => DingTalkEventCrypto.ExtractFromUserId("{\"EventType\":\"SCAN\"}"));
        Assert.Throws<InvalidOperationException>(() => DingTalkEventCrypto.ExtractFromUserId("not-json"));
    }

    [Fact]
    public void DecryptMsg_InvalidKey_Throws()
    {
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("openid-abc");
        // EncodingAESKey 非法（长度/Base64）→ CryptographicException（密码学失败语义）
        Assert.Throws<CryptographicException>(() => DingTalkEventCrypto.DecryptMsg("short-key", msgEncrypt, TestCorpId));
        Assert.Throws<CryptographicException>(() => DingTalkEventCrypto.DecryptMsg(new string('!', 43), msgEncrypt, TestCorpId));
    }

    [Fact]
    public void DecryptMsg_WrongKey_Fails()
    {
        // 用另一密钥加密 → 用错密钥解密 → 解析异常
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("openid-abc");
        var wrongKey = TestEncodingAESKey == "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"
            ? "BCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk01234"  // 43 字符不同密钥
            : "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";
        Assert.ThrowsAny<Exception>(() => DingTalkEventCrypto.DecryptMsg(wrongKey, msgEncrypt, TestCorpId));
    }
}
