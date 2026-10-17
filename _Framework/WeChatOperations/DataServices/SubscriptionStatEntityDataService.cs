using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x8BA2;&#x9605;&#x7EDF;&#x8BA1;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatSubscriptionStat&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x6309; channel &#x6309;&#x65E5;&#x7EDF;&#x8BA1;&#x8BA2;&#x9605;/&#x53D6;&#x5173;&#xFF08;subscribe/unsubscribe &#x4E8B;&#x4EF6;&#x9A71;&#x52A8;&#xFF0C;&#x6BCF;&#x65E5; upsert&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x552F;&#x4E00;&#x7EA6;&#x675F; (ChannelId, StatDate)&#xFF1A;&#x6BCF;&#x65E5;&#x6BCF; channel &#x4E00;&#x884C;&#x2014;&#x2014;&lt;c&gt;ISubscriptionStatService.RecordAsync&lt;/c&gt; &#x6309;&#x6B64;&#x952E;&#x6BCF;&#x65E5; upsert &#x539F;&#x5B50;&#x7D2F;&#x52A0;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 SubscriptionStatEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class SubscriptionStatEntityDataService(IDomainUser user, IEntityDAC<SubscriptionStatEntity> dac)
        : DomainDataServiceBase<SubscriptionStatEntity, SubscriptionStatEntityDto>(user, dac, hasSoftDelete:false) 
{
     // 场景：业务驱动的删除（自动按 HasSoftDelete 分派：有软删→软删，无软删→物理删）
     public async Task<bool> AdminDeleteAsync(long id, CancellationToken ct)
     {
         // 公共 DeleteAsync 自动分派——不调用 [Obsolete] 的 InternalHardDeleteAsync（ADR15）
         return await DeleteAsync(id, ct);
     }
}