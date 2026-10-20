using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 事件编排门面（外部身份联邦 E3——ADR-Federation-外部身份联邦与密封票据适配 E3 评审 F7/F8；V0.1.0 自
/// Federation 主包迁入 Abstractions 契约包——namespace 保持 <c>TKWF.Ext.Federation</c> 跨程序集同 namespace）。
/// <para>职责：按 channelId 构造通道 → <c>channel.HandleEventAsync</c> 取<b>平台化回复</b>（微信 XML 等——
/// 平台库通道实现；null = 无平台回复）→ 委托 <see cref="IEventReplyHandler"/> 集合
/// first-non-null-wins（消费方业务回复）→ 返回首个非 null <see cref="EventReplyResult"/>。
/// 编排内经 <c>IExternalIdentityService.GetByExternalIdAsync</c> 查 uid（<b>只读不建号</b>——评审 F7，
/// 不调 LinkAsync）→ 富化 <c>Parameters["uid"]</c> 供 handler 签票据。</para>
/// <para><b>handler 迭代边界（Oracle P2-3 记录）</b>：handler 委托仅对<b>未实现
/// <c>HandleEventAsync</c>（恒返回 null）的通道</b>兜底——已平台化自处理回复的通道（如
/// <c>WeChatEventChannel</c> 恒返回非 null，空回复兜底）对 handler 迭代<b>不可达</b>；
/// 两处迭代（通道自处理 + 编排器 handler）设计可接受，职责各归其位。</para>
/// <para>注册：<c>AddConstructibleService&lt;IEventOrchestrator, EventOrchestrator&gt;()</c>——门面标准形态；
/// 装配层（FederationWebExtension.HandleEvent）经 <c>guest.Use&lt;IEventOrchestrator&gt;()</c> 帧内解析。
/// <see cref="IEventReplyHandler"/> 集合 ctor 注入（ADR92 集合版守卫工厂帧内供给——消费方注册，空集合自然跳过）。</para>
/// <para>异常收敛：非取消异常 → 日志 + 返回 null（装配层转 200 空回复——微信 5 秒超时约束，平台回调安全兜底，
/// 异步客服消息归 Phase 2/消费方）。</para>
/// </summary>
public interface IEventOrchestrator : IDomainService
{
    /// <summary>事件编排：平台化回复 → 无平台回复时委托 handler（first-non-null-wins）；全部 null → null（装配层空回复兜底）。</summary>
    /// <param name="channelId">通道实例键（registry 选区）。</param>
    /// <param name="channelType">目标通道类型（如 <c>wechat_event</c>——平台段推导 <c>{PlatformType}_event</c>）。</param>
    /// <param name="context">事件上下文（含 __raw/query/channel_id + 验签解密富化 + uid 只读富化）。</param>
    Task<EventReplyResult?> OrchestrateAsync(string channelId, string channelType, SsoChannelEventContext context, CancellationToken ct = default);
}