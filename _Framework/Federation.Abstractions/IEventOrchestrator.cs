using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>
/// 事件编排门面（外部身份联邦 E3——ADR-Federation-外部身份联邦与密封票据适配 E3 评审 F7/F8；V0.1.0 自
/// Federation 主包迁入 Abstractions 契约包——namespace 保持 <c>TKWF.Ext.Federation</c> 跨程序集同 namespace）。
/// <para>职责（I2 语义层 §5.1）：按 channelId 构造通道 → <c>channel.HandleEventAsync</c> 解析为
/// <see cref="ChannelEventOutcome"/>（<see cref="ChannelEventOutcomeState.Rejected"/> 短路；<see cref="ChannelEventOutcomeState.NotApplicable"/> 跳过）→
/// 经 <c>IExternalIdentityService.GetByExternalIdAsync</c> 查 uid（<b>只读不建号</b>——评审 F7，不调 LinkAsync）→
/// 委托 <see cref="IEventReplyHandler"/> 集合 first-non-null-wins（消费方业务回复）→ 返回首个非 null <see cref="EventReplyResult"/>；
/// 记录关注状态（R3：记录在回复之后）+ 状态变化时发布 <c>FollowChangedEvent</c>。</para>
/// <para><b>handler 迭代边界（Oracle P2-3 记录 + I2 语义层）</b>：handler 委托仅对<b>有事件语义的通道</b>
/// （<see cref="ChannelEventOutcomeState.Parsed"/>）兜底——非事件通道（<see cref="ChannelEventOutcomeState.NotApplicable"/>）
/// 直接跳过；已解析通道产生回复后仍走 handler 迭代（V0.4.0 起通道不产回复，回复由 handler 层统一产出）。</para>
/// <para>注册：<c>AddConstructibleService&lt;IEventOrchestrator, EventOrchestrator&gt;()</c>——门面标准形态；
/// 装配层（FederationWebExtension.HandleEvent）经 <c>guest.Use&lt;IEventOrchestrator&gt;()</c> 帧内解析。
/// <see cref="IEventReplyHandler"/> 集合 ctor 注入（ADR92 集合版守卫工厂帧内供给——消费方注册，空集合自然跳过）。</para>
/// <para>异常收敛：非取消异常 → 日志 + 返回 null（装配层转 200 空回复——微信 5 秒超时约束，平台回调安全兜底，
/// 异步客服消息归 Phase 2/消费方）。</para>
/// </summary>
public interface IEventOrchestrator : IDomainService
{
    /// <summary>事件编排：解析 → uid 解析 → handler first-non-null-wins → 记录关注状态（R3 记录在后）→ 返回回复或 null。</summary>
    /// <param name="channelId">通道实例键（registry 选区）。</param>
    /// <param name="channelType">目标通道类型（如 <c>wechat_event</c>——平台段推导 <c>{PlatformType}_event</c>）。</param>
    /// <param name="context">入站报文上下文（传输层——原始 body/query/channel_id；业务语义已在 ChannelEvent 内）。</param>
    Task<EventReplyResult?> OrchestrateAsync(string channelId, string channelType, ChannelInboundContext context, CancellationToken ct = default);
}