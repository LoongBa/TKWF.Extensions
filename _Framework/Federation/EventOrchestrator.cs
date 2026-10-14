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
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 事件编排门面（外部身份联邦 E3——ADR-Federation-外部身份联邦与密封票据适配 E3 评审 F7/F8）。
/// <para>职责：按 channelId 构造通道 → <c>channel.HandleEventAsync</c> 取<b>平台化回复</b>（微信 XML 等——
/// 平台库通道实现；null = 无平台回复）→ 委托 <see cref="IEventReplyHandler"/> 集合
/// first-non-null-wins（消费方业务回复）→ 返回首个非 null <see cref="EventReplyResult"/>。
/// 编排内经 <see cref="ISsoChannelMapService.GetByChannelAsync"/> 查 uid（<b>只读不建号</b>——评审 F7，
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

/// <summary>
/// 事件编排门面实现（外部身份联邦 E3）。内部组合：<see cref="ISsoChannelFactory"/> 经基类 <c>User.Use</c> 懒加载
/// （DI004 零豁免——领域服务间调用经 User.Use，不 ctor 注入）；uid 查询经 <see cref="ISsoChannelMapService"/>
/// （AuthCenter.Abstractions 契约——只读消费，不加方法）。
/// </summary>
[DiContractIgnore]
public sealed class EventOrchestrator : DomainServiceBase, IEventOrchestrator
{
    private readonly IServiceProvider _sp;
    private readonly IReadOnlyList<IEventReplyHandler> _handlers;
    private readonly ILogger<EventOrchestrator> _logger;
    private ISsoChannelFactory? _factory;
    private ISsoChannelMapService? _channelMap;

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
    public async Task<EventReplyResult?> OrchestrateAsync(string channelId, string channelType, SsoChannelEventContext context, CancellationToken ct = default)
    {
        try
        {
            var channel = await Factory.CreateAsync(channelId, channelType, ct);
            if (channel is null)
            {
                _logger.LogWarning("事件编排选区失败：ChannelId={ChannelId}（CHANNEL_NOT_FOUND）", channelId);
                return null;
            }

            // uid 只读查询（评审 F7——GetByChannelAsync 只读不建号，不调 LinkAsync）→ 富化 Parameters["uid"] 供 handler 签票据
            if (context.Parameters != null
                && context.Parameters.TryGetValue("external_user_id", out var externalUserId)
                && !string.IsNullOrEmpty(externalUserId))
            {
                var map = await ChannelMap.GetByChannelAsync(channelId, externalUserId, ct);
                if (map is not null && context.Parameters is Dictionary<string, string?> dict)
                    dict["uid"] = map.UId;
            }

            // 平台化回复（微信 XML 等——平台库通道实现；null = 无平台回复，走 handler 委托）
            var platformReply = await channel.HandleEventAsync(context, ct);
            if (platformReply is not null)
                return platformReply;

            // handler first-non-null-wins（评审 F8——按 ChannelType 匹配；消费方业务回复）
            foreach (var handler in _handlers)
            {
                if (!string.Equals(handler.ChannelType, channelType, StringComparison.Ordinal))
                    continue;
                var reply = await handler.HandleAsync(context, ct);
                if (reply is not null)
                    return reply;
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
    private ISsoChannelMapService ChannelMap => _channelMap ??= User.Use<ISsoChannelMapService>();
}
