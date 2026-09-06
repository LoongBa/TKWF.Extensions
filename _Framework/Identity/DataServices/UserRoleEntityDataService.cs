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

partial class UserRoleEntityDataService(IDomainUser user, IEntityDAC<UserRoleEntity> dac)
    : DomainDataServiceBase<UserRoleEntity, UserRoleEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── 数据访问红线整改（2026-09-07）：UserStore/RoleStore 委托路径的业务方法 ──

    /// <summary>按用户 Id 查询角色分配（返回 RoleId 列表）。</summary>
    public async Task<List<long>> GetRoleIdsByUserIdAsync(long userId, CancellationToken ct = default)
    {
        var mappings = await EntitySelectAsync(ur => ur.UserId == userId, 0, 1000, ct: ct);
        return mappings.Select(ur => ur.RoleId).ToList();
    }

    /// <summary>检查用户-角色分配是否存在（幂等用）。</summary>
    public async Task<bool> ExistsAsync(long userId, long roleId, CancellationToken ct = default)
    {
        var mappings = await EntitySelectAsync(
            ur => ur.UserId == userId && ur.RoleId == roleId, 0, 1, ct: ct);
        return mappings.Count > 0;
    }

    /// <summary>检查角色是否已分配给任何用户（删除保护用）。</summary>
    public async Task<bool> RoleHasUsersAsync(long roleId, CancellationToken ct = default)
    {
        var mappings = await EntitySelectAsync(ur => ur.RoleId == roleId, 0, 1, ct: ct);
        return mappings.Count > 0;
    }

    /// <summary>创建用户-角色分配（幂等调用方先查 ExistsAsync）。</summary>
    public async Task CreateAsync(UserRoleEntity mapping, CancellationToken ct = default)
        => await EntityCreateAsync(mapping, ct);

    /// <summary>删除用户-角色分配。</summary>
    public async Task DeleteAsync(long userId, long roleId, CancellationToken ct = default)
    {
        var mappings = await EntitySelectAsync(
            ur => ur.UserId == userId && ur.RoleId == roleId, 0, 1, ct: ct);
        if (mappings.Count > 0)
            await EntityDeleteBatchAsync(mappings.Select(m => m.Id), ct);
    }

    /// <summary>按用户 Id 删除全部分配（级联——DeleteUser 用）。</summary>
    public async Task DeleteByUserIdAsync(long userId, CancellationToken ct = default)
    {
        var mappings = await EntitySelectAsync(ur => ur.UserId == userId, 0, 10000, ct: ct);
        if (mappings.Count > 0)
            await EntityDeleteBatchAsync(mappings.Select(m => m.Id), ct);
    }
}