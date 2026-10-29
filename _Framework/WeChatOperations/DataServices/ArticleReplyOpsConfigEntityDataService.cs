using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x8FD0;&#x8425;&#x914D;&#x7F6E;&#xB7;&#x56FE;&#x6587;&#x56DE;&#x590D;&#x57DF;&#xFF08;&#x8868; &lt;c&gt;TKWF_OpsConfig_ArticleReply&lt;/c&gt;&#xFF09;&#x2014;&#x2014;D03 1.10 &#x8FD0;&#x8425;&#x914D;&#x7F6E;&#x9762;&#xFF08;&#x6309;&#x57DF;&#x591A;&#x8868; O4-4&#xFF1A;&#x5404;&#x57DF;&#x72EC;&#x7ACB;&#x7248;&#x672C;/&#x5E76;&#x53D1;&#x5199;/schema &#x6F14;&#x8FDB;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;UrlTemplate&quot;/&gt;&#xFF1A;&#x56FE;&#x6587;&#x94FE;&#x63A5;&#x6A21;&#x677F;&#xFF08;&#x5982; &lt;c&gt;https://m.loongba.cn/{mid}/auth?t={ticket}&lt;/c&gt;&#xFF09;&#x2014;&#x2014;      &#x767D;&#x540D;&#x5355;&#x5360;&#x4F4D;&#x7B26;&#x66FF;&#x6362;&#xFF08;&lt;c&gt;{ticket}/{uid}/{openid}/{channel_id}/{channel_type}/{channel_alias}&lt;/c&gt;&#xFF09;&#xFF0C;&#x4E1A;&#x52A1;&#x6BB5;&#xFF08;&#x5982; &lt;c&gt;{mid}&lt;/c&gt;&#xFF09;&#x5B57;&#x9762;&#x8F93;&#x51FA;&#x4E0D;&#x66FF;&#x6362;&#xFF08;D03 &#xA7;7.1&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x4E50;&#x89C2;&#x5E76;&#x53D1;&#xFF08;R7&#xFF09;&#xFF1A;&lt;see cref=&quot;Version&quot;/&gt; &#x4E3A;&#x5E76;&#x53D1;&#x4EE4;&#x724C;&#x2014;&#x2014;PUT &#x5E26; expected version&#xFF0C;&#x51B2;&#x7A81; 409&#xFF1B;&lt;see cref=&quot;UpdatedAt&quot;/&gt;/&lt;see cref=&quot;UpdatedBy&quot;/&gt; &#x5199;&#x8DEF;&#x5F84;&#x7EDF;&#x4E00;&#x586B;&#x5145;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 ArticleReplyOpsConfigEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class ArticleReplyOpsConfigEntityDataService(IDomainUser user, IEntityDAC<ArticleReplyOpsConfigEntity> dac)
        : DomainDataServiceBase<ArticleReplyOpsConfigEntity, ArticleReplyOpsConfigEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}