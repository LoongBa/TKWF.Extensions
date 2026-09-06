using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Identity;
using TKWF.Ext.Identity.DTOs;

namespace TKWF.Ext.Identity;

partial class RoleEntityDataService(IDomainUser user, IEntityDAC<RoleEntity> dac)
    : DomainDataServiceBase<RoleEntity, RoleEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── 数据访问红线整改（2026-09-07）：RoleStore 委托路径的业务方法 ──

    /// <summary>按 Id 查询角色（实体返回——基类 GetByIdAsync 返回 DTO，故改名避冲突）。</summary>
    public async Task<RoleEntity?> GetEntityByIdAsync(long id, CancellationToken ct = default)
        => await EntityGetAsync(r => r.Id == id, ct);

    /// <summary>按名称查询角色。</summary>
    public async Task<RoleEntity?> GetByNameAsync(string name, CancellationToken ct = default)
        => await EntityGetAsync(r => r.Name == name, ct);

    /// <summary>分页查询角色（按 Id 倒序）。</summary>
    public async Task<List<RoleEntity>> GetRolesPagedAsync(int skip, int take, CancellationToken ct = default)
        => await EntitySelectAsync(null, skip, take, q => q.OrderByDescending(r => r.Id), ct);

    /// <summary>创建角色（回写自增 Id）。</summary>
    public async Task CreateAsync(RoleEntity role, CancellationToken ct = default)
        => await EntityCreateAsync(role, ct);

    /// <summary>更新角色（先设 UpdateTime）。</summary>
    public async Task UpdateAsync(RoleEntity role, CancellationToken ct = default)
    {
        role.UpdateTime = DateTimeOffset.Now;
        await EntityUpdateAsync(role, ct);
    }

    /// <summary>按 Id 物理删除（hasSoftDelete:false，用 DeleteBatch）。</summary>
    public async Task DeleteEntityAsync(long id, CancellationToken ct = default)
        => await EntityDeleteBatchAsync(new[] { id }, ct);
}