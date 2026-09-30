using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Authentication;
using TKWF.Ext.Authentication.DTOs;

namespace TKWF.Ext.Authentication;

/// <summary>数据服务：第三方平台凭证实体——AppSecret AES-GCM 加密在【本 DataService 边界】。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 PlatformCredentialEntityDataService.g.cs 承载。
// 【方案 §5.10：加密在 DataService 边界（MANDATORY）】写路径 CreateEncryptedAsync/UpdateEncryptedAsync 在本分部
// 加密 AppSecretEncrypted；读路径 GetSecretByAppAsync 在本分部解密返回明文；Service 层只见明文（不触密文格式）。
// 密钥派生：AuthCenterOptions.SecretEncryptionKeyPath 密钥文件前 32 字节（AES-256-GCM）——决策：密钥 = 文件前
// 32 字节（不做 SHA-256 派生——文件即密钥，简单且对齐"签名密钥文件"模式）；文件缺失 → 生产（IsProduction）
// InvalidOperationException fail-fast / 开发生成 32 随机字节 + 落盘 + ILogger Warning 一次。
// 加密格式："base64(iv).base64(tag).base64(cipher)"（12 字节随机 nonce + 16 字节 tag）。
// 【无删除路径（T8 定稿）】凭证敏感——刻意【不】提供 AdminDeleteAsync/公开 Delete（管理仅经 SetEnabledAsync 启停）；
// .g.cs 内部转发访问器仍可被同程序集使用（如过期清理），不对外暴露。
 public partial class PlatformCredentialEntityDataService(IDomainUser user, IEntityDAC<PlatformCredentialEntity> dac)
        : DomainDataServiceBase<PlatformCredentialEntity, PlatformCredentialEntityDto>(user, dac, hasSoftDelete:false)
{
    /// <summary>AES-GCM 密钥字节数（32 = AES-256）。</summary>
    private const int AesKeySizeBytes = 32;

    /// <summary>AES-GCM nonce（IV）字节数（12）。</summary>
    private const int AesNonceSizeBytes = 12;

    /// <summary>AES-GCM 认证标签字节数（16）。</summary>
    private const int AesTagSizeBytes = 16;

    /// <summary>密文字段分隔符（base64(iv).base64(tag).base64(cipher)）。</summary>
    private const char CipherFieldSeparator = '.';

    /// <summary>Oracle M4：AES-GCM 密钥单一来源 = <see cref="PlatformCredentialKeyStore"/>
    /// （PlatformCredentialService 构造时 Initialize——启动 fail-fast / 开发自动生成；本分部不再自带 LoadAesKey 双实现防漂移）。</summary>
    private static byte[] GetKey() => PlatformCredentialKeyStore.GetKey();

    /// <summary>AES-GCM 加密（Oracle M4——委托 PlatformCredentialKeyStore 单一实现）：明文 → "base64(iv).base64(tag).base64(cipher)"。</summary>
    private string Encrypt(string plaintext)
        => PlatformCredentialKeyStore.Encrypt(plaintext, GetKey());

    /// <summary>AES-GCM 解密（Oracle M4——委托 PlatformCredentialKeyStore 单一实现）："base64(iv).base64(tag).base64(cipher)" → 明文；认证失败抛 CryptographicException。</summary>
    private string Decrypt(string ciphertext)
        => PlatformCredentialKeyStore.Decrypt(ciphertext, GetKey());

    /// <summary>按平台查询全部启用凭证（密文列原样返回——AppSecretEncrypted 已 DtoFieldIgnore，不外泄）。</summary>
    public async Task<IReadOnlyList<PlatformCredentialEntity>> GetEnabledByPlatformAsync(string platform, CancellationToken ct = default)
        => await EntitySelectAsync(c => c.Platform == platform && c.IsEnabled, 0, 10_000, null, ct);

    /// <summary>按平台 + 应用类型查询凭证（实体返回，密文列原样）。</summary>
    public async Task<PlatformCredentialEntity?> GetByAppAsync(string platform, string appType, CancellationToken ct = default)
        => await EntityGetAsync(c => c.Platform == platform && c.AppType == appType, ct);

    /// <summary>读路径解密：按平台 + 应用类型返回明文密钥（AppSecret AES-GCM 解密；无记录返回 null）。</summary>
    public async Task<PlatformCredentialSecret?> GetSecretByAppAsync(string platform, string appType, CancellationToken ct = default)
    {
        var entity = await GetByAppAsync(platform, appType, ct);
        if (entity == null || !entity.IsEnabled || string.IsNullOrEmpty(entity.AppSecretEncrypted))
            return null; // 禁用凭证不发放明文密钥（方案 §5.10 启停管控语义）
        var plainSecret = Decrypt(entity.AppSecretEncrypted);
        return new PlatformCredentialSecret(entity.AppId, plainSecret);
    }

    /// <summary>读路径解密：按平台 + 应用 AppId 返回明文密钥（Oracle M3——微信授权 code 绑定发起 AppId，按 AppId 精确定位凭证）。</summary>
    public async Task<PlatformCredentialSecret?> GetSecretByAppIdAsync(string platform, string appId, CancellationToken ct = default)
    {
        var entity = await EntityGetAsync(c => c.Platform == platform && c.AppId == appId && c.IsEnabled, ct);
        if (entity == null || string.IsNullOrEmpty(entity.AppSecretEncrypted))
            return null;
        var plainSecret = Decrypt(entity.AppSecretEncrypted);
        return new PlatformCredentialSecret(entity.AppId, plainSecret);
    }

    /// <summary>写路径加密：创建凭证（明文 secret 在本边界 AES-GCM 加密落库；CreateTime/UpdateTime UTC）。</summary>
    public async Task CreateEncryptedAsync(PlatformCredentialEntity credential, string plainSecret, CancellationToken ct = default)
    {
        if (credential == null) throw new ArgumentNullException(nameof(credential));
        if (plainSecret == null) throw new ArgumentNullException(nameof(plainSecret));
        credential.AppSecretEncrypted = Encrypt(plainSecret);
        credential.CreateTime = DateTime.UtcNow;
        credential.UpdateTime = DateTime.UtcNow;
        await EntityCreateAsync(credential, ct);
    }

    /// <summary>写路径加密：更新凭证（newPlainSecret null = 不重加密；非 null = 重新加密；UpdateTime 恒置 UTC）。</summary>
    public async Task UpdateEncryptedAsync(PlatformCredentialEntity credential, string? newPlainSecret, CancellationToken ct = default)
    {
        if (credential == null) throw new ArgumentNullException(nameof(credential));
        if (newPlainSecret != null)
            credential.AppSecretEncrypted = Encrypt(newPlainSecret);
        credential.UpdateTime = DateTime.UtcNow;
        await EntityUpdateAsync(credential, ct);
    }

    /// <summary>启用/禁用（凭证启停管控——加载 → 置 IsEnabled + UpdateTime UTC → 更新；记录不存在静默 no-op）。</summary>
    public async Task SetEnabledAsync(long id, bool enabled, CancellationToken ct = default)
    {
        var entity = await EntityGetAsync(c => c.Id == id, ct);
        if (entity == null) return;
        entity.IsEnabled = enabled;
        entity.UpdateTime = DateTime.UtcNow;
        await EntityUpdateAsync(entity, ct);
    }
}
