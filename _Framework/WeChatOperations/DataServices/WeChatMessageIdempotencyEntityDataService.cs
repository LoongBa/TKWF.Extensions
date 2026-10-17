using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x6D88;&#x606F;&#x5E42;&#x7B49;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatMessageIdempotency&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x7F51;&#x5173;&#x6D88;&#x606F;&#x5E42;&#x7B49;&#x952E;&#xFF08;&#x9632;&#x6B62;&#x540C;&#x4E00;&#x5FAE;&#x4FE1;&#x6D88;&#x606F;&#x91CD;&#x590D;&#x8F6C;&#x53D1;/&#x91CD;&#x590D;&#x56DE;&#x590D;&#xFF0C;V0.2.0 &#x589E;&#x7F51;&#x5173;&#x6A21;&#x5757;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;IdempotencyKey&quot;/&gt;&#xFF1A;&#x89C4;&#x8303;&#x5316;&#x5E42;&#x7B49;&#x952E;&#xFF08;ChannelId&#x2B;MsgId&#x2B;FromUserName&#x2B;CreateTime &#x89C4;&#x8303;&#x5316;&#x62FC;&#x63A5;&#x2014;&#x2014;      &#x53BB;&#x7A7A;&#x767D;/&#x7EDF;&#x4E00;&#x5927;&#x5C0F;&#x5199;&#x540E;&#x54C8;&#x5E0C;&#x6216;&#x5B9A;&#x957F;&#x62FC;&#x4E32;&#xFF0C;&#x8BED;&#x4E49;&#x552F;&#x4E00;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;CreateTime&quot;/&gt;&#xFF1A;&#x5FAE;&#x4FE1;&#x539F;&#x59CB;&#x6D88;&#x606F;&#x65F6;&#x95F4;&#xFF08;&lt;b&gt;&#x539F;&#x6837;&#x7559;&#x5B58;&#xFF0C;&#x975E; UTC &#x8F6C;&#x6362;&lt;/b&gt;&#x2014;&#x2014;&#x5FAE;&#x4FE1;&#x65F6;&#x95F4;&#x6233;&#x8BED;&#x4E49;&#x4FDD;&#x7559;&#xFF09;&#xFF1B;      &lt;see cref=&quot;FirstSeenAt&quot;/&gt;&#xFF1A;&#x9996;&#x6B21;&#x89C1;&#x5230;&#x8BE5;&#x952E;&#x65F6;&#x95F4;&#xFF08;UTC&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;LastStatus&quot;/&gt;&#xFF08;int&#xFF09;&#xFF1A;1=Pending &#x5904;&#x7406;&#x4E2D; / 2=Forwarded &#x5DF2;&#x8F6C;&#x53D1; / 3=Skipped &#x5DF2;&#x8DF3;&#x8FC7;&#xFF08;&#x91CD;&#x590D;/&#x65E0;&#x89C4;&#x5219;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x552F;&#x4E00;&#x7D22;&#x5F15;&#xFF1A;&lt;see cref=&quot;IdempotencyKey&quot;/&gt;&#x2014;&#x2014;&#x540C;&#x952E;&#x4E8C;&#x6B21;&#x5165;&#x7AD9;&#x5E42;&#x7B49;&#x547D;&#x4E2D;&#xFF08;&#x9632;&#x5FAE;&#x4FE1;&#x91CD;&#x63A8;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 WeChatMessageIdempotencyEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class WeChatMessageIdempotencyEntityDataService(IDomainUser user, IEntityDAC<WeChatMessageIdempotencyEntity> dac)
        : DomainDataServiceBase<WeChatMessageIdempotencyEntity, WeChatMessageIdempotencyEntityDto>(user, dac, hasSoftDelete:false) 
{
    /// <summary>按规范化幂等键查询（RouteAsync 幂等判决入口——键存在即重复，不依赖状态精确值；方案 §十·四 ③）。</summary>
    /// <param name="idempotencyKey">规范化幂等键（msg:/evt: 前缀 + ChannelId 入键——<see cref="TKW.Federation.WeChat.MessageGateway.WeChatMessageIdempotencyKeyBuilder"/>）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<WeChatMessageIdempotencyEntity?> GetByKeyAsync(string idempotencyKey, CancellationToken ct = default)
        => await EntityGetAsync(e => e.IdempotencyKey == idempotencyKey, ct);

    /// <summary>活跃幂等记录计数（Pending + Forwarded——非终态；惰性清理前置检查，方案 §十·四 ④）。</summary>
    /// <param name="ct">取消令牌。</param>
    public async Task<long> CountActiveAsync(CancellationToken ct = default)
        => await CountAsync(
            e => e.LastStatus == WeChatMessageIdempotencyStatus.Pending
                || e.LastStatus == WeChatMessageIdempotencyStatus.Forwarded,
            ct);
}
