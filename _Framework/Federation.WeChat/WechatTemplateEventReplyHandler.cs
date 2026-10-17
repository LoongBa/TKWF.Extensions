using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 模板驱动 handler（方案 docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md §3.3——
/// 消费方<b>显式注册</b>，Oracle P0-1：平台库不默认注册 handler 实例防过度设计）。
/// <para>覆盖 <see cref="WechatEventReplyHandlerBase.OnDefaultAsync"/>——读
/// <c>Channel.Extra["news_reply"]</c> 模板构造图文（占位符 <c>{uid}/{openid}/{channel_id}/{channel_type}</c> 替换）；
/// 模板<b>缺失 / 损坏 / Enabled=false → 返回 null</b>（不回复不拦截——Oracle P0-2）。</para>
/// <para><b>无签票耦合</b>（Oracle 方案 §3.3"无签密封票据耦合"）：本 handler 为纯图文回复场景，
/// <c>link</c> 模板的 <c>{ticket}</c> 占位符<b>不替换（保留原样）</b>——签票前不可用，
/// 业务 handler 需签票时选用主包 <see cref="EventReplyHandlerBase"/>（签票专用基类）或
/// <c>User.Use&lt;ISealedTicketService&lt;FederatedIdentity&gt;&gt;()</c> 懒加载签票后自行替换。</para>
/// <para>注册（消费方）：<c>services.TryAddEnumerableConstructible&lt;IEventReplyHandler, WechatTemplateEventReplyHandler&gt;()</c>；
/// 与 <see cref="WechatProbeEventReplyHandler"/> <b>互斥使用</b>（Oracle P2-5——同时注册时 first-non-null-wins，
/// 幂等语义，使用指南声明）。</para>
/// </summary>
[DiContractIgnore]
public sealed class WechatTemplateEventReplyHandler : WechatEventReplyHandlerBase
{
    /// <summary>
    /// 构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser——logger 可空容错 NullLogger，
    /// 对齐 <see cref="WechatEventReplyHandlerBase"/> 形态）。
    /// </summary>
    public WechatTemplateEventReplyHandler(IDomainUser user, ILogger? logger = null) : base(user, logger)
    {
    }

    /// <summary>
    /// 兜底分支（模板驱动）：读 <c>Channel.Extra["news_reply"]</c> 模板 → 构造图文回复载荷。
    /// <para><b>模板缺失/损坏 → null</b>（<see cref="ReadNewsTemplate"/> Oracle P0-2）；<c>Enabled ≠ true</c> → null
    /// （不回复不拦截——业务 handler 正常流转）；<c>Channel</c> 未富化（registry 查不到）→ <c>Channel.Extra</c> 为
    /// null → 模板读取 null → 不回复。</para>
    /// <para>占位符替换非 ticket 项（<c>{uid}/{openid}/{channel_id}/{channel_type}</c>——<see cref="ReplacePlaceholders(string,WechatEventContext)"/>）；
    /// <c>TtlSeconds</c> 无签票业务语义忽略（字段保留模板一致性）。</para>
    /// </summary>
    protected override Task<WechatReplyPayload?> OnDefaultAsync(WechatEventContext ctx, CancellationToken ct)
    {
        // 模板读取（Extra["news_reply"]——通道级图文配置；缺失/损坏 → null，Oracle P0-2）
        var template = ReadNewsTemplate(ctx.Channel?.Extra);
        if (template is null or { Enabled: not true })
            return Task.FromResult<WechatReplyPayload?>(null);

        // 占位符替换（{uid}/{openid}/{channel_id}/{channel_type}；{ticket} 不替换——无签票耦合，方案 §3.3）
        var title = ReplacePlaceholders(template.Title ?? string.Empty, ctx);
        var description = ReplacePlaceholders(template.Description ?? string.Empty, ctx);
        var link = ReplacePlaceholders(template.Link ?? string.Empty, ctx);

        WechatReplyPayload payload = new(News:
        [
            new EventReplyNewsArticle(title, description, link, template.PicUrl),
        ]);
        return Task.FromResult<WechatReplyPayload?>(payload);
    }
}