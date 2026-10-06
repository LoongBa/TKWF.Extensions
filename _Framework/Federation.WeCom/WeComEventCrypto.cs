using System;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace TKWF.Federation.WeCom;

/// <summary>
/// 企业微信通讯录/事件回调加解密辅助（**信任根一票否决——M4 方案 §3.4，对齐微信同家族**）。
/// <para>回调（安全模式）：<c>msg_signature = sha1(字典序排序拼接 token, timestamp, nonce, msg_encrypt)</c>
/// 验签 + AES-256-CBC 解密取事件体（XML）。漏验 = 攻击者可伪造任意员工身份 = 体系崩溃。</para>
/// <para>企业微信官方规范（对齐 WeChatEventCrypto 同家族算法 + 增量——Oracle 评审 P1-1）：</para>
/// <para>① <b>验签</b>：<c>msg_signature = SHA1(字典序排序拼接 token, timestamp, nonce, msg_encrypt)</c>——
/// 四参数按字符串字典序升序排序后直接拼接，SHA1 十六进制小写比对（恒定时间）。</para>
/// <para>② <b>AES 解密</b>：<c>AESKey = Base64Decode(EncodingAESKey + "=")</c>（32 字节）；AES-256-CBC，
/// IV = AESKey 前 16 字节，PKCS7 填充；解密明文 = <c>random(16) + msg_len(4 网络序) + msg(XML) + receiveid</c>。</para>
/// <para>③ <b>ReceiveId 校验（增量——Oracle 评审 P1-1）</b>：WeChat 先例 DecryptMsg 只取 msg 丢弃 receiveid；
/// 本实现提取 <c>plain[20+msgLen..]</c> 与期望（自建 = corpid / 三方 = suiteid）恒定时间比对，不符抛
/// <see cref="CryptographicException"/>——防跨企业回调混串。</para>
/// <para>④ <b>URL 验证握手（Oracle 评审 P1-6）</b>：<see cref="VerifyAndDecryptEcho"/>——回调 URL 验证
/// （GET 带 echostr AES 密文）验签 + 解密回显明文（1s 硬约束）——归装配层端点握手，非 EventChannel 业务路径。</para>
/// <para>internal：仅本库两通道 + 测试（InternalsVisibleTo）消费。三平台同构验签基元（WeChat/WeCom/DingTalk）
/// 提炼待办登记（M4 方案 P2-1）——待第 4 个同算法平台落地后评估，YAGNI 暂不实施。</para>
/// </summary>
internal static class WeComEventCrypto
{
    /// <summary>
    /// 计算 <c>msg_signature = SHA1(sort(token, timestamp, nonce, msg_encrypt))</c>（十六进制小写）。
    /// <para>排序 = 微信官方 DictionarySort（逐字符字典序 + 长度 tiebreak）——输入均 ASCII，
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
    /// 校验回调签名（msg_signature）——信任根一票否决。
    /// <para>恒定时间比对（防时序攻击——签名比较属机密比较）；签名长度不符直接 false（不抛——防 500 泄漏）。</para>
    /// </summary>
    /// <param name="token">应用 Token（WeComChannelConfig.Token——企业微信后台安全设置）。</param>
    /// <param name="timestamp">推送参数 timestamp。</param>
    /// <param name="nonce">推送参数 nonce。</param>
    /// <param name="msgEncrypt">推送参数 msg_encrypt（密文）。</param>
    /// <param name="msgSignature">推送参数 msg_signature（待校验签名）。</param>
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
    /// AES-256-CBC 解密 + <b>ReceiveId 校验一体化</b>（Oracle 评审 P1-1）→ 事件体 XML。
    /// <para>失败抛 <see cref="CryptographicException"/>（EncodingAESKey 非法 / base64 非法 / 填充损坏 /
    /// <b>receiveid 与 <paramref name="expectedReceiveId"/> 不匹配</b>——fail-closed：expectedReceiveId 为空亦抛）。</para>
    /// </summary>
    /// <param name="encodingAESKey">应用 EncodingAESKey（43 字符）。</param>
    /// <param name="msgEncrypt">推送参数 msg_encrypt（Base64 AES 密文）。</param>
    /// <param name="expectedReceiveId">期望 receiveid（自建 = corpid / 三方 = suiteid——恒定时间比对，不符拒）。</param>
    /// <returns>解密后事件体（XML 字符串）。</returns>
    public static string DecryptAndVerify(string encodingAESKey, string msgEncrypt, string expectedReceiveId)
    {
        if (string.IsNullOrEmpty(encodingAESKey) || encodingAESKey.Length != 43)
            throw new CryptographicException("企业微信 EncodingAESKey 无效（须 43 字符）");
        if (string.IsNullOrEmpty(msgEncrypt))
            throw new CryptographicException("企业微信 msg_encrypt 为空");
        if (string.IsNullOrEmpty(expectedReceiveId))
            throw new CryptographicException("企业微信 receiveid 校验失败：expectedReceiveId（corpid/suiteid）未配置");

        // AESKey = Base64Decode(EncodingAESKey + "=")——解密密钥即 Base64 解码结果（微信官方）
        byte[] aesKey;
        try
        {
            aesKey = Convert.FromBase64String(encodingAESKey + "=");
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("企业微信 AESKey 派生失败（EncodingAESKey Base64 非法）", ex);
        }
        if (aesKey.Length != 32)
            throw new CryptographicException("企业微信 AESKey 派生失败（须 32 字节）");

        var iv = aesKey.AsSpan(0, 16).ToArray();
        byte[] cipher;
        try
        {
            cipher = Convert.FromBase64String(msgEncrypt);
        }
        catch (FormatException ex)
        {
            throw new CryptographicException("企业微信 msg_encrypt base64 解码失败", ex);
        }

        using var aes = Aes.Create();
        aes.Key = aesKey;
        aes.IV = iv;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;

        byte[] plain;
        using (var decryptor = aes.CreateDecryptor())
            plain = decryptor.TransformFinalBlock(cipher, 0, cipher.Length);

        // 明文 = random(16) + msg_len(4 网络序大端) + msg(XML) + receiveid（Oracle 评审 P1-1 增量——提取尾部比对）
        if (plain.Length < 20)
            throw new CryptographicException("企业微信回调解密结果长度非法");
        var msgLen = (plain[16] << 24) | (plain[17] << 16) | (plain[18] << 8) | plain[19];
        if (msgLen <= 0 || 20 + msgLen > plain.Length)
            throw new CryptographicException("企业微信回调解密结果 msg_len 非法");

        // 提取明文尾部 receiveid 与期望（corpid/suiteid）恒定时间比对（P1-1——防跨企业回调混串）
        var receiveIdLen = plain.Length - 20 - msgLen;
        var receiveId = Encoding.UTF8.GetString(plain, 20 + msgLen, receiveIdLen);
        if (receiveId.Length != expectedReceiveId.Length
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(receiveId), Encoding.UTF8.GetBytes(expectedReceiveId)))
            throw new CryptographicException("企业微信回调 receiveid 与 corpid/suiteid 不匹配（拒绝——防跨企业混串）");

        return Encoding.UTF8.GetString(plain, 20, msgLen);
    }

    /// <summary>
    /// URL 验证握手原语（Oracle 评审 P1-6）——回调 URL 验证（GET 带 echostr AES 密文）验签 + 解密回显明文
    /// （1s 硬约束，归装配层端点握手）。
    /// <para>验签覆盖 echostr（msg_signature 排序参数 = token/timestamp/nonce/echostr）；验签失败返回 false
    /// （装配层回显失败语义），解密/校验失败抛 <see cref="CryptographicException"/>。</para>
    /// </summary>
    /// <param name="token">应用 Token。</param>
    /// <param name="timestamp">URL 验证参数 timestamp。</param>
    /// <param name="nonce">URL 验证参数 nonce。</param>
    /// <param name="echostr">URL 验证参数 echostr（AES 密文——企业微信后台保存校验时生成）。</param>
    /// <param name="msgSignature">URL 验证参数 msg_signature。</param>
    /// <param name="encodingAESKey">应用 EncodingAESKey（43 字符）。</param>
    /// <param name="expectedReceiveId">期望 receiveid（自建 corpid / 三方 suiteid）。</param>
    /// <returns>解密后明文 echostr（装配层回显）；验签失败返回 null。</returns>
    public static string? VerifyAndDecryptEcho(string token, string timestamp, string nonce, string echostr, string msgSignature, string encodingAESKey, string expectedReceiveId)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce)
            || string.IsNullOrEmpty(echostr) || string.IsNullOrEmpty(msgSignature))
            return null;

        if (!VerifySignature(token, timestamp, nonce, echostr, msgSignature))
            return null; // 信任根一票否决——验签失败拒绝

        return DecryptAndVerify(encodingAESKey, echostr, expectedReceiveId);
    }

    /// <summary>
    /// 从解密事件体 XML 提取身份标识（通讯录/事件回调——<c>ChangeType</c> 场景含 UserID/OpenUserID）。
    /// <para>非法 XML / 缺两标识抛 <see cref="InvalidOperationException"/>。</para>
    /// </summary>
    /// <returns>(UserId, OpenUserId)——自建成员 userid / 三方成员 open_userid（可能其一为空）。</returns>
    public static (string? UserId, string? OpenUserId) ExtractFromXml(string xml)
    {
        if (string.IsNullOrWhiteSpace(xml))
            throw new InvalidOperationException("企业微信回调 XML 为空");

        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException ex)
        {
            throw new InvalidOperationException("企业微信回调 XML 非法", ex);
        }

        var userId = doc.Root?.Element("UserID")?.Value;
        var openUserId = doc.Root?.Element("OpenUserID")?.Value;
        if (string.IsNullOrWhiteSpace(userId) && string.IsNullOrWhiteSpace(openUserId))
            throw new InvalidOperationException("企业微信回调缺 UserID/OpenUserID");
        return (string.IsNullOrWhiteSpace(userId) ? null : userId.Trim(),
                string.IsNullOrWhiteSpace(openUserId) ? null : openUserId.Trim());
    }

    /// <summary>
    /// 测试辅助：AES-256-CBC 加密（构造合法 msg_encrypt 正向样本）——与 <see cref="DecryptAndVerify"/> 严格对称。
    /// <para>明文布局同官方加密侧：<c>[16B 随机][4B 网络序消息长度][XML][receiveid]</c> + PKCS7。</para>
    /// </summary>
    public static string EncryptMsg(string xml, string encodingAESKey, string receiveId)
    {
        if (string.IsNullOrEmpty(encodingAESKey) || encodingAESKey.Length != 43)
            throw new CryptographicException("企业微信 EncodingAESKey 无效（须 43 字符）");

        var aesKey = Convert.FromBase64String(encodingAESKey + "=");
        if (aesKey.Length != 32)
            throw new CryptographicException("企业微信 AESKey 派生失败（须 32 字节）");
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
}
