using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 异步抄送门面（BackgroundJob 队列——方案 §十·三 ④ 契约定位：<b>门面形态（用户裁定）</b>——接口标
/// <see cref="IDomainService"/> + 实现继承 <see cref="TKW.Framework.Domain.DomainServiceBase"/> +
/// <c>AddConstructibleService</c> 注册（守卫工厂：帧内 CurrentAopUser 供给 ctor IDomainUser，帧外解析抛守卫——
/// fail-fast 非静默降级）。入队语义固定，实现层随机制替换（Outbox/内存/EventBus 均可落为同一契约——
/// 本期定案 <see cref="IBackgroundJobManager"/> 持久化队列）。
/// <para>入队数据访问（转发日志 Status=Queued 落库）经基类 <c>User.Use&lt;MessageForwardLogEntityDataService&gt;()</c>
/// 懒加载（DI004 合规，禁 sp.GetRequiredService C1 绕行——普通 DI 特例仅限主框架/Abstractions 契约实现等
/// 接口不可改场景，本扩展自有门面一律守卫工厂）。</para>
/// <para>消费 <see cref="IBackgroundJobManager"/> 为用户裁定"极个别特例"（框架契约非 IDomainService 不可改接口）——
/// ctor 注 <see cref="System.IServiceProvider"/> + 惰性解析，调用点保证在域帧内（handler RouteAsync 帧内调用）。</para>
/// </summary>
public interface IMessageForwardDispatcher : IDomainService
{
    /// <summary>
    /// 异步抄送入队：落转发日志 <see cref="MessageForwardLogEntity"/>（Status=<see cref="WeChatMessageForwardLogStatus.Queued"/>）
    /// → <see cref="IBackgroundJobManager"/> 入队 <see cref="WeChatMessageForwardJob"/>（幂等键透传下游供其自行去重，
    /// at-least-once 语义方案 §十·三 ③）。只保证"已受理"（同步落库/入队即返回，不阻塞微信回包）。
    /// </summary>
    /// <param name="channelId">公众号 channel 实例 id。</param>
    /// <param name="endpointId">命中的下游端点配置 id（<see cref="DownstreamEndpointEntity"/>）。</param>
    /// <param name="rawXml">消息副本原始 XML（快照——防下游再查微信；方案 §十·二 ④ 原样透传）。</param>
    /// <param name="msgId">微信 MsgId（可空——事件类消息无 MsgId）。</param>
    /// <param name="fromUserName">发送者 openid。</param>
    /// <param name="msgType">微信消息类型（text/image/.../event——转发日志 MessageType 留痕；可空）。</param>
    /// <param name="matchRuleId">命中路由规则 id（转发日志 MatchRuleId 留痕——可空）。</param>
    /// <param name="forwardMode">命中规则转发模式（转发日志 ForwardMode 快照——LocalOnly 恒 0）。</param>
    /// <param name="idempotencyKey">规范化幂等键（透传下游供其自行幂等）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>转发日志 Id（供幂等表 <see cref="WeChatMessageIdempotencyEntity.ForwardLogId"/> 关联）。</returns>
    Task<long> EnqueueAsync(
        string channelId, long endpointId, string rawXml, string? msgId, string fromUserName,
        string? msgType, long? matchRuleId, int forwardMode, string idempotencyKey, CancellationToken ct = default);
}
