using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.DTOs;

namespace TKWF.Ext.Permissions;

/// <summary>数据服务：权限授予表实体——(PermissionName, ProviderName, ProviderKey) 业务唯一。</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 PermissionGrantEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在分部类中编写，标注 [GenerateControllerMethod]
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
//
// 【V0.3.0 (权限管理 Service)】手写骨架（DMP-Lite 模式，`.cs` 入库；`.g.cs` 不入库、构建时自动生成）。
// 消费方通过 DI 注入 PermissionGrantEntityDataService 即可操作权限授予数据。
[GenerateController(FromDataService = true)]
public partial class PermissionGrantEntityDataService(IDomainUser user, IEntityDAC<PermissionGrantEntity> dac)
        : DomainDataServiceBase<PermissionGrantEntity, PermissionGrantEntityDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>按权限名查询所有授予记录。</summary>
    [GenerateControllerMethod]
    public async Task<List<PermissionGrantEntityDto>> GetByPermissionNameAsync(
        string permissionName, CancellationToken ct = default)
    {
        var query = dac.Query.Where(g => g.PermissionName == permissionName);
        var entities = await dac.ToListAsync(query, ct);
        return entities.Select(PermissionGrantEntityDto.FromEntity).ToList();
    }

    /// <summary>按 provider（如 "User"/"Role"）查询所有授予记录。</summary>
    [GenerateControllerMethod]
    public async Task<List<PermissionGrantEntityDto>> GetByProviderAsync(
        string providerName, string providerKey, CancellationToken ct = default)
    {
        var query = dac.Query
            .Where(g => g.ProviderName == providerName && g.ProviderKey == providerKey);
        var entities = await dac.ToListAsync(query, ct);
        return entities.Select(PermissionGrantEntityDto.FromEntity).ToList();
    }

    /// <summary>查询指定权限名 + provider 的授予状态。</summary>
    [GenerateControllerMethod]
    public async Task<PermissionGrantEntityDto?> GetGrantAsync(
        string permissionName, string providerName, string providerKey,
        CancellationToken ct = default)
    {
        var query = dac.Query
            .Where(g => g.PermissionName == permissionName
                        && g.ProviderName == providerName
                        && g.ProviderKey == providerKey);
        var entity = await dac.FirstOrDefaultAsync(query, ct);
        return entity == null ? null : PermissionGrantEntityDto.FromEntity(entity);
    }

    /// <summary>
    /// 设置权限授予（upsert）——存在则更新 IsGranted，不存在则插入。
    /// </summary>
    [GenerateControllerMethod]
    public async Task<PermissionGrantEntityDto> SetGrantAsync(
        string permissionName, string providerName, string providerKey,
        bool isGranted, CancellationToken ct = default)
    {
        var query = dac.Query
            .Where(g => g.PermissionName == permissionName
                        && g.ProviderName == providerName
                        && g.ProviderKey == providerKey);
        var existing = await dac.FirstOrDefaultAsync(query, ct);

        if (existing != null)
        {
            existing.IsGranted = isGranted;
            await dac.UpdateAsync(existing, ct);
            return PermissionGrantEntityDto.FromEntity(existing);
        }
        else
        {
            var entity = new PermissionGrantEntity
            {
                PermissionName = permissionName,
                ProviderName = providerName,
                ProviderKey = providerKey,
                IsGranted = isGranted
            };
            var inserted = await dac.InsertAsync(entity, ct);
            return PermissionGrantEntityDto.FromEntity(inserted);
        }
    }

    /// <summary>撤销权限授予（删除记录）。</summary>
    [GenerateControllerMethod]
    public async Task<bool> RevokeGrantAsync(
        string permissionName, string providerName, string providerKey,
        CancellationToken ct = default)
    {
        var query = dac.Query
            .Where(g => g.PermissionName == permissionName
                        && g.ProviderName == providerName
                        && g.ProviderKey == providerKey);
        var existing = await dac.FirstOrDefaultAsync(query, ct);
        if (existing == null) return false;

        await dac.DeleteAsync(existing, ct);
        return true;
    }
}
