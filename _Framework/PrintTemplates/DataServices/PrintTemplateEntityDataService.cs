using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.PrintTemplates;
using TKWF.Ext.PrintTemplates.DTOs;

namespace TKWF.Ext.PrintTemplates;

/// <summary>数据服务：&#x6253;&#x5370;&#x6A21;&#x677F;&#x8868;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x5B9A;&#x4E49;&#x6A21;&#x677F;&#x952E;&#xFF08;&#x5982; &quot;Invoice.Standard&quot;&#xFF09;&#x2B; &#x663E;&#x793A;&#x540D; &#x2B; &#x63CF;&#x8FF0;&#x3002;     &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;     SG1 &#x81EA;&#x52A8;&#x751F;&#x6210; &lt;see cref=&quot;!:TKW.Framework.Domain.IDomainEntity&quot;/&gt; &#x90E8;&#x5206;&#x4E0E; DTO/DataService&#x3002;&lt;/para&gt;     &lt;para&gt;&#x4FDD;&#x7559; BCL &lt;c&gt;[Table(&quot;PrintTemplate&quot;)]&lt;/c&gt;&#xFF08;&lt;c&gt;FreeSqlTableStructureSynchronizer&lt;/c&gt; &#x9760;&#x5B83;&#x53D1;&#x73B0;&#x5B9E;&#x4F53;&#x5EFA;&#x8868;&#xFF09;&#xFF1B;     &#x5217;&#x6620;&#x5C04;&#x7528; FreeSql &lt;c&gt;[Column]&lt;/c&gt;&#xFF08;IsPrimary/IsIdentity/Position&#xFF0C;&#x5168;&#x9650;&#x5B9A;&#x907F;&#x514D;&#x4E0E; BCL Schema &#x7279;&#x6027;&#x540D;&#x51B2;&#x7A81;&#xFF09;&#x3002;&lt;/para&gt;</summary>
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