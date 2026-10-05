using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.AuthCenter.DTOs;

namespace TKWF.Ext.AuthCenter;

/// <summary>数据服务：跨系统映射实体——平台内部 id ↔ 业务 app + 业务本地 id + UnionId（统一 DMP 双机制）。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 PlatformAccountMapEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法（IPlatformAccountMapService 委托路径）。
//
// 【无删除路径（T7 定稿）】：本分部类刻意【不】提供 AdminDeleteAsync——映射行由
// IPlatformAccountMapService.LinkAsync 的 upsert 语义全权管理（同键存在则更新 UnionId，否则创建），
// 无业务驱动的删除场景；删除能力仅存于 .g.cs 内部转发访问器（同程序集 Store 可用），不对外暴露。
 public partial class PlatformAccountMapEntityDataService(IDomainUser user, IEntityDAC<PlatformAccountMapEntity> dac)
        : DomainDataServiceBase<PlatformAccountMapEntity, PlatformAccountMapEntityDto>(user, dac, hasSoftDelete:false)
{
    /// <summary>创建映射（Link upsert 的新建分支；CreateTime/UpdateTime UTC）。</summary>
    public async Task CreateAsync(PlatformAccountMapEntity entity, CancellationToken ct = default)
    {
        entity.CreateTime = DateTime.UtcNow;
        entity.UpdateTime = DateTime.UtcNow;
        await EntityCreateAsync(entity, ct);
    }

    /// <summary>按唯一三键查询（(PlatformAccountId, BusinessAppId, BusinessLocalId)——Link upsert 判定）。</summary>
    public async Task<PlatformAccountMapEntity?> GetByKeyAsync(
        string platformAccountId, string businessAppId, string businessLocalId, CancellationToken ct = default)
        => await EntityGetAsync(m => m.PlatformAccountId == platformAccountId
            && m.BusinessAppId == businessAppId && m.BusinessLocalId == businessLocalId, ct);

    /// <summary>按平台内部 id + 业务 app 查询（平台侧 → 业务侧）。</summary>
    public async Task<PlatformAccountMapEntity?> GetByPlatformAsync(string platformAccountId, string businessAppId, CancellationToken ct = default)
        => await EntityGetAsync(m => m.PlatformAccountId == platformAccountId && m.BusinessAppId == businessAppId, ct);

    /// <summary>按业务 app + 业务本地 id 查询（业务侧 → 平台侧）。</summary>
    public async Task<PlatformAccountMapEntity?> GetByBusinessAsync(string businessAppId, string businessLocalId, CancellationToken ct = default)
        => await EntityGetAsync(m => m.BusinessAppId == businessAppId && m.BusinessLocalId == businessLocalId, ct);

    /// <summary>按微信 unionid 查询全部映射（跨应用身份串联，类 UnionId）。</summary>
    public async Task<List<PlatformAccountMapEntity>> GetByUnionIdAsync(string unionId, CancellationToken ct = default)
        => await EntitySelectAsync(m => m.UnionId == unionId, 0, 10_000, null, ct);

    /// <summary>更新映射（Link upsert 更新分支；先设 UpdateTime）。</summary>
    public async Task UpdateAsync(PlatformAccountMapEntity entity, CancellationToken ct = default)
    {
        entity.UpdateTime = DateTime.UtcNow;
        await EntityUpdateAsync(entity, ct);
    }
}
