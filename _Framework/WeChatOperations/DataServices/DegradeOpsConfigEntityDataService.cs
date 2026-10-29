using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x8FD0;&#x8425;&#x914D;&#x7F6E;&#xB7;&#x964D;&#x7EA7;&#x884C;&#x4E3A;&#x57DF;&#xFF08;&#x8868; &lt;c&gt;TKWF_OpsConfig_Degrade&lt;/c&gt;&#xFF09;&#x2014;&#x2014;D03 1.10 &#x8FD0;&#x8425;&#x914D;&#x7F6E;&#x9762;&#xFF08;&#x6309;&#x57DF;&#x591A;&#x8868; O4-4&#xFF1A;&#x5404;&#x57DF;&#x72EC;&#x7ACB;&#x7248;&#x672C;/&#x5E76;&#x53D1;&#x5199;/schema &#x6F14;&#x8FDB;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;UnknownChannelReplyEnabled&quot;/&gt;&#xFF1A;S2 &#x672A;&#x77E5;&#x901A;&#x9053;&#x63D0;&#x793A;&#x5F00;&#x5173;&#xFF08;D03 &#xA7;4.3 S2&#x2014;&#x2014;&#x9ED8;&#x8BA4; false &#x9759;&#x9ED8;&#x9632;&#x63A2;&#x6D4B;&#xFF1B;      &#x5F00;&#x542F;&#x540E;&#x4E8B;&#x4EF6;&#x7AEF;&#x70B9;&#x672A;&#x77E5;&#x901A;&#x9053;&#x4EE5; 200 &#x7A7A;&#x54CD;&#x5E94;&#x66FF;&#x4EE3; 404&#xFF0C;&#x9632;&#x5FAE;&#x4FE1;&#x91CD;&#x8BD5;&#x98CE;&#x66B4;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x4E50;&#x89C2;&#x5E76;&#x53D1;&#xFF08;R7&#xFF09;&#xFF1A;&lt;see cref=&quot;Version&quot;/&gt; &#x4E3A;&#x5E76;&#x53D1;&#x4EE4;&#x724C;&#x2014;&#x2014;PUT &#x5E26; expected version&#xFF0C;&#x51B2;&#x7A81; 409&#xFF1B;&lt;see cref=&quot;UpdatedAt&quot;/&gt;/&lt;see cref=&quot;UpdatedBy&quot;/&gt; &#x5199;&#x8DEF;&#x5F84;&#x7EDF;&#x4E00;&#x586B;&#x5145;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 DegradeOpsConfigEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class DegradeOpsConfigEntityDataService(IDomainUser user, IEntityDAC<DegradeOpsConfigEntity> dac)
        : DomainDataServiceBase<DegradeOpsConfigEntity, DegradeOpsConfigEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}