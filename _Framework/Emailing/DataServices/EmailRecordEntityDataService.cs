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

/// <summary>数据服务：邮件记录表实体——存储发送邮件的记录（收件人、发件人、主题、正文、状态、错误信息等）。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。     SG1 自动生成 IDomainEntity 部分与 DTO/DataService。</para>     <para>保留 BCL <c>[Table("EmailRecord")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；     列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para></summary>
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