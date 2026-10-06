using System;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace TKWF.Federation.{Namespace};

/// <summary>
/// {平台}消息加解密辅助（**信任根一票否决——有入站回调平台**）。
/// <para>事件推送：<c>msg_signature</c> 验签 + AES-256-CBC 解密取 <c>FromUserName</c>(external_uid)。
/// 漏验 = 攻击者可伪造任意 external_uid = 整个认证体系崩溃。</para>
/// <para>⚠️ 平台差异：验签算法（SHA1 字典序拼接 / RSA-SHA256 / 双签名）与解密算法（AES-256-CBC / RSA）
/// ——本模板以微信家族形态（msg_signature = SHA1 字典序排序拼接 + AES-256-CBC，EncodingAESKey 派生密钥）为基准；
/// 追加闸门先例：WeCom ReceiveId=corpid/suiteid 校验（防跨企业回调混串，Oracle P1-1）。</para>
/// <para>internal：仅本库通道 + 测试（InternalsVisibleTo）消费。</para>
/// </summary>
internal static class {Platform}EventCrypto
{
    /// <summary>
    /// 计算 <c>msg_signature = SHA1(sort(token, timestamp, nonce, msg_encrypt))</c>（十六进制小写）。
    /// <para>排序 = 平台官方 DictionarySort（逐字符字典序 + 长度 tiebreak）——输入均 ASCII，
    /// <see cref="StringComparer.Ordinal"/> 等价。</para>
    /// </summary>
    public static string ComputeSignature(string token, string timestamp, string nonce, string msgEncrypt)
    {
        var sorted = new[] { token, timestamp, nonce, msgEncrypt };
        Array.Sort(sorted, StringComparer.Ordinal);
        var raw = string.Concat(sorted);
        return Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(raw))).ToLowerInvariant();
    }

    /// <summary>
    /// 校验事件推送签名（msg_signature）——信任根一票否决。
    /// <para>恒定时间比对（防时序攻击——签名比较属机密比较）；签名长度不符直接 false（不抛——防 500 泄漏）。</para>
    /// </summary>
    public static bool VerifySignature(string token, string timestamp, string nonce, string msgEncrypt, string msgSignature)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce)
            || string.IsNullOrEmpty(msgEncrypt) || string.IsNullOrEmpty(msgSignature))
            return false;

        var expected = ComputeSignature(token, timestamp, nonce, msgEncrypt);
        var actual = msgSignature.ToLowerInvariant();
        if (expected.Length != actual.Length)
            return false; // 长度不符——不抛（FixedTimeEquals 对异长抛 ArgumentException，此处防 500）

        // 恒定时间比对（防时序攻击——签名比较属机密比较）
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));
    }

    /// <summary>
    /// AES-256-CBC 解密 msg_encrypt → 原始 XML（含 FromUserName=external_uid）。
    /// <para>失败抛 <see cref="CryptographicException"/>（密钥非法 / base64 非法 / 填充损坏）。</para>
    /// </summary>
    public static string DecryptMsg(string encodingAESKey, string msgEncrypt)
    {
        if (string.IsNullOrEmpty(encodingAESKey) || encodingAESKey.Length != 43)
            throw new CryptographicException($"{PlatformName} EncodingAESKey 无效（须 43 字符）");
        if (string.IsNullOrEmpty(msgEncrypt))
            throw new CryptographicException($"{PlatformName} msg_encrypt 为空");

        // AESKey = Base64Decode(EncodingAESKey + "=")——解密密钥即 Base64 解码结果（平台官方）
        byte[] aesKey;
        try
        {
            aesKey = Convert.FromBase64String(encodingAESKey + "=");
        }
        catch (FormatException ex)
        {
            throw new CryptographicException($"{PlatformName} AESKey 派生失败（EncodingAESKey Base64 非法）", ex);
        }
        if (aesKey.Length != 32)
            throw new CryptographicException($"{PlatformName} AESKey 派生失败（须 32 字节）");

        var iv = aesKey.AsSpan(0, 16).ToArray();
        byte[] cipher;
        try
        {
            cipher = Convert.FromBase64String(msgEncrypt);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException($"{PlatformName} msg_encrypt base64 解码失败", ex);
        }

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] plain;
        using (var decryptor = aes.CreateDecryptor())
            plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        // 明文 = random(16) + msg_len(4 网络序大端) + msg + receiveid
        if (plain.Length < 20)
            throw new CryptographicException($"{PlatformName} 消息解密结果长度非法");
        var msgLenBytes = plain.AsSpan(16, 4);
        var msgLen = (msgLenBytes[0] << 24) | (msgLenBytes[1] << 16) | (msgLenBytes[2] << 8) | msgLenBytes[3];
        if (msgLen <= 0 || 20 + msgLen > plain.Length)
            throw new CryptographicException($"{PlatformName} 消息解密结果 msg_len 非法");

        return Encoding.UTF8.GetString(plain, 20, msgLen);
    }

    /// <summary>
    /// 从解密 XML 提取 FromUserName（external_uid）——XML 解析（CDATA 感知），非法 XML / 缺节点抛
    /// <see cref="InvalidOperationException"/>。
    /// </summary>
    public static string ExtractFromUserName(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidOperationException($"{PlatformName} 消息 XML 为空");

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException($"{PlatformName} 消息 XML 非法", ex);
        }

        var value = doc.Root?.Element("FromUserName")?.Value;
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"{PlatformName} 消息 XML 缺 FromUserName");
        return value.Trim();
    }

    /// <summary>
    /// 测试辅助：AES-256-CBC 加密（构造合法 msg_encrypt 正向样本）——与 <see cref="DecryptMsg"/> 严格对称。
    /// <para>明文布局同官方加密侧：<c>[16B 随机][4B 网络序消息长度][XML][receiveid]</c> + PKCS7。</para>
    /// </summary>
    public static string EncryptMsg(string xml, string encodingAESKey, string receiveId)
    {
        if (string.IsNullOrEmpty(encodingAESKey) || encodingAESKey.Length != 43)
            throw new CryptographicException($"{PlatformName} EncodingAESKey 无效（须 43 字符）");

        var aesKey = Convert.FromBase64String(encodingAESKey + "=");
        if (aesKey.Length != 32)
            throw new CryptographicException($"{PlatformName} AESKey 派生失败（须 32 字节）");
        var iv = aesKey.AsSpan(0, 16).ToArray();

        var random = RandomNumberGenerator.GetBytes(16);
        var xmlBytes = Encoding.UTF8.GetBytes(xml);
        var msgLen = (uint)xmlBytes.Length;
        var lenBytes = new byte[4];
        lenBytes[0] = (byte)(msgLen >> 24);
        lenBytes[1] = (byte)(msgLen >> 16);
        lenBytes[2] = (byte)(msgLen >> 8);
        lenBytes[3] = (byte)msgLen;
        var receiveIdBytes = Encoding.UTF8.GetBytes(receiveId);

        var plain = new byte[random.Length + lenBytes.Length + xmlBytes.Length + receiveIdBytes.Length];
        Buffer.BlockCopy(random, 0, plain, 0, random.Length);
        Buffer.BlockCopy(lenBytes, 0, plain, random.Length, lenBytes.Length);
        Buffer.BlockCopy(xmlBytes, 0, plain, random.Length + lenBytes.Length, xmlBytes.Length);
        Buffer.BlockCopy(receiveIdBytes, 0, plain, random.Length + lenBytes.Length + xmlBytes.Length, receiveIdBytes.Length);

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] output;
        using (var encryptor = aes.CreateEncryptor())
            output = encryptor.TransformFinalBlock(plain, 0, plain.Length);
        return Convert.ToBase64String(output);
    }

    private static string PlatformName => "{Namespace}";
}
