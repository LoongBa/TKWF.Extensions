using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.TrustCenter;
using TKWF.Ext.TrustCenter.DTOs;

namespace TKWF.Ext.TrustCenter;

/// <summary>数据服务：&#x5BC6;&#x5C01;&#x7968;&#x636E;&#x5B9E;&#x4F53;&#xFF08;&#x9762;&#x2460; &#x5B89;&#x5168;&#x6570;&#x636E;&#x6295;&#x9012;/&#x5BC6;&#x5C01;&#x7968;&#x636E;&#x2014;&#x2014;&#x62C6;&#x5206;&#x547D;&#x540D; 2026-10-16&#xFF0C;ADR109 &#x51B3;&#x7B56; 4 &#x4FEE;&#x8BA2;&#xFF09;&#x3002;      &lt;para&gt;&#x81EA;&#x539F; &lt;see cref=&quot;AccessCodeEntity&quot;/&gt; &#x9762;&#x2460;&#x62C6;&#x5206;&#xFF1A;&#x8F7D;&#x8377;&#x5BC6;&#x5C01; &#x2B; &#x5355;&#x6B21;&#x5151;&#x6362; &#x2B; TTL &#x2B; &#x53EF;&#x6838;&#x9500;&#x4EBA;&#x2014;&#x2014;      &#x5E76;&#x5165;&#x6846;&#x67B6; &lt;c&gt;ISealedTicketService&amp;lt;T&amp;gt;&lt;/c&gt; &#x7684; DB CAS &#x540E;&#x7AEF;&#xFF08;&lt;c&gt;SealedTicketService&amp;lt;T&amp;gt;&lt;/c&gt;&#xFF09;&#x3002;      &#x53EA;&#x5B58; &lt;c&gt;SHA256(code)&lt;/c&gt; &#x7D22;&#x5F15;&#x4E0D;&#x5B58;&#x539F;&#x6587;&#xFF1B;&lt;c&gt;used&lt;/c&gt; &#x539F;&#x5B50; CAS &#x9632;&#x91CD;&#x653E;&#xFF1B;      &lt;c&gt;ExpectedClaimant&lt;/c&gt; &#x53EF;&#x6838;&#x9500;&#x4EBA;&#x8FDB; CAS &#x6761;&#x4EF6;&#xFF08;&#x65E0; TOCTOU&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 SealedTicketEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class SealedTicketEntityDataService(IDomainUser user, IEntityDAC<SealedTicketEntity> dac)
        : DomainDataServiceBase<SealedTicketEntity, SealedTicketEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}