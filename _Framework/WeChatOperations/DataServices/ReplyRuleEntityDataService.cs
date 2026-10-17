using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x56DE;&#x590D;&#x89C4;&#x5219;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatReplyRule&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x901A;&#x7528;&#x56DE;&#x590D;&#x89C4;&#x5219;&#x8868;&#xFF08;Oracle P1 &#x6CDB;&#x5316;&#xFF1A;&#x539F; KeywordRule/ClickReplyMap/WelcomeConfig &#x5408;&#x5E76;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;SceneType&#xFF08;int&#xFF09;&#xFF1A;1=Subscribe &#x6B22;&#x8FCE;&#x8BED; / 2=Keyword &#x5173;&#x952E;&#x8BCD; / 3=Click &#x83DC;&#x5355; key &#x6620;&#x5C04; / 4=Image / 5=Voice / 6=Video /      7=ShortVideo / 8=Location / 9=Link / 10=Scan &#x626B;&#x7801; / 11=Default &#x515C;&#x5E95;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;TriggerKey&quot;/&gt;&#xFF1A;&#x5173;&#x952E;&#x8BCD;&#xFF08;Keyword&#xFF09;&#x6216;&#x83DC;&#x5355; key&#xFF08;Click&#xFF09;&#x2014;&#x2014;&#x573A;&#x666F;&#x578B;&#x89C4;&#x5219;&#xFF08;Subscribe/Image/.../Scan/Default&#xFF09;&#x4E3A;&#x7A7A;&#x3002;      &lt;see cref=&quot;MatchMode&quot;/&gt; &#x4EC5; Keyword &#x573A;&#x666F;&#x9002;&#x7528;&#xFF08;1=Exact &#x5B8C;&#x5168;&#x5339;&#x914D; / 2=Contains &#x5305;&#x542B;&#x5339;&#x914D;&#xFF09;&#x3002;      &lt;see cref=&quot;ReplyText&quot;/&gt;&#xFF08;&#x76F4;&#x63A5;&#x6587;&#x672C;&#xFF09;&#x4F18;&#x5148;&#x4E8E; &lt;see cref=&quot;MaterialId&quot;/&gt;&#xFF08;&#x7D20;&#x6750;&#x5F15;&#x7528;&#xFF09;&#x2014;&#x2014;&#x5B58;&#x5728; ReplyText &#x7528; ReplyText&#xFF0C;&#x5426;&#x5219;&#x8D70;&#x7D20;&#x6750;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x26A0;&#xFE0F; Click &#x6620;&#x5C04;&#x72EC;&#x7ACB;&#x4E8E;&#x83DC;&#x5355;&#x7ED3;&#x6784;&#xFF1A;SceneType=Click &#x7684;&#x89C4;&#x5219;&#x4FEE;&#x6539;&lt;b&gt;&#x4E0D;&#x89E6;&#x53D1;&lt;/b&gt; menu/create&#xFF08;Oracle P1 &#x8BED;&#x4E49;&#x4FDD;&#x7559;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 ReplyRuleEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class ReplyRuleEntityDataService(IDomainUser user, IEntityDAC<ReplyRuleEntity> dac)
        : DomainDataServiceBase<ReplyRuleEntity, ReplyRuleEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}