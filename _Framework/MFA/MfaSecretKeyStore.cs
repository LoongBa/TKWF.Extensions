using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.MFA;

/// <summary>
/// MFA secret AES-GCM 密钥持有者（internal static——DataService 分部构造器固定 (IDomainUser, IEntityDAC) 两参，
/// 无法注入 Options；由 <see cref="MfaService"/> 构造时幂等初始化，TOTP 加解密经 <see cref="GetKey"/> 取密钥）。
/// <para>镜像 <c>PlatformCredentialKeyStore</c>（Authentication）机制（ADR-MFA-TOTP自研与密钥存储 §决策2）：
/// 密钥从 <see cref="MfaOptions.SecretEncryptionKeyPath"/> 读取（前 32 字节 = AES-256-GCM）；缺失时生产
/// （IsProduction=true）fail-fast（InvalidOperationException 拒启动）/ 开发生成 32 随机字节 + Warning；
/// 文件存在取前 32 字节（&lt;32 抛）；文件不存在 → 生产抛 / 开发生成写文件 + Warning。</para>
/// <para>加密格式："base64(iv).base64(tag).base64(cipher)"（12 字节随机 nonce + 16 字节 tag）。
/// DB 无明文——TOTP secret 仅以密文形式落 <see cref="MfaSecretEntity.SecretEncrypted"/>。</para>
/// </summary>
internal static class MfaSecretKeyStore
{
    /// <summary>AES-GCM 密钥（前 32 字节——幂等单例）。</summary>
    private static byte[]? _key;

    /// <summary>初始化锁（幂等单例——线程安全双检）。</summary>
    private static readonly object Gate = new();

    /// <summary>幂等初始化（<see cref="MfaService"/> 构造时调用；线程安全单例——已初始化直接返回）。</summary>
    public static void Initialize(MfaOptions options, ILogger logger)
    {
        if (_key != null) return;
        lock (Gate)
        {
            if (_key != null) return;

            var keyPath = options.SecretEncryptionKeyPath;
            if (string.IsNullOrWhiteSpace(keyPath))
            {
                if (options.IsProduction)
                    throw new InvalidOperationException("MfaOptions.SecretEncryptionKeyPath 未配置——生产环境必须提供 AES-GCM 密钥文件（前 32 字节）");
                logger.LogWarning("MfaOptions.SecretEncryptionKeyPath 未配置——开发环境生成临时密钥（不落盘，重启即变，仅限开发）");
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

    /// <summary>取密钥（须先经 <see cref="MfaService"/> 构造 <see cref="Initialize"/>——否则 InvalidOperationException）。</summary>
    public static byte[] GetKey()
        => _key ?? throw new InvalidOperationException("MfaSecretKeyStore 未初始化——请先经 MfaService 构造（Initialize）");

    /// <summary>AES-GCM 加密：明文 → "base64(iv).base64(tag).base64(cipher)"（12 字节随机 nonce + 16 字节 tag）。</summary>
    public static string Encrypt(string plaintext, byte[] key)
    {
        if (plaintext is null) throw new ArgumentNullException(nameof(plaintext));
        if (key is null || key.Length == 0) throw new ArgumentNullException(nameof(key));

        var iv = RandomNumberGenerator.GetBytes(12);
        var plainBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);
        var cipher = new byte[plainBytes.Length];
        var tag = new byte[16];
        using var aes = new AesGcm(key, 16);
        aes.Encrypt(iv, plainBytes, cipher, tag);
        return Convert.ToBase64String(iv) + "." + Convert.ToBase64String(tag) + "." + Convert.ToBase64String(cipher);
    }

    /// <summary>AES-GCM 解密：解析 "base64(iv).base64(tag).base64(cipher)"；格式非法/认证失败抛 CryptographicException。</summary>
    public static string Decrypt(string value, byte[] key)
    {
        if (value is null) throw new ArgumentNullException(nameof(value));

        var parts = value.Split('.');
        if (parts.Length != 3)
            throw new CryptographicException("MFA secret 密文格式非法——期望 base64(iv).base64(tag).base64(cipher)");
        var iv = Convert.FromBase64String(parts[0]);
        var tag = Convert.FromBase64String(parts[1]);
        var cipher = Convert.FromBase64String(parts[2]);
        var plain = new byte[cipher.Length];
        using var aes = new AesGcm(key, 16);
        aes.Decrypt(iv, cipher, tag, plain);
        return System.Text.Encoding.UTF8.GetString(plain);
    }
}