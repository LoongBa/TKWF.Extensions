using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuditLogging;
using TKWF.Ext.AuditLogging.DTOs;

namespace TKWF.Ext.AuditLogging;

/// <summary>数据服务：&#x5BA1;&#x8BA1;&#x65E5;&#x5FD7;&#x8868;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x8BB0;&#x5F55;&#x65B9;&#x6CD5;&#x7EA7;&#x8C03;&#x7528;&#x4E8B;&#x4EF6;&#xFF08;&#x8C03;&#x7528;&#x8005;&#x3001;&#x76EE;&#x6807;&#x65B9;&#x6CD5;&#x3001;&#x53C2;&#x6570;&#x8131;&#x654F; JSON&#x3001;&#x8017;&#x65F6;&#x3001;&#x6210;&#x529F;/&#x5F02;&#x5E38;&#x3001;&#x5173;&#x8054; ID&#xFF09;&#x3002;     &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;     SG1 &#x81EA;&#x52A8;&#x751F;&#x6210; &lt;see cref=&quot;!:TKW.Framework.Domain.IDomainEntity&quot;/&gt; &#x90E8;&#x5206;&#x4E0E; DTO/DataService&#x3002;&lt;/para&gt;     &lt;para&gt;&#x4FDD;&#x7559; BCL &lt;c&gt;[Table(&quot;AuditLog&quot;)]&lt;/c&gt;&#xFF08;&lt;c&gt;FreeSqlTableStructureSynchronizer&lt;/c&gt; &#x9760;&#x5B83;&#x53D1;&#x73B0;&#x5B9E;&#x4F53;&#x5EFA;&#x8868;&#xFF09;&#xFF1B;     &#x5217;&#x6620;&#x5C04;&#x7528; FreeSql &lt;c&gt;[Column]&lt;/c&gt;&#xFF08;IsPrimary/IsIdentity/Position&#xFF0C;&#x5168;&#x9650;&#x5B9A;&#x907F;&#x514D;&#x4E0E; BCL Schema &#x7279;&#x6027;&#x540D;&#x51B2;&#x7A81;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 AuditLogEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class AuditLogEntityDataService(IDomainUser user, IEntityDAC<AuditLogEntity> dac)
        : DomainDataServiceBase<AuditLogEntity, AuditLogEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }

    // ── 数据访问红线整改（2026-09-07）：AuditLogStore/AuditLogQueryService 委托路径的业务方法 ──

    /// <summary>按 predicate 计数（.g.cs 无 EntityCountAsync 转发，骨架补 Count——经 EntitySelectAsync 可靠路径）。</summary>
    public async Task<long> CountAsync(System.Linq.Expressions.Expression<Func<AuditLogEntity, bool>>? predicate, CancellationToken ct = default)
    {
        var list = await EntitySelectAsync(predicate, 0, int.MaxValue, ct: ct);
        return list.Count;
    }
}
