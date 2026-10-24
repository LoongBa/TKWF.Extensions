using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Events;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 事件编排门面实现（外部身份联邦 E3）。内部组合：<see cref="ISsoChannelFactory"/> 经基类 <c>User.Use</c> 懒加载
/// （DI004 零豁免——领域服务间调用经 User.Use，不 ctor 注入）；uid 查询经 <see cref="IExternalIdentityService"/>
/// （AuthCenter.Abstractions 契约——V0.11.0 T4 由 ISsoChannelMapService 收口，只读消费不加方法）。
/// <para>契约 <see cref="IEventOrchestrator"/> 已迁 Abstractions 契约包（V0.1.0 ④ 裁定——namespace 保持
/// <c>TKWF.Ext.Federation</c> 跨程序集同 namespace），本实现类继续在 Federation 主包实现该契约。</para>
/// </summary>
[DiContractIgnore]
public sealed class EventOrchestrator : DomainServiceBase, IEventOrchestrator
{
    private readonly IServiceProvider _sp;
    private readonly IReadOnlyList<IEventReplyHandler> _handlers;
    private readonly ILogger<EventOrchestrator> _logger;
    private ISsoChannelFactory? _factory;
    private IExternalIdentityService? _channelMap;
    private IFollowStateService? _followState;

    /// <summary>排他校验缓存（键 = handler 类型名集合，值 = 已校验）——同一装配集每进程仅校验一次。</summary>
    private static readonly ConcurrentDictionary<string, bool> _exclusivityCache = new();

    /// <summary>构造（IDomainUser 经守卫工厂帧内供给；IEventReplyHandler 集合 ctor 注入——ADR92 集合版守卫工厂）。</summary>
    public EventOrchestrator(
        IDomainUser user,
        IServiceProvider serviceProvider,
        IEnumerable<IEventReplyHandler> handlers,
        ILogger<EventOrchestrator> logger)
        : base(user)
    {
        _sp = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _handlers = handlers?.ToList() ?? throw new ArgumentNullException(nameof(handlers));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        ValidateExclusivity(_handlers);
    }

    /// <summary>
    /// 排他单模型装配校验（v0.12.0——fail-fast）：同一 ChannelType 至多一个<b>业务</b> handler。
    /// 排除：<see cref="DiagnosticEventReplyHandlerAttribute"/> 标记（探针/诊断）+ 
    /// <see cref="IChannelSpecificEventReplyHandler"/>（W3 通道绑定桶——精确覆盖，非默认链）。
    /// 违反 → 抛装配错误（把"多 handler 并存的行为不确定性"变为配置即错）。
    /// </summary>
    private static void ValidateExclusivity(IReadOnlyList<IEventReplyHandler> handlers)
    {
        var key = string.Join("|", handlers.Select(h => h.GetType().FullName).OrderBy(n => n, StringComparer.Ordinal));
        _exclusivityCache.GetOrAdd(key, _ =>
        {
            var conflicts = handlers
                .Where(h => h.GetType().GetCustomAttribute<DiagnosticEventReplyHandlerAttribute>() is null)
                .Where(h => h is not IChannelSpecificEventReplyHandler)
                .GroupBy(h => h.ChannelType, StringComparer.Ordinal)
                .Where(g => g.Count() > 1)
                .ToList();
            if (conflicts.Count > 0)
            {
                var detail = string.Join("; ", conflicts.Select(g =>
                    $"{g.Key}: [{string.Join(", ", g.Select(h => h.GetType().Name))}]"));
                throw new InvalidOperationException(
                    $"事件回复 handler 排他性违反——每个 ChannelType 至多一个业务 handler（探针/诊断除外）：" +
                    $"冲突：{detail}。请删除多余注册或改为继承默认 handler（WechatTemplateEventReplyHandler）。");
            }
            return true;
        });
    }

    /// <inheritdoc />
    public async Task<EventReplyResult?> OrchestrateAsync(string channelId, string channelType, ChannelInboundContext context, CancellationToken ct = default)
    {
        try
        {
            var channel = await Factory.CreateAsync(channelId, channelType, ct);
            if (channel is null)
            {
                _logger.LogWarning("事件编排选区失败：ChannelId={ChannelId}（CHANNEL_NOT_FOUND）", channelId);
                return null;
            }

            // I2 语义层（方案 §5.1 时序）：通道解析 → outcome 三态分派
            var outcome = await channel.HandleEventAsync(context, ct);
            if (outcome.State == ChannelEventOutcomeState.Rejected)
            {
                // 信任根失败/解密失败——编排短路（不记录、不回复、不回退 handler；装配层 200 空回复兜底）
                _logger.LogWarning("事件编排拒绝（信任根）：ChannelId={ChannelId} ChannelType={ChannelType}",
                    channelId, channelType);
                return null;
            }
            if (outcome.State == ChannelEventOutcomeState.NotApplicable || outcome.Event is null)
            {
                // 非事件通道/无事件语义——跳过（无 handler 迭代：dispatch 不存在）
                _logger.LogDebug("事件编排跳过（非事件）：ChannelId={ChannelId} ChannelType={ChannelType}",
                    channelId, channelType);
                return null;
            }
            var evt = outcome.Event;

            // uid 只读查询（评审 F7——GetByExternalIdAsync 只读不建号，不调 LinkAsync）→ dispatch 承载供 handler 决策
            string? uid = null;
            if (!string.IsNullOrEmpty(evt.ExternalUserId))
            {
                var map = await ChannelMap.GetByExternalIdAsync(channelId, evt.ExternalUserId, ct);
                if (map is not null)
                {
                    uid = map.UId;
                    _logger.LogInformation("事件编排 uid 命中：ChannelId={ChannelId} ExternalUserId={ExternalUserId} UId={UId}",
                        channelId, evt.ExternalUserId, map.UId);
                }
                else
                {
                    _logger.LogWarning("事件编排 uid 未命中（未绑定 → handler 仍可回复，UId=null）：ChannelId={ChannelId} ExternalUserId={ExternalUserId}",
                        channelId, evt.ExternalUserId);
                }
            }
            else
            {
                _logger.LogWarning("事件编排缺 ExternalUserId：ChannelId={ChannelId}", channelId);
            }
            var dispatch = new ChannelEventDispatch(evt, uid);

            // handler 委托（W3 双桶解析——2026-10-16 两级模型）：
            //   桶 1：通道特定绑定（实现 IChannelSpecificEventReplyHandler 且 ChannelId 精确匹配——first-non-null-wins）
            //   桶 2：ChannelType 匹配（现状语义——默认共用；绑定桶全 null 时回退）
            //   绑定桶优先（精确覆盖）——消费方为特定 channelId 定制 handler，其余通道走类型桶默认。
            EventReplyResult? reply = null;
            foreach (var handler in _handlers)
            {
                if (handler is IChannelSpecificEventReplyHandler specific
                    && string.Equals(specific.ChannelId, channelId, StringComparison.Ordinal))
                {
                    var candidate = await handler.HandleAsync(dispatch, ct);
                    if (candidate is not null)
                    {
                        reply = candidate;
                        break;
                    }
                }
            }
            if (reply is null)
            {
                foreach (var handler in _handlers)
                {
                    if (string.Equals(handler.ChannelType, channelType, StringComparison.Ordinal))
                    {
                        var candidate = await handler.HandleAsync(dispatch, ct);
                        if (candidate is not null)
                        {
                            reply = candidate;
                            break;
                        }
                    }
                }
            }

            // R3 记录在后（§5.2）：handler 业务写已成功执行，记录冲突只影响自身与提交阶段，
            // 回复内容已生成——并发重投的 DedupeKey 冲突不再让 handler 的写先中毒。
            // 记录失败（DB 故障）→ 吞异常 + 日志，回复照常（微信重投补齐）。
            try
            {
                var recordResult = await FollowState.RecordAsync(evt, ct);
                if (recordResult.StateChanged)
                {
                    await PublishFollowChangedAsync(evt, uid, recordResult.State?.IsFollowing);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogError(ex, "关注状态记录失败（不影响回复——微信重投补齐）：ChannelId={ChannelId} ExternalUserId={ExternalUserId}",
                    channelId, evt.ExternalUserId);
            }

            return reply;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 事件编排异常收敛——装配层转 200 空回复（微信 5 秒超时约束；异步客服消息归 Phase 2/消费方）
            _logger.LogError(ex, "事件编排异常：ChannelId={ChannelId}", channelId);
            return null;
        }
    }

    private ISsoChannelFactory Factory => _factory ??= User.Use<ISsoChannelFactory>();
    private IExternalIdentityService ChannelMap => _channelMap ??= User.Use<IExternalIdentityService>();
    private IFollowStateService FollowState => _followState ??= User.Use<IFollowStateService>();

    /// <summary>发布关注变化域事件（§3.7——仅 StateChanged==true；UId 未绑定 null 照常发；AOP 帧内收集提交后派发）。</summary>
    private async Task PublishFollowChangedAsync(ChannelEvent evt, string? uid, bool? isFollowing)
    {
        var bus = User.GetService<ILocalEventBus>();
        await bus.PublishAsync(new FollowChangedEvent(
            ChannelId: evt.ChannelId,
            ChannelType: evt.ChannelType,
            ExternalUserId: evt.ExternalUserId,
            UnionId: evt.UnionId,
            UId: uid,
            IsFollowing: isFollowing ?? IsFollow(evt.EventType),
            EventType: evt.EventType,
            EventKey: evt.EventKey,
            Source: evt.Source,
            OccurredAt: evt.OccurredAt));
    }

    /// <summary>关注域事件类型（follow / scan_follow = 关注；unfollow = 取关——与 FollowStateService 判据同型）。</summary>
    private static bool IsFollow(string eventType)
        => eventType is "follow" or "scan_follow";
}
