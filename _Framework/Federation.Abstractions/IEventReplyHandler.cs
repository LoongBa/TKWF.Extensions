using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>
/// 事件回复处理器契约（外部身份联邦 E3——ADR-Federation-外部身份联邦与密封票据适配 E3 评审 F8）。
/// <para>消费方实现——事件回复是<b>业务决策</b>（Federation 只提供契约 + 编排，默认不注册任何 handler；
/// 平台库亦不注册——防过度设计）；编排按 <see cref="ChannelType"/> 匹配 + first-non-null-wins。</para>
/// <para>注册：消费方 <c>services.TryAddEnumerableConstructible&lt;IEventReplyHandler, X&gt;()</c>
/// （ADR92 集合版守卫工厂——对齐 ISsoChannel 集合注册形态）。</para>
/// <para>典型用途：验签已通过的事件（<c>external_user_id</c>/<c>uid</c> 已在
/// <see cref="SsoChannelEventContext.Parameters"/>——编排层 uid 只读查询富化）→ handler 经
/// <c>ISealedTicketService&lt;FederatedIdentity&gt;</c> 签票据 → 返回图文/文本链接回复
/// （<see cref="EventReplyResult"/>——微信 XML 信封由平台库通道 WeChatEventChannel 包裹）。</para>
/// </summary>
public interface IEventReplyHandler : IDomainService
{
    /// <summary>匹配的通道类型（wechat_event 等——编排按 ChannelType 过滤）。</summary>
    string ChannelType { get; }

    /// <summary>处理事件并返回回复；返回 null = 不处理（first-non-null-wins 语义——编排取首个非 null）。</summary>
    Task<EventReplyResult?> HandleAsync(SsoChannelEventContext context, CancellationToken ct = default);
}
