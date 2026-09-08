using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.OrganizationUnit.DTOs;

namespace TKWF.Ext.OrganizationUnit;

/// <summary>组织单元-用户关联 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para>唯一约束冲突异常自然传播（<see cref="AddAsync"/> 不捕获）——由 Manager 层捕获转业务异常。</para></summary>
partial class OrganizationUnitUserEntityDataService(IDomainUser user, IEntityDAC<OrganizationUnitUserEntity> dac)
    : DomainDataServiceBase<OrganizationUnitUserEntity, OrganizationUnitUserEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── Store 委托路径的业务方法（异常自然传播） ──

    /// <summary>按用户 Id 查其全部关联行（用户所属 OU 查询用）。</summary>
    public async Task<IReadOnlyList<OrganizationUnitUserEntity>> GetByUserAsync(string userId, CancellationToken ct = default)
        => await EntitySelectAsync(t => t.UserId == userId, 0, MaxBatchRead, q => q.OrderBy(t => t.Id), ct);

    /// <summary>按 OU Id 集合查关联行，投影 UserId 去重。</summary>
    public async Task<IReadOnlyList<string>> GetUserIdsByOrganizationUnitIdsAsync(IReadOnlyList<long> ouIds, CancellationToken ct = default)
    {
        if (ouIds.Count == 0) return Array.Empty<string>();
        var rows = await EntitySelectAsync(t => ouIds.Contains(t.OrganizationUnitId), 0, MaxBatchRead, null, ct);
        return rows.Select(r => r.UserId).Distinct().ToList();
    }

    /// <summary>新增关联（不捕获异常——唯一约束冲突由 Manager 捕获转业务异常）。</summary>
    public async Task AddAsync(OrganizationUnitUserEntity entity, CancellationToken ct = default)
        => await EntityCreateAsync(entity, ct);

    /// <summary>移除单个关联（匹配行物理删除；不存在静默成功——幂等）。</summary>
    public async Task RemoveAsync(long ouId, string userId, CancellationToken ct = default)
    {
        var row = await EntityGetAsync(t => t.OrganizationUnitId == ouId && t.UserId == userId, ct);
        if (row != null)
            await EntityDeleteBatchAsync(new[] { row.Id }, ct);
    }

    /// <summary>统计某 OU 关联用户数（删除保护用）。</summary>
    public async Task<long> CountByOrganizationUnitIdAsync(long ouId, CancellationToken ct = default)
        => await CountAsync(t => t.OrganizationUnitId == ouId, ct);

    /// <summary>批量物理删除 OU 集合的关联行（删除 OU 前置清空 junction）。</summary>
    public async Task DeleteByOrganizationUnitIdsAsync(IReadOnlyList<long> ouIds, CancellationToken ct = default)
    {
        if (ouIds.Count == 0) return;
        var rows = await EntitySelectAsync(t => ouIds.Contains(t.OrganizationUnitId), 0, MaxBatchRead, null, ct);
        if (rows.Count > 0)
            await EntityDeleteBatchAsync(rows.Select(r => r.Id), ct);
    }

    private const int MaxBatchRead = 100000;
}
