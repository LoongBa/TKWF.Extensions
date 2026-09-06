using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Settings;
using TKWF.Ext.Settings.DTOs;

namespace TKWF.Ext.Settings;

/// <summary>数据服务：&#x8BBE;&#x7F6E;&#x8868;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x5B58;&#x50A8;&#x5206;&#x5C42;&#x952E;&#x503C;&#x5BF9;&#x8BBE;&#x7F6E;&#xFF08;&#x540D;&#x79F0; &#x2B; &#x63D0;&#x4F9B;&#x8005;&#x5B9A;&#x4F4D; &#x2B; &#x503C; &#x2B; &#x63CF;&#x8FF0;&#xFF09;&#x3002;     &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;     SG1 &#x81EA;&#x52A8;&#x751F;&#x6210; &lt;see cref=&quot;!:TKW.Framework.Domain.IDomainEntity&quot;/&gt; &#x90E8;&#x5206;&#x4E0E; DTO/DataService&#x3002;&lt;/para&gt;     &lt;para&gt;&#x4FDD;&#x7559; BCL &lt;c&gt;[Table(&quot;Setting&quot;)]&lt;/c&gt;&#xFF08;&lt;c&gt;FreeSqlTableStructureSynchronizer&lt;/c&gt; &#x9760;&#x5B83;&#x53D1;&#x73B0;&#x5B9E;&#x4F53;&#x5EFA;&#x8868;&#xFF09;&#xFF1B;     &#x5217;&#x6620;&#x5C04;&#x7528; FreeSql &lt;c&gt;[Column]&lt;/c&gt;&#xFF08;IsPrimary/IsIdentity/Position&#xFF0C;&#x5168;&#x9650;&#x5B9A;&#x907F;&#x514D;&#x4E0E; BCL Schema &#x7279;&#x6027;&#x540D;&#x51B2;&#x7A81;&#xFF09;&#x3002;&lt;/para&gt;</summary>
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