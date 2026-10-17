using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>数据服务：&#x6D88;&#x606F;&#x8F6C;&#x53D1;&#x65E5;&#x5FD7;&#x5B9E;&#x4F53;&#xFF08;&#x8868; &lt;c&gt;TKWF_WeChatMessageForwardLog&lt;/c&gt;&#xFF09;&#x2014;&#x2014;&#x7F51;&#x5173;&#x6D88;&#x606F;&#x8F6C;&#x53D1;&#x6D41;&#x6C34;&#xFF08;&#x4E00;&#x6B21;&#x5165;&#x7AD9; &#x2192; &#x8DEF;&#x7531;&#x51B3;&#x7B56; &#x2192; &#x51FA;&#x7AD9;&#x7ED3;&#x679C;&#x5168;&#x94FE;&#x8BB0;&#x5F55;&#xFF0C;V0.2.0 &#x589E;&#x7F51;&#x5173;&#x6A21;&#x5757;&#xFF09;&#x3002;      &lt;para&gt;SG1 &#x5316;&#xFF1A;&#x58F0;&#x660E;&#x5F0F;&#x5B9E;&#x4F53;&#x2014;&#x2014;&lt;c&gt;partial&lt;/c&gt; &#x2B; &lt;c&gt;[DomainGenerateCode]&lt;/c&gt;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;Status&quot;/&gt;&#xFF08;int&#xFF09;&#xFF1A;1=Queued &#x5DF2;&#x5165;&#x961F; / 2=Success &#x6210;&#x529F; / 3=Fail &#x5931;&#x8D25; / 4=Timeout &#x8D85;&#x65F6; /      5=SkippedDuplicate &#x91CD;&#x590D;&#x8DF3;&#x8FC7;&#xFF08;&#x5E42;&#x7B49;&#x547D;&#x4E2D;&#xFF09;/ 6=NoRule &#x65E0;&#x5339;&#x914D;&#x89C4;&#x5219;&#x3002;&lt;/para&gt;      &lt;para&gt;&lt;see cref=&quot;ResponseBody&quot;/&gt; &#x622A;&#x65AD; 500&#xFF08;&#x51FA;&#x7AD9;&#x54CD;&#x5E94;&#x539F;&#x6837;&#x622A;&#x65AD;&#x7559;&#x5B58;&#xFF09;&#xFF1B;&lt;see cref=&quot;ErrorMsg&quot;/&gt; &#x8131;&#x654F; 1000&#xFF08;&#x5F02;&#x5E38;&#x6D88;&#x606F;&#x4E0D;&#x542B;&#x660E;&#x6587;&#x51ED;&#x8BC1;&#xFF09;&#x3002;&lt;/para&gt;      &lt;para&gt;&#x65E5;&#x5FD7;&#x6309; &lt;see cref=&quot;ChannelId&quot;/&gt; &#x2B; &lt;see cref=&quot;MsgId&quot;/&gt; &#x68C0;&#x7D22;&#xFF08;&#x975E;&#x552F;&#x4E00;&#x7D22;&#x5F15;&#x2014;&#x2014;&#x540C;&#x4E00;&#x6D88;&#x606F;&#x591A;&#x8DEF;&#x7531;/&#x91CD;&#x8BD5;&#x53EF;&#x591A;&#x884C;&#xFF09;&#x3002;&lt;/para&gt;</summary>
// 提示：标准 CRUD 逻辑和构造函数已由 MessageForwardLogEntityDataService.g.cs 承载。
// 这里的分部类仅用于编写特定的业务查询方法。
//
// 【V4.9.40 架构说明】
// - DataService 为 sealed 分部类，.g.cs 承载所有 CRUD 和搜索能力
// - 自定义业务逻辑 → 在 `Services/` 下创建 Service 类，标注 `[GenerateController]`
// - 数据过滤干预 → 实现 IGlobalQueryFilter（初始化器基类 DomainHostInitializerBase 已实现，override Apply<T> 即可）
// - 不要手动创建 Controller 类（V3.7 起由 SG 自动生成）
 partial class MessageForwardLogEntityDataService(IDomainUser user, IEntityDAC<MessageForwardLogEntity> dac)
        : DomainDataServiceBase<MessageForwardLogEntity, MessageForwardLogEntityDto>(user, dac, hasSoftDelete:false) 
{
    /// <summary>转发日志分页查询（ChannelId 必过滤；MsgId/Status/CreateTime 半开区间过滤可空——管理端对账检索）。</summary>
    /// <param name="channelId">公众号隔离 id（必过滤）。</param>
    /// <param name="msgId">微信消息 id 精确过滤（可空 = 不按 MsgId）。</param>
    /// <param name="status">转发状态过滤（<see cref="WeChatMessageForwardLogStatus"/>——可空 = 全部状态）。</param>
    /// <param name="from">CreateTime 下界（闭——可空 = 不限）。</param>
    /// <param name="to">CreateTime 上界（闭——可空 = 不限）。</param>
    /// <param name="page">页码（1 起）。</param>
    /// <param name="size">每页条数。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<List<MessageForwardLogEntity>> QueryForwardLogsAsync(
        string channelId, string? msgId = null, int? status = null, DateTime? from = null, DateTime? to = null,
        int page = 1, int size = 50, CancellationToken ct = default)
        => await EntitySelectAsync(
            l => l.ChannelId == channelId
                && (msgId == null || l.MsgId == msgId)
                && (status == null || l.Status == status.Value)
                && (from == null || l.CreateTime >= from.Value)
                && (to == null || l.CreateTime <= to.Value),
            (page < 1 ? 1 : page - 1) * size, size,
            q => q.OrderByDescending(l => l.Id),
            ct);

    /// <summary>转发日志计数（与 <see cref="QueryForwardLogsAsync"/> 同过滤条件——分页总条数）。</summary>
    /// <param name="channelId">公众号隔离 id（必过滤）。</param>
    /// <param name="msgId">微信消息 id 精确过滤（可空）。</param>
    /// <param name="status">转发状态过滤（可空）。</param>
    /// <param name="from">CreateTime 下界（闭——可空）。</param>
    /// <param name="to">CreateTime 上界（闭——可空）。</param>
    /// <param name="ct">取消令牌。</param>
    public async Task<long> CountForwardLogsAsync(
        string channelId, string? msgId = null, int? status = null, DateTime? from = null, DateTime? to = null,
        CancellationToken ct = default)
        => await CountAsync(
            l => l.ChannelId == channelId
                && (msgId == null || l.MsgId == msgId)
                && (status == null || l.Status == status.Value)
                && (from == null || l.CreateTime >= from.Value)
                && (to == null || l.CreateTime <= to.Value),
            ct);
}
