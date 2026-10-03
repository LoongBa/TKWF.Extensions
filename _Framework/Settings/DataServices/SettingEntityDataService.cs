using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Settings;
using TKWF.Ext.Settings.DTOs;

namespace TKWF.Ext.Settings;

/// <summary>数据服务：设置表实体——存储分层键值对设置（名称 + 提供者定位 + 值 + 描述）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。     SG1 自动生成 <see cref="!:TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>     <para>保留 BCL <c>[Table("Setting")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；     列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para></summary>
// 提示：标准 CRUD 逻辑和构造函数已由 SettingEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class SettingEntityDataService(IDomainUser user, IEntityDAC<SettingEntity> dac)
        : DomainDataServiceBase<SettingEntity, SettingEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }

     // ── V0.2.1（数据访问红线整改）：SettingStore 委托路径的业务方法 ──

     /// <summary>按三键（Name + ProviderName + ProviderKey）查询单条设置。</summary>
     public async Task<SettingEntity?> GetByKeyAsync(
         string name, string providerName, string? providerKey, CancellationToken ct = default)
         => await EntityGetAsync(s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey, ct);

     /// <summary>按 Provider 查询设置列表（上限 1000 条，满足设置管理页查询）。</summary>
     public async Task<List<SettingEntity>> GetListByProviderAsync(
         string providerName, string? providerKey, CancellationToken ct = default)
         => await EntitySelectAsync(s => s.ProviderName == providerName && s.ProviderKey == providerKey, 0, 1000, ct: ct);

     /// <summary>按三键 Upsert——存在则更新（保留自增 Id 与 CreateTime），不存在则插入。</summary>
     public async Task UpsertByKeyAsync(SettingEntity entity, CancellationToken ct = default)
     {
         var existing = await EntityGetAsync(
             s => s.Name == entity.Name && s.ProviderName == entity.ProviderName && s.ProviderKey == entity.ProviderKey, ct);
         if (existing != null)
         {
             existing.Value = entity.Value;
             existing.Description = entity.Description;
             existing.IsVisibleToClients = entity.IsVisibleToClients;
             existing.UpdateTime = entity.UpdateTime;
             await EntityUpdateAsync(existing, ct);
         }
         else
         {
             await EntityCreateAsync(entity, ct);
         }
     }

     /// <summary>按三键删除——物理删除（hasSoftDelete:false；EntitySoftDeleteAsync 对未启用软删实体抛
     /// <c>InvalidOperationException</c>，故用 <c>EntityDeleteBatchAsync</c> 走物理删路径）。</summary>
     public async Task DeleteByKeyAsync(
         string name, string providerName, string? providerKey, CancellationToken ct = default)
     {
         var existing = await EntityGetAsync(
             s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey, ct);
         if (existing != null)
             await EntityDeleteBatchAsync(new[] { existing.Id }, ct);
     }
 }