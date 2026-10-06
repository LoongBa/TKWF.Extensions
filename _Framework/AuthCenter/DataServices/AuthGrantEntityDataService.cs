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

/// <summary>数据服务：应用授权实体——OAuth2 authorization grant 语义（V0.8.0 `/grants` 端点数据底座 + B-口令兑换产品线写入点预留）。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AuthGrantEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询/写入方法。
partial class AuthGrantEntityDataService(IDomainUser user, IEntityDAC<AuthGrantEntity> dac)
        : DomainDataServiceBase<AuthGrantEntity, AuthGrantEntityDto>(user, dac, hasSoftDelete:false)
{
    /// <summary>按复合键查询授权记录（upsert 判定前置——UserId+AppId+Source 唯一约束对齐 UX_AuthGrant_User_App_Source）。</summary>
    public async Task<AuthGrantEntity?> GetByKeyAsync(string userId, string appId, string source, CancellationToken ct = default)
        => await EntityGetAsync(e => e.UserId == userId && e.AppId == appId && e.Source == source, ct);

    /// <summary>查询用户有效授权（Status=Active(0)，按 CreateTime 倒序；appId 可空 = 全部）。</summary>
    public async Task<IReadOnlyList<AuthGrantEntity>> GetGrantsAsync(string userId, string? appId, CancellationToken ct = default)
    {
        var list = await EntitySelectAsync(
            appId is null
                ? (e => e.UserId == userId && e.Status == 0)
                : (e => e.UserId == userId && e.AppId == appId && e.Status == 0),
            skip: 0,
            limit: 100,
            orderBy: q => q.OrderByDescending(e => e.CreateTime),
            ct);
        return list;
    }

    /// <summary>创建授权记录（回写自增 Id；CreateTime/UpdateTime UTC）。</summary>
    public async Task<AuthGrantEntity> CreateAsync(AuthGrantEntity entity, CancellationToken ct = default)
    {
        entity.CreateTime = DateTime.UtcNow;
        entity.UpdateTime = DateTime.UtcNow;
        return await EntityCreateAsync(entity, ct);
    }

    /// <summary>刷新授权为 Active 态（条件原子更新——ADR89：单语句 WHERE 谓词更新 + 影响行数；
    /// upsert 幂等命中既有行时刷新 Status=0 + UpdateTime，防 read-modify-write 竞态）。</summary>
    public async Task<bool> RefreshActiveAsync(long id, DateTime updateTime, CancellationToken ct = default)
    {
        var affected = await EntityUpdateWhereAsync(
            e => e.Id == id,
            e => new AuthGrantEntity { Status = 0, UpdateTime = updateTime },
            ct);
        return affected == 1;
    }
}
