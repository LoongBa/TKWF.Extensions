using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Emailing;
using TKWF.Ext.Emailing.DTOs;

namespace TKWF.Ext.Emailing;

/// <summary>数据服务：&#x90AE;&#x4EF6;&#x8BB0;&#x5F55;&#x8868;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x5B58;&#x50A8;&#x53D1;&#x9001;&#x90AE;&#x4EF6;&#x7684;&#x8BB0;&#x5F55;&#xFF08;&#x6536;&#x4EF6;&#x4EBA;&#x3001;&#x53D1;&#x4EF6;&#x4EBA;&#x3001;&#x4E3B;&#x9898;&#x3001;&#x6B63;&#x6587;&#x3001;&#x72B6;&#x6001;&#x3001;&#x9519;&#x8BEF;&#x4FE1;&#x606F;&#x7B49;&#xFF09;&#x3002;     &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;     SG1 &#x81EA;&#x52A8;&#x751F;&#x6210; IDomainEntity &#x90E8;&#x5206;&#x4E0E; DTO/DataService&#x3002;&lt;/para&gt;     &lt;para&gt;&#x4FDD;&#x7559; BCL &lt;c&gt;[Table(&quot;EmailRecord&quot;)]&lt;/c&gt;&#xFF08;&lt;c&gt;FreeSqlTableStructureSynchronizer&lt;/c&gt; &#x9760;&#x5B83;&#x53D1;&#x73B0;&#x5B9E;&#x4F53;&#x5EFA;&#x8868;&#xFF09;&#xFF1B;     &#x5217;&#x6620;&#x5C04;&#x7528; FreeSql &lt;c&gt;[Column]&lt;/c&gt;&#xFF08;IsPrimary/IsIdentity/Position&#xFF0C;&#x5168;&#x9650;&#x5B9A;&#x907F;&#x514D;&#x4E0E; BCL Schema &#x7279;&#x6027;&#x540D;&#x51B2;&#x7A81;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 EmailRecordEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class EmailRecordEntityDataService(IDomainUser user, IEntityDAC<EmailRecordEntity> dac)
        : DomainDataServiceBase<EmailRecordEntity, EmailRecordEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }

     // ── V0.2.0（数据访问红线整改）：EmailRecordStore 委托路径的业务方法 ──

     /// <summary>按 Id 查询单条邮件记录（返回实体，未找到返回 null——基类 GetByIdAsync 返回 DTO 且未找到抛异常，故独立命名）。</summary>
     public async Task<EmailRecordEntity?> GetEntityByIdAsync(long id, CancellationToken ct = default)
         => await EntityGetAsync(e => e.Id == id, ct);

     /// <summary>按状态查询邮件记录列表（上限 1000 条，null/空 表示查询全部）。</summary>
     public async Task<List<EmailRecordEntity>> GetListByStatusAsync(string? status, CancellationToken ct = default)
     {
         Expression<Func<EmailRecordEntity, bool>>? predicate = string.IsNullOrEmpty(status)
             ? null
             : e => e.Status == status;
         return await EntitySelectAsync(predicate, 0, 1000, ct: ct);
     }

     /// <summary>Upsert——按 Id 查：存在则更新（保留自增 Id），不存在则插入（回写自增 Id）。</summary>
     public async Task UpsertAsync(EmailRecordEntity entity, CancellationToken ct = default)
     {
         var existing = await EntityGetAsync(e => e.Id == entity.Id, ct);
         if (existing != null)
         {
             await EntityUpdateAsync(entity, ct);
         }
         else
         {
             var created = await EntityCreateAsync(entity, ct);
             entity.Id = created.Id;
         }
     }

     /// <summary>按 Id 物理删除——hasSoftDelete:false；EntitySoftDeleteAsync 对未启用软删实体抛
     /// <c>InvalidOperationException</c>，故用 <c>EntityDeleteBatchAsync</c> 走物理删路径（Settings 已验证）。</summary>
     public async Task DeleteByIdAsync(long id, CancellationToken ct = default)
     {
         var existing = await EntityGetAsync(e => e.Id == id, ct);
         if (existing != null)
             await EntityDeleteBatchAsync(new[] { id }, ct);
     }
}