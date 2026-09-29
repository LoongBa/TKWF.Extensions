using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 跨系统映射服务契约——平台内部 id ↔ 业务 app + 业务本地 id + UnionId（统一 DMP 双机制：映射表/外键）。
/// <para>方案 §5.9——DMP V4.0 管理员走映射表、会员走外键；Ext 以本表统一承载「平台内部 id ↔ 业务 app + 业务本地 id」。</para>
/// </summary>
public interface IPlatformAccountMapService
{
    /// <summary>按平台内部 id + 业务 app 查询映射（平台侧 → 业务侧）。</summary>
    Task<PlatformAccountMapEntity?> GetByPlatformAsync(string platformAccountId, string businessAppId, CancellationToken ct = default);

    /// <summary>按业务 app + 业务本地 id 查询映射（业务侧 → 平台侧）。</summary>
    Task<PlatformAccountMapEntity?> GetByBusinessAsync(string businessAppId, string businessLocalId, CancellationToken ct = default);

    /// <summary>建立/更新映射（upsert——同键存在则更新 UnionId）。</summary>
    Task<PlatformAccountMapEntity> LinkAsync(string platformAccountId, string businessAppId, string businessLocalId, string? unionId, CancellationToken ct = default);

    /// <summary>按微信 unionid 查询全部映射（跨应用身份串联，类 UnionId）。</summary>
    Task<IReadOnlyList<PlatformAccountMapEntity>> GetByUnionIdAsync(string unionId, CancellationToken ct = default);
}
