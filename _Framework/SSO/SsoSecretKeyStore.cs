using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.SSO;

/// <summary>
/// SSO 应用凭证 AES-GCM 密钥持有者（内部静态——DataService 分部构造器固定 (IDomainUser, IEntityDAC) 两参，
/// 无法注入 <see cref="SsoOptions"/>；由 <see cref="SsoClientService"/> 构造时幂等初始化，加解密经
/// <see cref="Encrypt(string, byte[])"/>/<see cref="Decrypt(string, byte[])"/> 显式传 key）。
/// <para>镜像 <c>PlatformCredentialKeyStore</c> 模式（SSO 独立扩展不引 Authentication 主包——本扩展内建等价辅助）：
/// 密钥从 <c>SsoOptions.SecretEncryptionKeyPath</c> 读取（前 32 字节）；缺失时生产 fail-fast
/// （<see cref="InvalidOperationException"/>）/ 开发生成 32 随机字节 + 写文件 + Warning（对齐签名密钥策略）。</para>
/// <para>AES-GCM 输出 "base64(iv).base64(tag).base64(cipher)"（12 字节 nonce + 16 字节 tag）——
/// 密文落库（ClientSecretEncrypted/HmacSecretEncrypted），明文不落库。</para>
/// </summary>
internal static class SsoSecretKeyStore
{
    private static byte[]? _key;
    private static readonly object Gate = new();

    /// <summary>幂等初始化（SsoClientService 构造时调用；线程安全单例）。</summary>
    public static void Initialize(SsoOptions options, ILogger logger)
    {
        if (_key != null) return;
        lock (Gate)
        {
            if (_key != null) return;

            var keyPath = options.SecretEncryptionKeyPath;
            if (string.IsNullOrWhiteSpace(keyPath))
            {
                if (options.IsProduction)
                    throw new InvalidOperationException("SsoOptions.SecretEncryptionKeyPath 未配置——生产环境必须提供 AES-GCM 密钥文件（前 32 字节）");
                logger.LogWarning("SsoOptions.SecretEncryptionKeyPath 未配置——开发环境生成临时密钥（不落盘，重启即变，仅限开发）");
                _key = RandomNumberGenerator.GetBytes(32);
                return;
            }

            if (File.Exists(keyPath))
            {
                var bytes = File.ReadAllBytes(keyPath);
                if (bytes.Length < 32)
                    throw new InvalidOperationException($"密钥文件 {keyPath} 长度 {bytes.Length} < 32 字节——AES-GCM 需要 ≥32 字节密钥");
                _key = bytes.AsSpan(0, 32).ToArray();
            }
            else
            {
                if (options.IsProduction)
                    throw new InvalidOperationException($"密钥文件 {keyPath} 不存在——生产环境必须预置 AES-GCM 密钥文件（前 32 字节）");
                logger.LogWarning("密钥文件 {KeyPath} 不存在——开发环境生成并写入 32 随机字节", keyPath);
                _key = RandomNumberGenerator.GetBytes(32);
                Directory.CreateDirectory(Path.GetDirectoryName(keyPath)!);
                File.WriteAllBytes(keyPath, _key);
            }
        }
    }

    /// <summary>取密钥（须先 Initialize——否则 InvalidOperationException）。</summary>
    public static byte[] GetKey()
        => _key ?? throw new InvalidOperationException("SsoSecretKeyStore 未初始化——请先经 SsoClientService 构造（Initialize）");

    /// <summary>
    /// 测试隔离钩子（对齐 V0.5.3 ADR-Authentication-DevRsaKeyCache 契约"静态缓存必须有 Reset 钩子"）——
    /// 清空密钥并覆盖敏感字节（防托管残留）。测试 setup/teardown 调用；生产路径零接触。
    /// </summary>
    public static void ResetForTests()
    {
        lock (Gate)
        {
            if (_key != null)
            {
                Array.Clear(_key);
                _key = null;
            }
        }
    }

    /// <summary>AES-GCM 加密：输出 "base64(iv).base64(tag).base64(cipher)"（12 字节 nonce + 16 字节 tag）。</summary>
    public static string Encrypt(string plaintext, byte[] key)
    {
        var iv = RandomNumberGenerator.GetBytes(12);
        var plainBytes = Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(iv, plainBytes, cipher, tag);
        return Convert.ToBase64String(iv) + "." + Convert.ToBase64String(tag) + "." + Convert.ToBase64String(cipher);
    }

    /// <summary>AES-GCM 解密：解析 "base64(iv).base64(tag).base64(cipher)"；格式非法/解密失败抛 CryptographicException。</summary>
    public static string Decrypt(string value, byte[] key)
    {
        var parts = value.Split('.');
        if (parts.Length != 3)
            throw new CryptographicException("SSO 密文格式非法——期望 base64(iv).base64(tag).base64(cipher)");
        var iv = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var cipher = Convert.FromBase64String(parts[2]);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(iv, cipher, tag, plain);
        return Encoding.UTF8.GetString(plain);
    }
}