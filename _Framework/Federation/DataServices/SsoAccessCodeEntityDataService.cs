using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.Federation.DTOs;

namespace TKWF.Ext.Federation;

/// <summary>数据服务：SSO &#x6388;&#x6743;&#x7801;&#x5B9E;&#x4F53;&#xFF08;&#x8054;&#x90A6; accesscode&#xFF0C;&#x901A;&#x9053; B &#x7968;&#x636E;&#xFF09;&#x2014;&#x2014;120s &#x5355;&#x6B21; &#x2B; SHA256 &#x5B58;&#x50A8; &#x2B; &#x539F;&#x5B50; CAS &#x2B; PKCE &#x53EF;&#x9009;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x8BBE;&#x8BA1;&#x6587;&#x6863; &#xA7;6.1 &#x2B; Oracle P2-1/P2-2&#xFF1A;&#x53EA;&#x5B58; &lt;c&gt;SHA256(code)&lt;/c&gt; &#x7D22;&#x5F15;&#x4E0D;&#x5B58;&#x539F;&#x6587;&#xFF1B;TTL 120 &#x79D2;&#xFF1B;      &lt;c&gt;used&lt;/c&gt; &#x539F;&#x5B50; CAS&#xFF08;&lt;c&gt;UPDATE ... WHERE used=false&lt;/c&gt;&#xFF09;&#x9632;&#x91CD;&#x653E;&#xFF1B;&lt;c&gt;code_verifier_hash&lt;/c&gt; PKCE &#x53EF;&#x9009;      &#xFF08;defense in depth&#x2014;&#x2014;&#x590D;&#x7528; IOAuthTicketService &#x65E2;&#x6709; PKCE &#x8D44;&#x4EA7;&#x8BED;&#x4E49;&#xFF0C;Oracle P2-1&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 SsoAccessCodeEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class SsoAccessCodeEntityDataService(IDomainUser user, IEntityDAC<SsoAccessCodeEntity> dac)
        : DomainDataServiceBase<SsoAccessCodeEntity, SsoAccessCodeEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}