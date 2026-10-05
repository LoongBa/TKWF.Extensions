using System;
using System.Security.Cryptography;
using System.Text;
using TKWF.Federation.WeChat;

namespace TKWF.Federation.WeChat.Tests;

/// <summary>
/// 事件推送信任根测试（T5 验收 W3——msg_signature 验签 + AES 解密正负路径，设计文档 §8.1 一票否决）。
/// <para>构造微信官方规范样本：msg_signature = SHA1(字典序排序 token,timestamp,nonce,msg_encrypt)；
/// AES-256-CBC 加密（EncodingAESKey 43 字符 Base64Decode 派生 32 字节密钥，IV=前 16 字节，PKCS7）；
/// 明文 = random(16) + msg_len(4 网络序) + xml(含 FromUserName) + receiveId。</para>
/// </summary>
public class WeChatEventCryptoTests
{
    private const string TestEncodingAESKey = "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"; // 43 字符
    private const string TestToken = "test-token";

    /// <summary>构造合法微信加密消息（msg_encrypt + 正确 msg_signature）。</summary>
    private static (string MsgEncrypt, string Timestamp, string Nonce, string Signature) BuildEncryptedEvent(
        string openId, string? tamperSignature = null)
    {
        var aesKey = Convert.FromBase64String(TestEncodingAESKey + "=");
        var iv = aesKey.AsSpan(0, 16).ToArray();

        // 明文：random(16) + msg_len(4 网络序) + xml + receiveId
        var xml = $"<xml><ToUserName><![CDATA[gh_test]]></ToUserName><FromUserName><![CDATA[{openId}]]></FromUserName><CreateTime>1234567890</CreateTime><MsgType><![CDATA[event]]></MsgType><Event><![CDATA[CLICK]]></Event></xml>";
        var xmlBytes = Encoding.UTF8.GetBytes(xml);
        var msgLen = BitConverter.GetBytes(xmlBytes.Length);
        Array.Reverse(msgLen); // 网络序大端
        var plain = new byte[16 + 4 + xmlBytes.Length + "receiveid".Length];
        RandomNumberGenerator.Fill(plain.AsSpan(0, 16));
        msgLen.CopyTo(plain, 16);
        xmlBytes.CopyTo(plain, 20);
        Encoding.UTF8.GetBytes("receiveid").CopyTo(plain, 20 + xmlBytes.Length);

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
        Assert.True(WeChatEventCrypto.VerifySignature(TestToken, timestamp, nonce, msgEncrypt, signature));
    }

    [Fact]
    public void VerifySignature_TamperedMsgEncrypt_Rejected()
    {
        var (msgEncrypt, timestamp, nonce, signature) = BuildEncryptedEvent("openid-abc", "deadbeef");
        Assert.False(WeChatEventCrypto.VerifySignature(TestToken, timestamp, nonce, msgEncrypt, signature));
    }

    [Fact]
    public void VerifySignature_WrongToken_Rejected()
    {
        var (msgEncrypt, timestamp, nonce, signature) = BuildEncryptedEvent("openid-abc");
        Assert.False(WeChatEventCrypto.VerifySignature("wrong-token", timestamp, nonce, msgEncrypt, signature));
    }

    [Fact]
    public void VerifySignature_MissingParams_Rejected()
    {
        Assert.False(WeChatEventCrypto.VerifySignature("", "ts", "nonce", "enc", "sig"));
        Assert.False(WeChatEventCrypto.VerifySignature("tok", "", "nonce", "enc", "sig"));
    }

    [Fact]
    public void DecryptMsg_Valid_ReturnsXmlWithFromUserName()
    {
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("openid-xyz");
        var xml = WeChatEventCrypto.DecryptMsg(TestEncodingAESKey, msgEncrypt);
        Assert.Contains("<FromUserName>", xml);
        Assert.Contains("openid-xyz", xml);
    }

    [Fact]
    public void ExtractFromUserName_FromDecryptedXml()
    {
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("openid-mp1");
        var xml = WeChatEventCrypto.DecryptMsg(TestEncodingAESKey, msgEncrypt);
        var openId = WeChatEventCrypto.ExtractFromUserName(xml);
        Assert.Equal("openid-mp1", openId);
    }

    [Fact]
    public void DecryptMsg_InvalidKey_Throws()
    {
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("openid-abc");
        // EncodingAESKey 非法（长度/Base64）→ CryptographicException（密码学失败语义）
        Assert.Throws<CryptographicException>(() => WeChatEventCrypto.DecryptMsg("short-key", msgEncrypt));
        Assert.Throws<CryptographicException>(() => WeChatEventCrypto.DecryptMsg(new string('!', 43), msgEncrypt));
    }

    [Fact]
    public void DecryptMsg_WrongKey_Fails()
    {
        // 用另一密钥加密 → 用错密钥解密 → 解析异常
        var (msgEncrypt, _, _, _) = BuildEncryptedEvent("openid-abc");
        var wrongKey = TestEncodingAESKey == "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG"
            ? "BCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijk01234"  // 43 字符不同密钥
            : "abcdefghijklmnopqrstuvwxyz0123456789ABCDEFG";
        Assert.ThrowsAny<Exception>(() => WeChatEventCrypto.DecryptMsg(wrongKey, msgEncrypt));
    }
}