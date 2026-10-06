using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：第三方平台凭证实体——纯持久化（AppSecretEncrypted 密文列原样存取，加解密在 PlatformCredentialService 服务层——E4 密钥管理抽象，V0.7.0）。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 PlatformCredentialEntityDataService.g.cs 承载。
// 【E4 密钥管理抽象（V0.7.0）】加密边界从本 DataService 上移服务层：加解密经注入的 ISymmetricKeyProvider
// （keyed 注册键 SymmetricKeyProviderKeys.AuthCenter，FileSymmetricKeyProvider 生产 fail-fast / 开发随机兜底）。
// 本分部回归纯持久化——CreateAsync/UpdateAsync 只做落库（不加密）；GetByAppIdAsync 只做查询（不解密）；
// GetSecretByAppAsync 已删除（解密逻辑在 PlatformCredentialService.GetSecretAsync）。
// 【无删除路径（T8 定稿）】凭证敏感——刻意【不】提供 AdminDeleteAsync/公开 Delete（管理仅经 SetEnabledAsync 启停）；
// .g.cs 内部转发访问器仍可被同程序集使用（如过期清理），不对外暴露。
 public partial class PlatformCredentialEntityDataService(IDomainUser user, IEntityDAC<PlatformCredentialEntity> dac)
        : DomainDataServiceBase<PlatformCredentialEntity, PlatformCredentialEntityDto>(user, dac, hasSoftDelete:false)
{
    /// <summary>按平台查询全部启用凭证（密文列原样返回——AppSecretEncrypted 已 DtoFieldIgnore，不外泄）。</summary>
    public async Task<IReadOnlyList<PlatformCredentialEntity>> GetEnabledByPlatformAsync(string platform, CancellationToken ct = default)
        => await EntitySelectAsync(c => c.Platform == platform && c.IsEnabled, 0, 10_000, null, ct);

    /// <summary>按平台 + 应用类型查询凭证（实体返回，密文列原样）。</summary>
    public async Task<PlatformCredentialEntity?> GetByAppAsync(string platform, string appType, CancellationToken ct = default)
        => await EntityGetAsync(c => c.Platform == platform && c.AppType == appType, ct);

    /// <summary>按平台 + 应用 AppId 查询启用凭证（纯持久化——Oracle M3：微信授权 code 绑定发起 AppId，按 AppId 精确定位；IsEnabled 过滤为查询条件非加密逻辑）。</summary>
    public async Task<PlatformCredentialEntity?> GetByAppIdAsync(string platform, string appId, CancellationToken ct = default)
        => await EntityGetAsync(c => c.Platform == platform && c.AppId == appId && c.IsEnabled, ct);

    /// <summary>创建凭证（纯持久化——AppSecretEncrypted 已由 PlatformCredentialService 服务层加密；CreateTime/UpdateTime UTC）。</summary>
    public async Task CreateAsync(PlatformCredentialEntity credential, CancellationToken ct = default)
    {
        if (credential == null) throw new ArgumentNullException(nameof(credential));
        credential.CreateTime = DateTime.UtcNow;
        credential.UpdateTime = DateTime.UtcNow;
        await EntityCreateAsync(credential, ct);
    }

    /// <summary>更新凭证（纯持久化——重加密已由服务层完成；UpdateTime 恒置 UTC）。</summary>
    public async Task UpdateAsync(PlatformCredentialEntity credential, CancellationToken ct = default)
    {
        if (credential == null) throw new ArgumentNullException(nameof(credential));
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
