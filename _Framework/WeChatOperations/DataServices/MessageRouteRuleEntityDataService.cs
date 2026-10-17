using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x6D88;&#x606F;&#x8DEF;&#x7531;&#x89C4;&#x5219;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatMessageRouteRule&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x7F51;&#x5173;&#x6D88;&#x606F;&#x8DEF;&#x7531;&#x89C4;&#x5219;&#xFF08;V0.2.0 &#x589E;&#x7F51;&#x5173;&#x6A21;&#x5757;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;MsgType&quot;/&gt;&#xFF08;string&#xFF09;&#xFF1A;&#x5FAE;&#x4FE1;&#x6D88;&#x606F;&#x7C7B;&#x578B;&#xFF08;text/image/voice/video/shortvideo/location/link/event&#xFF09;&#x2014;&#x2014;&#x7A7A; = &#x5168;&#x7C7B;&#x578B;&#x3002;      &lt;see cref=&quot;Event&quot;/&gt;&#xFF08;string&#xFF09;&#xFF1A;&#x4EC5; &lt;see cref=&quot;MsgType&quot;/&gt; = event &#x53C2;&#x4E0E;&#xFF08;subscribe/unsubscribe/SCAN/CLICK/LOCATION&#xFF09;&#xFF0C;&#x7A7A; = &#x5168;&#x4E8B;&#x4EF6;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;MatchMode&quot;/&gt;&#xFF08;int&#xFF09;&#xFF1A;0=None &#x65E0;&#x5339;&#x914D;&#xFF08;&#x7EAF;&#x7C7B;&#x578B;/&#x4E8B;&#x4EF6;&#x8DEF;&#x7531;&#xFF09;/ 1=Exact &#x5B8C;&#x5168;&#x5339;&#x914D; / 2=Contains &#x5305;&#x542B;&#x5339;&#x914D;&#x3002;      &lt;see cref=&quot;TriggerKey&quot;/&gt;&#xFF1A;&#x5339;&#x914D;&#x952E;&#xFF08;MatchMode=None &#x65F6;&#x6052;&#x7A7A;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;ForwardMode&quot;/&gt;&#xFF08;int&#xFF09;&#xFF1A;1=LocalOnly &#x4EC5;&#x672C;&#x5730;&#x56DE;&#x590D; / 2=AsyncCopy &#x5F02;&#x6B65;&#x590D;&#x5236; / 3=SyncProxy &#x540C;&#x6B65;&#x4EE3;&#x7406; /      4=PreprocessSync &#x9884;&#x5904;&#x7406;&#x540E;&#x540C;&#x6B65;&#x3002;&lt;see cref=&quot;EndpointId&quot;/&gt;&#xFF1A;&#x4E0B;&#x6E38;&#x7AEF;&#x70B9;&#x5F15;&#x7528;&#x2014;&#x2014;LocalOnly &#x6052;&#x7A7A;&#xFF08;&#x4EC5;&#x672C;&#x5730;&#x56DE;&#x590D;&#x4E0D;&#x8F6C;&#x53D1;&#x4E0B;&#x6E38;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x89C4;&#x5219;&#x8BFB;&#x4E00;&#x5F8B;&#x6309; &lt;see cref=&quot;ChannelId&quot;/&gt; &#x8FC7;&#x6EE4;&#xFF08;&#x591A;&#x516C;&#x4F17;&#x53F7;&#x6570;&#x636E;&#x9694;&#x79BB;&#xFF09;&#xFF0C;&#x540C; channel &#x5185;&#x6309; &lt;see cref=&quot;SortOrder&quot;/&gt; &#x5347;&#x5E8F;&#x5C1D;&#x8BD5;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 MessageRouteRuleEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class MessageRouteRuleEntityDataService(IDomainUser user, IEntityDAC<MessageRouteRuleEntity> dac)
        : DomainDataServiceBase<MessageRouteRuleEntity, MessageRouteRuleEntityDto>(user, dac, hasSoftDelete:false) 
{
    /// <summary>启用规则列表（ChannelId + IsEnabled，SortOrder 升序——网关路由匹配候选集；方案 §十·一 ④）。</summary>
    /// <param name="channelId">公众号隔离 id（规则读一律按 ChannelId 过滤）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<List<MessageRouteRuleEntity>> GetEnabledRulesAsync(string channelId, CancellationToken ct = default)
        => await EntitySelectAsync(
            r => r.ChannelId == channelId && r.IsEnabled,
            0, 500,
            q => q.OrderBy(r => r.SortOrder).ThenBy(r => r.Id),
            ct);

    /// <summary>按 channel 分页查询规则（管理端——SortOrder 升序 + Id 稳定序）。</summary>
    /// <param name="channelId">公众号隔离 id。</param>
    /// <param name="page">页码（1 起）。</param>
    /// <param name="size">每页条数。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<List<MessageRouteRuleEntity>> QueryRulesByChannelAsync(string channelId, int page, int size, CancellationToken ct = default)
        => await EntitySelectAsync(
            r => r.ChannelId == channelId,
            (page < 1 ? 1 : page - 1) * size, size,
            q => q.OrderBy(r => r.SortOrder).ThenBy(r => r.Id),
            ct);

    /// <summary>按 channel 规则总数（管理端分页计数）。</summary>
    /// <param name="channelId">公众号隔离 id。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<long> CountRulesByChannelAsync(string channelId, CancellationToken ct = default)
        => await CountAsync(r => r.ChannelId == channelId, ct);
}
