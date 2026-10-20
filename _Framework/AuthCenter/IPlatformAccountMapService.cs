using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 跨系统映射服务契约——平台内部 id ↔ 业务 app + 业务本地 id（统一 DMP 双机制：映射表/外键）。
/// <para>方案 §5.9——DMP V4.0 管理员走映射表、会员走外键；Ext 以本表统一承载「平台内部 id ↔ 业务 app + 业务本地 id」。</para>
/// <para>V0.11.0（方案 §4.1 T1）：UnionId 列与按 unionid 反查随联邦身份因子化退役——联邦身份映射归
/// <see cref="IExternalIdentityService"/>（权威 = AuthFactorEntity）。</para>
/// </summary>
public interface IPlatformAccountMapService : IDomainService
{
    /// <summary>按平台内部 id + 业务 app 查询映射（平台侧 → 业务侧）。</summary>
    Task<PlatformAccountMapEntity?> GetByPlatformAsync(string platformAccountId, string businessAppId, CancellationToken ct = default);

    /// <summary>按业务 app + 业务本地 id 查询映射（业务侧 → 平台侧）。</summary>
    Task<PlatformAccountMapEntity?> GetByBusinessAsync(string businessAppId, string businessLocalId, CancellationToken ct = default);

    /// <summary>建立映射（upsert——同键存在则幂等返回既有行）。</summary>
    Task<PlatformAccountMapEntity> LinkAsync(string platformAccountId, string businessAppId, string businessLocalId, CancellationToken ct = default);
}
