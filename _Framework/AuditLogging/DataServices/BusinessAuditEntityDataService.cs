using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuditLogging;
using TKWF.Ext.AuditLogging.DTOs;

namespace TKWF.Ext.AuditLogging;

/// <summary>数据服务：&#x4E1A;&#x52A1;&#x4E8B;&#x5B9E;&#x5BA1;&#x8BA1;&#x8868;&#x5B9E;&#x4F53;&#x2014;&#x2014;&#x4E00;&#x884C; = &#x4E00;&#x6B21;&#x4E1A;&#x52A1;&#x5BF9;&#x8C61;&#x53D8;&#x66F4;&#x4E8B;&#x5B9E;&#xFF08;Actor &#x2B; Target &#x2B; Action &#x2B; Before/After&#xFF09;&#x3002;      &lt;para&gt;&#x4E0E; &lt;see cref=&quot;AuditLogEntity&quot;/&gt;&#xFF08;&#x65B9;&#x6CD5;&#x8C03;&#x7528;&#x5BA1;&#x8BA1;&#xFF09;&#x5206;&#x7ACB;&#xFF1A;&lt;b&gt;&#x5199;&#x6A21;&#x578B;&#x5206;&#x7ACB;&#xFF0C;&#x6BCF;&#x9762;&#x5355;&#x4E00;&#x4E8B;&#x5B9E;&#x6E90;&lt;/b&gt;&#xFF08;P2&#xFF09;&#x3002;      &#x5185;&#x5BB9;&#x5217;&#x663E;&#x5F0F; &lt;c&gt;[MaxLength(-1)]&lt;/c&gt;&#xFF08;AGENTS &#xA7;8 2026-11-14 &#x4E8B;&#x6545;&#x7EA2;&#x7EBF;&#x2014;&#x2014;&#x9632; Pg 22001&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;b&gt;&#x9ED8;&#x8BA4; append-only&lt;/b&gt;&#xFF08;ADR-AuditLogging-&#x4E0D;&#x53EF;&#x62B5;&#x8D56;&#x4E0E;&#x5220;&#x9664;&#x7B56;&#x7565;&#xFF09;&#x2014;&#x2014;&#x6807;&#x51C6; CRUD &#x66B4;&#x9732;&#x9762;&#x5728; DataService &#x5206;&#x90E8;&#x6392;&#x9664;      &lt;c&gt;BeforeJson&lt;/c&gt;/&lt;c&gt;AfterJson&lt;/c&gt;&#xFF08;&#x6BD4;&#x7167; V0.4.0 &lt;c&gt;ArgumentsJson&lt;/c&gt; ExcludeMethods &#x5148;&#x4F8B;&#xFF0C;&#x9632; PII&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 BusinessAuditEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class BusinessAuditEntityDataService(IDomainUser user, IEntityDAC<BusinessAuditEntity> dac)
        : DomainDataServiceBase<BusinessAuditEntity, BusinessAuditEntityDto>(user, dac, hasSoftDelete:false) 
{
}