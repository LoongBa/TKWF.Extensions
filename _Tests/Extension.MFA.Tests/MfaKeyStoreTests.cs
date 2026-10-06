using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Ext.MFA;

namespace TKWF.Ext.MFA.Tests;

/// <summary>
/// TOTP secret 密钥存储测试（E4 密钥管理抽象 V0.2.0）——生产 fail-fast（Oracle Q8-4）+ secret 密文落库（明文不落库，ADR-MFA-TOTP自研与密钥存储）。
/// <para>V0.2.0（E4 T3）：原进程级静态 <c>MfaSecretKeyStore</c> 已删除——改经框架 <see cref="FileSymmetricKeyProvider"/>
/// （keyed <see cref="ISymmetricKeyProvider"/> 单例）直接验证三态语义（生产缺密钥构造即抛 / dev 随机不落盘 /
/// dev 写盘跨实例可读）+ 单段规范密文格式（base64(nonce[12]‖cipher‖tag[16])，无点分隔）。
/// 每用例独立 provider 实例（无进程级静态——无需反射复位；临时密钥文件用独立路径 + finally 清理）。</para>
/// </summary>
public class MfaKeyStoreTests
{
    // ── 私有助手 ──

    /// <summary>临时密钥文件路径（每用例独立——防跨用例文件残留干扰）。</summary>
    private static string TempKeyPath()
        => Path.Combine(Path.GetTempPath(), "tkfw-mfa-key-" + Guid.NewGuid().ToString("N") + ".key");

    /// <summary>从 provisioning URI 提取 Base32 secret 参数。</summary>
    private static string ExtractSecret(string provisioningUri)
    {
        var match = Regex.Match(provisioningUri, "[?&]secret=([^&]+)");
        Assert.True(match.Success, $"无法从 provisioning URI 提取 secret：{provisioningUri}");
        return match.Groups[1].Value;
    }

    // ── 生产缺密钥 fail-fast（Oracle Q8-4） ──

    [Fact]
    public void Production_MissingKeyPath_ProviderCtor_FailFast()
    {
        // 生产（IsProduction=true）+ 无 SecretEncryptionKeyPath → FileSymmetricKeyProvider 构造即抛（拒绝启动）
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new FileSymmetricKeyProvider(keyPath: null, isProduction: true, NullLogger<FileSymmetricKeyProvider>.Instance));

        Assert.Contains("密钥文件路径", ex.Message);
    }

    [Fact]
    public void Production_MissingKeyPath_ServiceResolution_FailFast()
    {
        // 经宿主解析 IMfaService（守卫工厂建 MfaService → 解析 IEnumerable<IMfaMethod> → TotpMfaMethod 构造 →
        // keyed 单例工厂触发 FileSymmetricKeyProvider 构造）→ 生产缺密钥拒启
        using var host = MfaTestHost.Create(new MfaOptions { IsProduction = true });

        var ex = Assert.Throws<InvalidOperationException>(() => host.Mfa);

        Assert.Contains("密钥文件路径", ex.Message);
    }

    // ── 开发模式无密钥路径 → 临时密钥（不落盘） ──

    [Fact]
    public void DevMode_NoKeyPath_TemporaryKey_Generated()
    {
        // 开发模式（IsProduction=false 默认）缺密钥路径 → 随机 32 字节临时密钥，不抛
        var provider = new FileSymmetricKeyProvider(keyPath: null, isProduction: false, NullLogger<FileSymmetricKeyProvider>.Instance);

        Assert.Equal(32, provider.GetKey().Length);
    }

    // ── 开发模式配置密钥路径（文件缺失 → 写盘 + 跨实例可读） ──

    [Fact]
    public void DevMode_ConfiguredPath_MissingFile_WritesKey_CrossInstanceReadable()
    {
        var path = TempKeyPath();
        try
        {
            // dev 配置 path 但文件缺失 → 随机 32B + 写入文件（跨重启密文可读——对齐扩展既有行为）
            var provider = new FileSymmetricKeyProvider(path, isProduction: false, NullLogger<FileSymmetricKeyProvider>.Instance);

            Assert.True(File.Exists(path));
            Assert.Equal(32, new FileInfo(path).Length);

            // 新实例重读同一文件 → 同一密钥材料（可解旧实例密文）
            var provider2 = new FileSymmetricKeyProvider(path, isProduction: false, NullLogger<FileSymmetricKeyProvider>.Instance);
            const string plaintext = "JBSWY3DPEHPK3PXP";
            Assert.Equal(plaintext, provider2.Decrypt(provider.Encrypt(plaintext)));
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    // ── AES-GCM 加密/解密回环 + 密文格式（单段规范格式） ──

    [Fact]
    public void Encrypt_Decrypt_Roundtrip_CipherFormat()
    {
        // 开发模式临时密钥（随机 32B）——回环加解密（AES-GCM 认证）
        var provider = new FileSymmetricKeyProvider(keyPath: null, isProduction: false, NullLogger<FileSymmetricKeyProvider>.Instance);

        const string plaintext = "JBSWY3DPEHPK3PXP";
        var encrypted = provider.Encrypt(plaintext);

        // 单段规范格式 base64(nonce[12]‖cipher‖tag[16])——无点分隔；密文非明文
        Assert.DoesNotContain(".", encrypted);
        Assert.NotEqual(plaintext, encrypted);

        // 回环解密（AES-GCM 认证）
        Assert.Equal(plaintext, provider.Decrypt(encrypted));
    }

    // ── secret 密文落库（明文不落库——方案 §五 DataService/实体 行） ──

    [Fact]
    public async Task SecretCiphertext_InDb_NotPlaintext_SingleBlobFormat()
    {
        using var host = MfaTestHost.Create();
        var userId = Guid.NewGuid().ToString("N");

        var enroll = await host.Mfa.EnrollAsync(userId, "totp", new MfaEnrollContext());
        var plaintextSecret = ExtractSecret(enroll.ProvisioningUri!);

        var row = (await host.Secrets.GetByUserAsync(userId)).Single();
        Assert.NotNull(row.SecretEncrypted);
        // 明文不落库——DB 密文 ≠ 明文 Base32 secret
        Assert.NotEqual(plaintextSecret, row.SecretEncrypted);
        // 单段规范格式 base64(nonce[12]‖cipher‖tag[16])——无点分隔
        Assert.DoesNotContain(".", row.SecretEncrypted!);
        // 解密回环（经宿主 keyed 单例 provider——同一密钥材料）
        var provider = host.ServiceProvider.GetRequiredKeyedService<ISymmetricKeyProvider>(SymmetricKeyProviderKeys.Mfa);
        Assert.Equal(plaintextSecret, provider.Decrypt(row.SecretEncrypted));
    }
}
