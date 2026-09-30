using System;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TKWF.Ext.MFA;

namespace TKWF.Ext.MFA.Tests;

/// <summary>
/// TOTP secret 密钥存储测试——生产 fail-fast（Oracle Q8-4）+ secret 密文落库（明文不落库，ADR-MFA-TOTP自研与密钥存储）。
/// <para>⚠️ <see cref="MfaSecretKeyStore"/> 为进程级静态单例（幂等 Initialize）——测试类间并行可能已初始化；
/// 生产 fail-fast 用例经反射复位私有 <c>_key</c> 字段保证本次验证生效（复位→断言紧邻，窗口极小）。</para>
/// </summary>
public class MfaKeyStoreTests
{
    // ── 私有助手 ──

    /// <summary>反射复位 KeyStore 静态密钥（测试侧操纵进程级静态——生产 fail-fast 须 `_key==null` 才触发）。</summary>
    private static void ResetMfaSecretKeyStore()
    {
        var field = typeof(MfaSecretKeyStore).GetField("_key", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new InvalidOperationException("MfaSecretKeyStore._key 字段未找到");
        field.SetValue(null, null);
    }

    /// <summary>取密钥（未初始化则幂等补初始化——并行测试可能复位静态状态，防御性补初始化）。</summary>
    private static byte[] GetOrInitKey()
    {
        try { return MfaSecretKeyStore.GetKey(); }
        catch (InvalidOperationException)
        {
            MfaSecretKeyStore.Initialize(new MfaOptions(), NullLogger.Instance);
            return MfaSecretKeyStore.GetKey();
        }
    }

    /// <summary>从 provisioning URI 提取 Base32 secret 参数。</summary>
    private static string ExtractSecret(string provisioningUri)
    {
        var match = Regex.Match(provisioningUri, "[?&]secret=([^&]+)");
        Assert.True(match.Success, $"无法从 provisioning URI 提取 secret：{provisioningUri}");
        return match.Groups[1].Value;
    }

    // ── 生产缺密钥 fail-fast（Oracle Q8-4） ──

    [Fact]
    public void Production_MissingKeyPath_Initialize_FailFast()
    {
        ResetMfaSecretKeyStore();

        // 生产（IsProduction=true）+ 无 SecretEncryptionKeyPath → KeyStore 初始化拒绝启动
        var ex = Assert.Throws<InvalidOperationException>(() =>
            MfaSecretKeyStore.Initialize(new MfaOptions { IsProduction = true }, NullLogger.Instance));

        Assert.Contains("SecretEncryptionKeyPath", ex.Message);
    }

    [Fact]
    public void Production_MissingKeyPath_ServiceResolution_FailFast()
    {
        // 经宿主解析 IMfaService（MfaService 构造触发 KeyStore 幂等 Initialize）→ 生产缺密钥拒启
        using var host = MfaTestHost.Create(new MfaOptions { IsProduction = true });
        ResetMfaSecretKeyStore();   // 复位后紧邻解析——保证本次验证触发生产分支

        var ex = Assert.Throws<InvalidOperationException>(() => host.Mfa);

        Assert.Contains("SecretEncryptionKeyPath", ex.Message);
    }

    // ── 开发模式无密钥路径 → 临时密钥（不落盘） ──

    [Fact]
    public void DevMode_NoKeyPath_TemporaryKey_Generated()
    {
        ResetMfaSecretKeyStore();

        // 开发模式（IsProduction=false 默认）缺密钥路径 → 生成 32 字节临时密钥，不抛
        MfaSecretKeyStore.Initialize(new MfaOptions(), NullLogger.Instance);

        Assert.Equal(32, MfaSecretKeyStore.GetKey().Length);
    }

    // ── AES-GCM 加密/解密回环 + 密文格式 ──

    [Fact]
    public void Encrypt_Decrypt_Roundtrip_CipherFormat()
    {
        ResetMfaSecretKeyStore();
        MfaSecretKeyStore.Initialize(new MfaOptions(), NullLogger.Instance);
        var key = MfaSecretKeyStore.GetKey();

        const string plaintext = "JBSWY3DPEHPK3PXP";
        var encrypted = MfaSecretKeyStore.Encrypt(plaintext, key);

        // 密文格式 "base64(iv).base64(tag).base64(cipher)"（12 字节 nonce + 16 字节 tag）；密文非明文
        Assert.Contains(".", encrypted);
        Assert.Equal(3, encrypted.Split('.').Length);
        Assert.NotEqual(plaintext, encrypted);

        // 回环解密（AES-GCM 认证）
        Assert.Equal(plaintext, MfaSecretKeyStore.Decrypt(encrypted, key));
    }

    // ── secret 密文落库（明文不落库——方案 §五 DataService/实体 行） ──

    [Fact]
    public async Task SecretCiphertext_InDb_NotPlaintext_DottedFormat()
    {
        using var host = MfaTestHost.Create();
        var userId = Guid.NewGuid().ToString("N");

        var enroll = await host.Mfa.EnrollAsync(userId, "totp", new MfaEnrollContext());
        var plaintextSecret = ExtractSecret(enroll.ProvisioningUri!);

        var row = (await host.Secrets.GetByUserAsync(userId)).Single();
        Assert.NotNull(row.SecretEncrypted);
        // 明文不落库——DB 密文 ≠ 明文 Base32 secret
        Assert.NotEqual(plaintextSecret, row.SecretEncrypted);
        // 密文格式 base64(iv).base64(tag).base64(cipher)
        Assert.Equal(3, row.SecretEncrypted!.Split('.').Length);
        // 解密回环（KeyStore 已初始化——MfaService 构造触发；防御性取密钥）
        Assert.Equal(plaintextSecret, MfaSecretKeyStore.Decrypt(row.SecretEncrypted, GetOrInitKey()));
    }
}
