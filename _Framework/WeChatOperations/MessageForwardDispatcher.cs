using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.BackgroundJobs;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.WeChatOperations;

/// <inheritdoc />
[DiContractIgnore]
internal sealed class MessageForwardDispatcher : DomainServiceBase, IMessageForwardDispatcher
{
    private readonly IServiceProvider _sp;
    private readonly ILogger? _logger;

    private MessageForwardLogEntityDataService? _logDs;
    private MessageForwardLogEntityDataService LogDs => _logDs ??= User.Use<MessageForwardLogEntityDataService>();

    /// <summary>
    /// 构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser；<see cref="IServiceProvider"/> 惰性解析
    /// <see cref="IBackgroundJobManager"/>——用户裁定"极个别特例"：框架契约非 IDomainService 不可改接口，
    /// 非域基础设施注入；<b>调用点保证在域帧内</b>（handler RouteAsync 帧内调用 EnqueueAsync）。
    /// </summary>
    public MessageForwardDispatcher(IDomainUser user, IServiceProvider sp, ILogger? logger = null) : base(user)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<long> EnqueueAsync(
        string channelId, long endpointId, string rawXml, string? msgId, string fromUserName,
        string? msgType, long? matchRuleId, int forwardMode, string idempotencyKey, CancellationToken ct = default)
    {
        // ① 转发日志 Status=Queued（方案 §十·三 ⑤——幂等表在前、异步投递在后；Queued 行承载命中规则快照）
        var now = DateTime.UtcNow;
        var log = new MessageForwardLogEntity
        {
            ChannelId = channelId,
            MsgId = msgId,
            FromUserName = fromUserName,
            MessageType = msgType ?? string.Empty,
            MatchRuleId = matchRuleId,
            ForwardMode = forwardMode,
            DownstreamEndpointId = endpointId,
            Status = WeChatMessageForwardLogStatus.Queued,
            CreateTime = now,
            UpdateTime = now,
        };
        var created = await LogDs.EntityCreateAsync(log, ct);

        // ② 入队（BackgroundJob 持久化队列——TKWF_JobRecord 重启续投 + 重试 MaxRetryCount + SystemActor 自动绑定；
        //    消费方须 AddBackgroundJobs<TUserInfo>() 接线——缺接线 GetRequiredService 抛，网关 fail-safe 降级捕获）
        var jobs = _sp.GetRequiredService<IBackgroundJobManager>();
        await jobs.EnqueueAsync<WeChatMessageForwardJob>(
            new WeChatMessageForwardJobArgs(channelId, endpointId, rawXml, msgId, fromUserName, idempotencyKey, created.Id));
        return created.Id;
    }
}
