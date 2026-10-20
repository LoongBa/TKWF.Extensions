using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;
using TKWF.Ext.Federation.DTOs;

namespace TKWF.Ext.Federation;

/// <summary>数据服务：&#x5173;&#x6CE8;/&#x53D6;&#x5173;&#x4E8B;&#x4EF6;&#x4E8B;&#x5B9E;&#x8868;&#xFF08;I2 &#x6E20;&#x9053;&#x5173;&#x6CE8;&#x5173;&#x7CFB;&#x57DF;&#x2014;&#x2014;&#x8FFD;&#x52A0;&#x5199;&#xFF0C;&#x4E8B;&#x5B9E;&#x4E0D;&#x53EF;&#x4E22;&#xFF1A;&#x4E8B;&#x4EF6;&#x884C;&#x65E0;&#x8BBA;&#x662F;&#x5426;&#x6392;&#x5E8F;&#x66F4;&#x65B0;&#x90FD;&#x843D;&#x5E93;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#xFF08;&#x4E0D;&#x6307;&#x5B9A; UserType&#x2014;&#x2014;ADR42 D4&#xFF0C;&#x4F9B;&#x4EFB;&#x610F;&#x88C5;&#x914D;&#x5B9E;&#x4F8B;&#xFF1B;      &#x5BF9;&#x9F50; &lt;see cref=&quot;SsoChannelRegistryEntity&quot;/&gt; &#x4F53;&#x4F8B;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x5E42;&#x7B49;&#xFF08;&#x65B9;&#x6848; &#xA7;4.3&#xFF09;&#xFF1A;&lt;see cref=&quot;DedupeKey&quot;/&gt; = &lt;c&gt;{channelId}|{eventType}|{externalUserId}|{occurredAtUnix}|{eventKey}|{messageId}&lt;/c&gt;      &#xFF08;&#x8D85; 256 &#x5B57;&#x7B26; &#x2192; SHA256 &#x5341;&#x516D;&#x8FDB;&#x5236;&#x2014;&#x2014;&#x4FDD;&#x7559;&#x53EF;&#x5224;&#x5B9A;&#x6027;&#xFF0C;&#x622A;&#x65AD;&#x4F1A;&#x4EA7;&#x751F;&#x5047;&#x78B0;&#x649E;&#xFF09;&#xFF1B;&#x5FAE;&#x4FE1;&#x4E8B;&#x4EF6;&#x65E0; MsgId&#xFF0C;CreateTime &#x79D2;&#x7EA7; &#x2B; FromUserName &#x2B; Event      &#x5DF2;&#x8DB3;&#x591F;&#x552F;&#x4E00;&#xFF0C;&#x91CD;&#x590D;&#x63A8;&#x9001;&#x5FC5;&#x7136;&#x540C;&#x952E;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;Source&quot;/&gt;&#xFF08;R5&#xFF09;&#x5BF9;&#x9F50; WeChatOperations &#x7EDF;&#x8BA1;&#x7EF4;&#x5EA6;&#xFF08;&#x626B;&#x7801;&#x6765;&#x6E90;/&#x6E20;&#x9053;&#x6807;&#x8BC6;&#xFF09;&#xFF1B;&lt;see cref=&quot;EventType&quot;/&gt;      = follow / unfollow / scan_follow / scan_only / message / menu_click&#xFF08;&#x5E73;&#x53F0;&#x5382;&#x5546;&#x8BED;&#x4E49;&#x5F52;&#x4E00;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 FollowEventEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class FollowEventEntityDataService(IDomainUser user, IEntityDAC<FollowEventEntity> dac)
        : DomainDataServiceBase<FollowEventEntity, FollowEventEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}