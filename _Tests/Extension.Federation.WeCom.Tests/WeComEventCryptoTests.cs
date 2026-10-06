using System;
using System.Security.Cryptography;
using System.Text;
using TKWF.Federation.WeCom;

namespace TKWF.Federation.WeCom.Tests;

/// <summary>
/// 回调信任根测试（M4 方案 T7 验收 F3——msg_signature 验签 + AES 解密 + <b>ReceiveId 校验</b>正负路径，
/// 三道闸门一票否决 + URL 验证 echostr 握手原语 P1-6）。
/// <para>构造企业微信官方规范样本：msg_signature = SHA1(字典序排序 token,timestamp,nonce,msg_encrypt)；
/// AES-256-CBC 加密（EncodingAESKey 43 字符 Base64Decode 派生 32 字节密钥，IV=前 16 字节，PKCS7）；
/// 明文 = random(16) + msg_len(4 网络序) + XML + receiveid（corpid/suiteid）。</para>
/// </summary>
public class WeComEventCryptoTests
{
    private const string TestEncodingAESKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"; // 43 字符
    private const string TestToken = "test-token";
    private const string TestCorpId = "ww_test_corp";

    /// <summary>构造合法企业微信加密事件（msg_encrypt + 正确 msg_signature），receiveid 默认=TestCorpId。</summary>
    private static (string MsgEncrypt, string Timestamp, string Nonce, string Signature) BuildEncryptedEvent(
        string userId, string? tamperSignature = null, string receiveId = TestCorpId)
    {
        var aesKey = Convert.FromBase64String(TestEncodingAESKey + "=");
        var iv = aesKey.AsSpan(0, 16).ToArray();

        // 明文：random(16) + msg_len(4 网络序) + XML + receiveid
        var xml = $"<xml><ToUserName><![CDATA[{receiveId}]]></ToUserName><FromUserName><![CDATA[sys]]></FromUserName><CreateTime>1403610513</CreateTime><MsgType><![CDATA[event]]></MsgType><Event><![CDATA[change_contact]]></Event><ChangeType><![CDATA[update_user]]></ChangeType><UserID><![CDATA[{userId}]]></UserID></xml>";
        var xmlBytes = Encoding.UTF8.GetBytes(xml);
        var msgLen = BitConverter.GetBytes(xmlBytes.Length);
        Array.Reverse(msgLen); // 网络序大端
        var receiveIdBytes = Encoding.UTF8.GetBytes(receiveId);
        var plain = new byte[16 + 4 + xmlBytes.Length + receiveIdBytes.Length];
        RandomNumberGenerator.Fill(plain.AsSpan(0, 16));
        msgLen.CopyTo(plain, 16);
        xmlBytes.CopyTo(plain, 20);
        receiveIdBytes.CopyTo(plain, 20 + xmlBytes.Length);

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

        // msg_signature = SHA1(字典序排序 token,timestamp,nonce,msg_encrypt)
        var timestamp = "1403610513";
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
        var (msgEncrypt, timestamp, nonce, signature) = BuildEncryptedEvent("user-abc");
        Assert.True(WeComEventCrypto.VerifySignature(TestToken, timestamp, nonce, msgEncrypt, signature));
    }

    [Fact]
    public void VerifySignature_TamperedMsgEncrypt_Rejected()
    {
        var (msgEncrypt, timestamp, nonce, signature) = BuildEncryptedEvent("user-abc", "deadbeef");
        Assert.False(WeComEventCrypto.VerifySignature(TestToken, timestamp, nonce, msgEncrypt, signature));
    }

    [Fact]
    public void VerifySignature_WrongToken_Rejected()
    {
        var (msgEncrypt, timestamp, nonce, signature) = BuildEncryptedEvent("user-abc");
        Assert.False(WeComEventCrypto.VerifySignature("wrong-token", timestamp, nonce, msgEncrypt, signature));
    }

    [Fact]
    public void VerifySignature_MissingParams_Rejected()
    {
        Assert.False(WeComEventCrypto.VerifySignature("", "ts", "nonce", "enc", "sig"));
        Assert.False(WeComEventCrypto.VerifySignature("tok", "", "nonce", "enc", "sig"));
    }

    [Fact]
    public void DecryptAndVerify_Valid_ReturnsXmlWithUserId_AndReceiveIdMatches()
    {
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("user-xyz");
        var xml = WeComEventCrypto.DecryptAndVerify(TestEncodingAESKey, msgEncrypt, TestCorpId);
        Assert.Contains("UserID", xml);
        Assert.Contains("user-xyz", xml);
    }

    [Fact]
    public void DecryptAndVerify_ReceiveIdMismatch_Throws()
    {
        // Oracle 评审 P1-1 第二道闸门——明文 receiveid 与期望（corpid）不符 → CryptographicException（防跨企业混串）
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("user-abc", receiveId: "ww_forged_corp");
        Assert.Throws<CryptographicException>(() => WeComEventCrypto.DecryptAndVerify(TestEncodingAESKey, msgEncrypt, TestCorpId));
    }

    [Fact]
    public void DecryptAndVerify_ExpectedReceiveIdMissing_Throws()
    {
        // fail-closed：expectedReceiveId（corpid/suiteid）未配置 → 抛（防 receiveid 校验被静默跳过）
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("user-abc");
        Assert.Throws<CryptographicException>(() => WeComEventCrypto.DecryptAndVerify(TestEncodingAESKey, msgEncrypt, ""));
    }

    [Fact]
    public void VerifyAndDecryptEcho_Valid_ReturnsPlainText()
    {
        // URL 验证握手（Oracle 评审 P1-6）——echostr 验签 + 解密回显明文（1s 硬约束）
        var (echostr, timestamp, nonce, signature) = BuildEncryptedEvent("echo-plain-1");
        var plain = WeComEventCrypto.VerifyAndDecryptEcho(TestToken, timestamp, nonce, echostr, signature, TestEncodingAESKey, TestCorpId);
        Assert.NotNull(plain);
        Assert.Contains("echo-plain-1", plain); // 回显明文 = 解密后事件体（装配层原样回显）
    }

    [Fact]
    public void VerifyAndDecryptEcho_TamperedSignature_ReturnsNull()
    {
        // 验签失败 → null（装配层回显失败语义——不抛防 500 泄漏）
        var (echostr, timestamp, nonce, _) = BuildEncryptedEvent("echo-plain-2");
        var plain = WeComEventCrypto.VerifyAndDecryptEcho(TestToken, timestamp, nonce, echostr, "deadbeef", TestEncodingAESKey, TestCorpId);
        Assert.Null(plain);
    }

    [Fact]
    public void ExtractFromXml_UserId_ReturnsCompositeParts()
    {
        var xml = "<xml><UserID><![CDATA[usr_123]]></UserID></xml>";
        var (userId, openUserId) = WeComEventCrypto.ExtractFromXml(xml);
        Assert.Equal("usr_123", userId);
        Assert.Null(openUserId);
    }

    [Fact]
    public void ExtractFromXml_OpenUserId_ReturnsCompositeParts()
    {
        var xml = "<xml><OpenUserID><![CDATA[ou_1]]></OpenUserID></xml>";
        var (userId, openUserId) = WeComEventCrypto.ExtractFromXml(xml);
        Assert.Null(userId);
        Assert.Equal("ou_1", openUserId);
    }

    [Fact]
    public void ExtractFromXml_MissingIdentity_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => WeComEventCrypto.ExtractFromXml("<xml><ChangeType>update_user</ChangeType></xml>"));
        Assert.Throws<InvalidOperationException>(() => WeComEventCrypto.ExtractFromXml("not-xml"));
    }

    [Fact]
    public void DecryptAndVerify_InvalidKey_Throws()
    {
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("user-abc");
        // EncodingAESKey 非法（长度/Base64）→ CryptographicException（密码学失败语义）
        Assert.Throws<CryptographicException>(() => WeComEventCrypto.DecryptAndVerify("short-key", msgEncrypt, TestCorpId));
        Assert.Throws<CryptographicException>(() => WeComEventCrypto.DecryptAndVerify(new string('!', 43), msgEncrypt, TestCorpId));
    }

    [Fact]
    public void DecryptAndVerify_WrongKey_Fails()
    {
        // 用另一密钥加密 → 用错密钥解密 → 解析异常
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("user-abc");
        var wrongKey = TestEncodingAESKey == "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"
            ? "BCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk01234"  // 43 字符不同密钥
            : "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";
        Assert.ThrowsAny<Exception>(() => WeComEventCrypto.DecryptAndVerify(wrongKey, msgEncrypt, TestCorpId));
    }
}
