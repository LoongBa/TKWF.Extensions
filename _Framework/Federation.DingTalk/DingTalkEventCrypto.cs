using System;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TKWF.Federation.DingTalk;

/// <summary>
/// 钉钉事件订阅回调加解密辅助（**信任根一票否决——M3 方案 §3.4**）。
/// <para>事件订阅推送（安全模式）：<c>signature = sha1(字典序排序拼接 token, timestamp, nonce, msg_encrypt)</c>
/// 验签 + AES-256-CBC 解密取事件体（JSON——<c>FromUserId</c>）。漏验 = 攻击者可伪造任意 unionId/openId =
/// 整个认证体系崩溃。</para>
/// <para>钉钉官方规范（对齐 WeChatEventCrypto 同构算法 + 钉钉增量——Oracle 评审 P1-1/P1-6）：</para>
/// <para>① <b>验签</b>：<c>signature = SHA1(字典序排序拼接 token, timestamp, nonce, msg_encrypt)</c>——
/// 四参数按字符串字典序升序排序后直接拼接，SHA1 十六进制小写比对（恒定时间）。</para>
/// <para>② <b>AES 解密</b>：<c>AESKey = Base64Decode(EncodingAESKey + "=")</c>（32 字节）；AES-256-CBC，
/// IV = AESKey 前 16 字节，PKCS7 填充；解密明文 = <c>random(16) + msg_len(4 网络序) + msg(JSON) + receiveid(corpId)</c>。</para>
/// <para>③ <b>receiveid 校验（第二道闸门——P1-1）</b>：解密后提取明文尾部 receiveid 与期望
/// expectedReceiveId（= <see cref="DingTalkChannelConfig.CorpId"/>）恒定时间比对，不符抛
/// <see cref="CryptographicException"/>——forged org-change event 可授予企业访问权，敏感度高于微信消息事件。</para>
/// <para>internal：仅本库两通道 + 测试（InternalsVisibleTo）消费。Stream Mode（官方建议新应用替代 Webhook）
/// 解密与 Webhook 共用本 crypto 基元（M3 方案 §五可见性注记——归装配层另立项）。</para>
/// </summary>
internal static class DingTalkEventCrypto
{
    /// <summary>
    /// 计算 <c>signature = SHA1(sort(token, timestamp, nonce, msg_encrypt))</c>（十六进制小写）。
    /// <para>排序 = 官方 DictionarySort（逐字符字典序 + 长度 tiebreak）——输入均 ASCII，
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
    /// 校验事件推送签名（signature）——信任根一票否决。
    /// <para>恒定时间比对（防时序攻击——签名比较属机密比较）；签名长度不符直接 false（不抛——防 500 泄漏）。</para>
    /// </summary>
    /// <param name="token">应用 Token（DingTalkChannelConfig.Token——钉钉后台安全设置）。</param>
    /// <param name="timestamp">钉钉推送参数 timestamp。</param>
    /// <param name="nonce">钉钉推送参数 nonce。</param>
    /// <param name="msgEncrypt">钉钉推送参数 msg_encrypt（密文）。</param>
    /// <param name="signature">钉钉推送参数 signature（待校验签名）。</param>
    public static bool VerifySignature(string token, string timestamp, string nonce, string msgEncrypt, string signature)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce)
            || string.IsNullOrEmpty(msgEncrypt) || string.IsNullOrEmpty(signature))
            return false;

        var expected = ComputeSignature(token, timestamp, nonce, msgEncrypt);
        var actual = signature.ToLowerInvariant();
        if (expected.Length != actual.Length)
            return false; // 长度不符——不抛（FixedTimeEquals 对异长抛 ArgumentException，此处防 500）

        // 恒定时间比对（防时序攻击——签名比较属机密比较）
        return CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(expected), Encoding.ASCII.GetBytes(actual));
    }

    /// <summary>
    /// AES-256-CBC 解密 msg_encrypt → 事件体 JSON，并校验 receiveid（Oracle 评审 P1-1 第二道闸门）。
    /// <para>失败抛 <see cref="CryptographicException"/>（EncodingAESKey 非法 / base64 非法 / 填充损坏 /
    /// <b>receiveid 与 <paramref name="expectedReceiveId"/> 不匹配</b>——fail-closed：expectedReceiveId 为空亦抛）。</para>
    /// </summary>
    /// <param name="encodingAESKey">应用 EncodingAESKey（43 字符）。</param>
    /// <param name="msgEncrypt">钉钉推送参数 msg_encrypt（Base64 AES 密文）。</param>
    /// <param name="expectedReceiveId">期望 receiveid（= <see cref="DingTalkChannelConfig.CorpId"/>——恒定时间比对，不符拒）。</param>
    /// <returns>解密后事件体（JSON 字符串，含 <c>FromUserId</c>）。</returns>
    public static string DecryptMsg(string encodingAESKey, string msgEncrypt, string expectedReceiveId)
    {
        if (string.IsNullOrEmpty(encodingAESKey) || encodingAESKey.Length != 43)
            throw new CryptographicException("钉钉 EncodingAESKey 无效（须 43 字符）");
        if (string.IsNullOrEmpty(msgEncrypt))
            throw new CryptographicException("钉钉 msg_encrypt 为空");
        if (string.IsNullOrEmpty(expectedReceiveId))
            throw new CryptographicException("钉钉 receiveid 校验失败：expectedReceiveId（CorpId）未配置");

        // AESKey = Base64Decode(EncodingAESKey + "=")——解密密钥即 Base64 解码结果（钉钉官方）
        byte[] aesKey;
        try
        {
            aesKey = Convert.FromBase64String(encodingAESKey + "=");
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("钉钉 AESKey 派生失败（EncodingAESKey Base64 非法）", ex);
        }
        if (aesKey.Length != 32)
            throw new CryptographicException("钉钉 AESKey 派生失败（须 32 字节）");

        var iv = aesKey.AsSpan(0, 16).ToArray();
        byte[] cipher;
        try
        {
            cipher = Convert.FromBase64String(msgEncrypt);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("钉钉 msg_encrypt base64 解码失败", ex);
        }

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] plain;
        using (var decryptor = aes.CreateDecryptor())
            plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        // 明文 = random(16) + msg_len(4 网络序大端) + msg(JSON) + receiveid(corpId)（Oracle 评审 P1-6 布局）
        if (plain.Length < 20)
            throw new CryptographicException("钉钉事件解密结果长度非法");
        var msgLen = (plain[16] << 24) | (plain[17] << 16) | (plain[18] << 8) | plain[19];
        if (msgLen <= 0 || 20 + msgLen > plain.Length)
            throw new CryptographicException("钉钉事件解密结果 msg_len 非法");

        // 提取明文尾部 receiveid 与 expectedReceiveId 恒定时间比对（P1-1 第二道闸门——不符拒）
        var receiveIdLen = plain.Length - 20 - msgLen;
        var receiveId = Encoding.UTF8.GetString(plain, 20 + msgLen, receiveIdLen);
        if (receiveId.Length != expectedReceiveId.Length
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(receiveId), Encoding.UTF8.GetBytes(expectedReceiveId)))
            throw new CryptographicException("钉钉事件 receiveid 与 CorpId 不匹配（拒绝——防 forged org-change event）");

        return Encoding.UTF8.GetString(plain, 20, msgLen);
    }

    /// <summary>
    /// 从解密事件体 JSON 提取 FromUserId（= openId，应用维度——钉钉扫码/事件推送事件体，Oracle 评审 P1-6）。
    /// <para>非法 JSON / 缺 FromUserId 抛 <see cref="InvalidOperationException"/>。</para>
    /// </summary>
    public static string ExtractFromUserId(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            throw new InvalidOperationException("钉钉事件体 JSON 为空");

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException("钉钉事件体 JSON 非法", ex);
        }

        using (doc)
        {
            var root = doc.RootElement;
            if (!root.TryGetProperty("FromUserId", out var value) || string.IsNullOrWhiteSpace(value.GetString()))
                throw new InvalidOperationException("钉钉事件体缺 FromUserId");
            return value.GetString()!.Trim();
        }
    }

    /// <summary>
    /// 测试辅助：AES-256-CBC 加密（构造合法 msg_encrypt 正向样本）——与 <see cref="DecryptMsg"/> 严格对称。
    /// <para>明文布局同官方加密侧：<c>[16B 随机][4B 网络序消息长度][JSON][receiveid]</c> + PKCS7。</para>
    /// </summary>
    public static string EncryptMsg(string json, string encodingAESKey, string receiveId)
    {
        if (string.IsNullOrEmpty(encodingAESKey) || encodingAESKey.Length != 43)
            throw new CryptographicException("钉钉 EncodingAESKey 无效（须 43 字符）");

        var aesKey = Convert.FromBase64String(encodingAESKey + "=");
        if (aesKey.Length != 32)
            throw new CryptographicException("钉钉 AESKey 派生失败（须 32 字节）");
        var iv = aesKey.AsSpan(0, 16).ToArray();

        var random = RandomNumberGenerator.GetBytes(16);
        var jsonBytes = Encoding.UTF8.GetBytes(json);
        var msgLen = (uint)jsonBytes.Length;
        var lenBytes = new byte[4];
        lenBytes[0] = (byte)(msgLen >> 24);
        lenBytes[1] = (byte)(msgLen >> 16);
        lenBytes[2] = (byte)(msgLen >> 8);
        lenBytes[3] = (byte)msgLen;
        var receiveIdBytes = Encoding.UTF8.GetBytes(receiveId);

        var plain = new byte[random.Length + lenBytes.Length + jsonBytes.Length + receiveIdBytes.Length];
        Buffer.BlockCopy(random, 0, plain, 0, random.Length);
        Buffer.BlockCopy(lenBytes, 0, plain, random.Length, lenBytes.Length);
        Buffer.BlockCopy(jsonBytes, 0, plain, random.Length + lenBytes.Length, jsonBytes.Length);
        Buffer.BlockCopy(receiveIdBytes, 0, plain, random.Length + lenBytes.Length + jsonBytes.Length, receiveIdBytes.Length);

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
}
