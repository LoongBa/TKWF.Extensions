using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.PrintTemplates;
using TKWF.Ext.PrintTemplates.DTOs;

namespace TKWF.Ext.PrintTemplates;

/// <summary>数据服务：&#x6253;&#x5370;&#x6A21;&#x677F;&#x7248;&#x672C;&#x8868;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x5173;&#x8054;&#x6A21;&#x677F;&#xFF08;TemplateId&#xFF09;&#x2B; &#x7248;&#x672C;&#x53F7;&#xFF08;SemVer &#x5B57;&#x7B26;&#x4E32;&#xFF09;&#x2B; &#x6B63;&#x6587; &#x2B; &#x72B6;&#x6001;&#x3002;     &lt;para&gt;TemplateId &#x2B; Version &#x552F;&#x4E00;&#xFF08;&#x5E76;&#x53D1;&#x53D1;&#x5E03;&#x9632;&#x51B2;&#x7A81;&#x2014;&#x2014;&#x8D25;&#x8005;&#x663E;&#x5F0F;&#x5F02;&#x5E38;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 PrintTemplateVersionEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class PrintTemplateVersionEntityDataService(IDomainUser user, IEntityDAC<PrintTemplateVersionEntity> dac)
        : DomainDataServiceBase<PrintTemplateVersionEntity, PrintTemplateVersionEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }

     // ── 数据访问红线整改：TemplateStore 委托路径的业务方法 ──
     // 异常自然向上传播（模板为审计关键资产，不静默）。

     /// <summary>按 TemplateId + Version（唯一索引 IX_ptv_template_version）查询单条版本实体。</summary>
     public async Task<PrintTemplateVersionEntity?> GetByTemplateAndVersionAsync(long templateId, string version, CancellationToken ct = default)
         => await EntityGetAsync(v => v.TemplateId == templateId && v.Version == version, ct);

     /// <summary>查询指定模板的当前 Active 版本。</summary>
     public async Task<PrintTemplateVersionEntity?> GetActiveByTemplateAsync(long templateId, CancellationToken ct = default)
         => await EntityGetAsync(v => v.TemplateId == templateId && v.Status == PrintTemplateVersionStatus.Active, ct);

     /// <summary>列出模板全部版本（上限 1000 条，按版本号倒序——最新优先）。</summary>
     public async Task<List<PrintTemplateVersionEntity>> ListVersionsByTemplateAsync(long templateId, CancellationToken ct = default)
         => await EntitySelectAsync(v => v.TemplateId == templateId, 0, 1000, q => q.OrderByDescending(v => v.Version), ct);

     /// <summary>按 TemplateId + Version Upsert——存在则更新（按 Id），不存在则插入（回写 Id）。
     /// <para>以"先查后插/更"替代原生 FreeSql <c>InsertOrUpdate</c> 语义；并发发布同 Key 版本唯一约束冲突时
     /// DataService 的 Create 自然抛数据库异常并向上传播，败者显式收到异常（不吞）。</para></summary>
     public async Task UpsertVersionAsync(PrintTemplateVersionEntity version, CancellationToken ct = default)
     {
         var existing = await EntityGetAsync(v => v.TemplateId == version.TemplateId && v.Version == version.Version, ct);
         if (existing != null)
         {
             version.Id = existing.Id;
             await EntityUpdateAsync(version, ct);
         }
         else
         {
             await EntityCreateAsync(version, ct);
         }
     }
}