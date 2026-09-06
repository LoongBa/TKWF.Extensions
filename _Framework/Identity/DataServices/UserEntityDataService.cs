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

partial class UserEntityDataService(IDomainUser user, IEntityDAC<UserEntity> dac)
    : DomainDataServiceBase<UserEntity, UserEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── 数据访问红线整改（2026-09-07）：UserStore 委托路径的业务方法 ──

    /// <summary>按 Id 查询用户（实体返回——基类 GetByIdAsync 返回 DTO，故改名避冲突）。</summary>
    public async Task<UserEntity?> GetEntityByIdAsync(long id, CancellationToken ct = default)
        => await EntityGetAsync(u => u.Id == id, ct);

    /// <summary>按规范化用户名查询用户。</summary>
    public async Task<UserEntity?> GetByNormalizedUserNameAsync(string normalizedUserName, CancellationToken ct = default)
        => await EntityGetAsync(u => u.NormalizedUserName == normalizedUserName, ct);

    /// <summary>按邮箱查询用户。</summary>
    public async Task<UserEntity?> GetByEmailAsync(string email, CancellationToken ct = default)
        => await EntityGetAsync(u => u.Email == email, ct);

    /// <summary>分页查询用户（按 Id 倒序）。</summary>
    public async Task<List<UserEntity>> GetUsersPagedAsync(int skip, int take, CancellationToken ct = default)
        => await EntitySelectAsync(null, skip, take, q => q.OrderByDescending(u => u.Id), ct);

    /// <summary>创建用户（回写自增 Id）。</summary>
    public async Task CreateAsync(UserEntity user, CancellationToken ct = default)
        => await EntityCreateAsync(user, ct);

    /// <summary>更新用户（先设 UpdateTime）。</summary>
    public async Task UpdateAsync(UserEntity user, CancellationToken ct = default)
    {
        user.UpdateTime = DateTimeOffset.Now;
        await EntityUpdateAsync(user, ct);
    }

    /// <summary>按 Id 物理删除（hasSoftDelete:false，用 DeleteBatch）。</summary>
    public async Task DeleteEntityAsync(long id, CancellationToken ct = default)
        => await EntityDeleteBatchAsync(new[] { id }, ct);
}