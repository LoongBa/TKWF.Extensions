using System;
using System.Security.Cryptography;
using System.IO;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 平台凭证 AES-GCM 密钥持有者（内部静态——DataService 分部构造器固定 (IDomainUser, IEntityDAC) 两参，
/// 无法注入 Options；由 PlatformCredentialService 构造时幂等初始化，DataService 经 <see cref="GetKey"/> 取密钥）。
/// <para>方案 §5.10：密钥从 <c>AuthCenterOptions.SecretEncryptionKeyPath</c> 读取（前 32 字节）；
/// 缺失时生产 fail-fast（InvalidOperationException）/ 开发生成 32 随机字节 + 写文件 + Warning（对齐签名密钥策略）。</para>
/// </summary>
internal static class PlatformCredentialKeyStore
{
    private static byte[]? _key;
    private static readonly object Gate = new();

    /// <summary>幂等初始化（PlatformCredentialService 构造时调用；线程安全单例）。</summary>
    public static void Initialize(AuthCenterOptions options, ILogger logger)
    {
        if (_key != null) return;
        lock (Gate)
        {
            if (_key != null) return;

            var keyPath = options.SecretEncryptionKeyPath;
            if (string.IsNullOrWhiteSpace(keyPath))
            {
                if (options.IsProduction)
                    throw new InvalidOperationException("AuthCenterOptions.SecretEncryptionKeyPath 未配置——生产环境必须提供 AES-GCM 密钥文件（前 32 字节）");
                logger.LogWarning("AuthCenterOptions.SecretEncryptionKeyPath 未配置——开发环境生成临时密钥（不落盘，重启即变，仅限开发）");
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
        => _key ?? throw new InvalidOperationException("PlatformCredentialKeyStore 未初始化——请先经 PlatformCredentialService 构造（Initialize）");

    /// <summary>AES-GCM 加密：输出 "base64(iv).base64(tag).base64(cipher)"（12 字节 nonce + 16 字节 tag）。</summary>
    public static string Encrypt(string plaintext, byte[] key)
    {
        var iv = RandomNumberGenerator.GetBytes(12);
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
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
            throw new CryptographicException("PlatformCredential 密文格式非法——期望 base64(iv).base64(tag).base64(cipher)");
        var iv = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var cipher = Convert.FromBase64String(parts[2]);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(iv, cipher, tag, plain);
        return System.Text.Encoding.UTF8.GetString(plain);
    }
}
