using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
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
            foreach (var handler in _handlers)
            {
                if (handler is IChannelSpecificEventReplyHandler specific
                    && string.Equals(specific.ChannelId, channelId, StringComparison.Ordinal))
                {
                    var reply = await handler.HandleAsync(dispatch, ct);
                    if (reply is not null)
                        return reply;
                }
            }
            foreach (var handler in _handlers)
            {
                if (string.Equals(handler.ChannelType, channelType, StringComparison.Ordinal))
                {
                    var reply = await handler.HandleAsync(dispatch, ct);
                    if (reply is not null)
                        return reply;
                }
            }

            return null;
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
}
