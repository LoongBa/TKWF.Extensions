using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.PrintTemplates;
using TKWF.Ext.PrintTemplates.DTOs;

namespace TKWF.Ext.PrintTemplates;

/// <summary>数据服务：打印模板表实体——定义模板键（如 "Invoice.Standard"）+ 显示名 + 描述。     <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。     SG1 自动生成 <see cref="!:TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>     <para>保留 BCL <c>[Table("PrintTemplate")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；     列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para></summary>
// 提示：标准 CRUD 逻辑和构造函数已由 PrintTemplateEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class PrintTemplateEntityDataService(IDomainUser user, IEntityDAC<PrintTemplateEntity> dac)
        : DomainDataServiceBase<PrintTemplateEntity, PrintTemplateEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }

     // ── 数据访问红线整改：TemplateStore 委托路径的业务方法 ──
     // 异常自然向上传播（模板为审计关键资产，不静默）。

     /// <summary>按模板键（Key 唯一索引）查询单条模板实体。</summary>
     public async Task<PrintTemplateEntity?> GetByKeyAsync(string key, CancellationToken ct = default)
         => await EntityGetAsync(t => t.Key == key, ct);

     /// <summary>按 Key Upsert——存在则更新（保留自增 Id 与 CreateTime，重置 UpdateTime），不存在则插入（回写 Id）。
     /// <para>以"先查后插/更"替代原生 FreeSql <c>InsertOrUpdate</c> 语义；唯一索引冲突（并发同 Key）由数据库
     /// 抛唯一约束异常，自然向上传播不吞。</para></summary>
     public async Task UpsertByKeyAsync(PrintTemplateEntity template, CancellationToken ct = default)
     {
         var existing = await EntityGetAsync(t => t.Key == template.Key, ct);
         if (existing != null)
         {
             template.Id = existing.Id;
             template.CreateTime = existing.CreateTime;
             template.UpdateTime = DateTimeOffset.Now;
             await EntityUpdateAsync(template, ct);
         }
         else
         {
             await EntityCreateAsync(template, ct);
         }
     }
}