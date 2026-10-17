using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x4EA4;&#x4E92;&#x65E5;&#x5FD7;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatInteractionLog&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x8FD0;&#x7EF4;&#x6392;&#x67E5;&#x521A;&#x9700;&#xFF08;&#x6BCF;&#x6B21;&#x56DE;&#x590D;&#x8BB0;&#x5F55;&#xFF0C;&#x542B;&#x89E6;&#x53D1;&#x7C7B;&#x578B;/&#x56DE;&#x590D;&#x5185;&#x5BB9;/&#x6240;&#x5F15;&#x7D20;&#x6750;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;TriggerType&#xFF08;int&#xFF09;&#xFF1A;1=Subscribe / 2=Keyword / 3=Click / 4=Image / 5=Voice / 6=Video / 7=ShortVideo /      8=Location / 9=Link / 10=Scan / 11=Default / 12=None&#xFF08;&#x672A;&#x547D;&#x4E2D;&#x89C4;&#x5219;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;Oracle P2&#xFF1A;&lt;see cref=&quot;ReplyMaterialId&quot;/&gt; &#x53EF;&#x7A7A;&#x2014;&#x2014;&#x8FFD;&#x6EAF;&#x56DE;&#x590D;&#x4E86;&#x54EA;&#x4E2A;&#x7D20;&#x6750;&#xFF08;&#x5168;&#x5C40;&#x7D20;&#x6750; &lt;see cref=&quot;MaterialEntity&quot;/&gt; &#x5F15;&#x7528;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 InteractionLogEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class InteractionLogEntityDataService(IDomainUser user, IEntityDAC<InteractionLogEntity> dac)
        : DomainDataServiceBase<InteractionLogEntity, InteractionLogEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}