using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 系统状态聚合门面（DMP-Lite 5.8 "系统状态"页四张状态卡聚合——公众号连接状态 / 今日消息量 /
/// 自动回复命中率 / 转发成功率）。
/// <para>职责：只读聚合查询（<see cref="GetStatusSummaryAsync"/> 一次返回四卡数据源）——
/// ① <b>公众号连接状态</b>：channel 配置存在（<see cref="IChannelConfigService.ListAsync"/> 按 ChannelId 命中）
/// 且凭证可解析（DTO <see cref="WeChatChannelConfigDto.HasCredentials"/>——AES-GCM 密文非空）→ Connected=true；
/// ② <b>今日消息量 / 自动回复命中率</b>：<see cref="InteractionLogEntity"/> 当日（UTC 自然日半开区间）
/// 消息计数 + 有回复记录（<see cref="InteractionLogEntity.ReplyContent"/> 非空 或
/// <see cref="InteractionLogEntity.ReplyMaterialId"/> 非空）占比；③ <b>转发成功率</b>：
/// <see cref="MessageForwardLogEntity"/> 当日 Success ÷ (Success+Fail+Timeout)——
/// Queued/SkippedDuplicate/NoRule 双端排除（未决/非转发决策不计入成功分母）。</para>
/// <para>铁律（tkwf-extension）：接口 <c>: IDomainService</c> + 实现 <c>internal sealed : DomainServiceBase</c>
/// + <c>[DiContractIgnore]</c> + <c>AddConstructibleService</c> 注册（守卫工厂帧内供给 ctor IDomainUser）；
/// DataService 经基类 <c>User.Use&lt;具体 DataService&gt;()</c> 懒加载（DI004 零豁免）；数据访问红线——
/// 仅经 SG1 生成 DataService 的 <c>CountAsync</c>/<c>EntitySelectAsync</c> 原语，零裸 ORM/IEntityDAC。</para>
/// <para>安全边界：DTO（<see cref="WeChatOperationsStatusDto"/>）只含状态布尔/计数/比率——<b>不含任何凭证
/// 明文/密文</b>（Oracle P1 DTO 投影安全；连接判定经 <see cref="WeChatChannelConfigDto.HasCredentials"/>
/// 布尔投影，解密仅出站瞬时归 <see cref="DbWeChatCredentialResolver"/>）。</para>
/// </summary>
public interface IWeChatOperationsStatusService : IDomainService
{
    /// <summary>
    /// 系统状态聚合查询（DMP-Lite 5.8 四卡——公众号连接状态/今日消息量/自动回复命中率/转发成功率）。
    /// 当日口径 = <b>UTC 自然日</b>（<c>[DateTime.UtcNow.Date, +1 天)</c> 半开区间——跨时区部署语义一致）。
    /// </summary>
    /// <param name="channelId">公众号隔离 id（未配置 → 全部返回未连接 + 零值，不抛）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>四卡聚合结果（<see cref="WeChatOperationsStatusDto"/>——只读、无凭证）。</returns>
    [RequirePermission("WeChatOperations.Query")]
    Task<WeChatOperationsStatusDto> GetStatusSummaryAsync(string channelId, CancellationToken ct = default);
}

/// <inheritdoc />
[DiContractIgnore]
internal sealed class WeChatOperationsStatusService : DomainServiceBase, IWeChatOperationsStatusService
{
    private readonly ILogger? _logger;

    private IChannelConfigService? _channelConfig;
    private IChannelConfigService ChannelConfig => _channelConfig ??= User.Use<IChannelConfigService>();
    private InteractionLogEntityDataService? _interactionLog;
    private InteractionLogEntityDataService InteractionLog => _interactionLog ??= User.Use<InteractionLogEntityDataService>();
    private MessageForwardLogEntityDataService? _forwardLog;
    private MessageForwardLogEntityDataService ForwardLog => _forwardLog ??= User.Use<MessageForwardLogEntityDataService>();

    /// <summary>
    /// 构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser；logger 可空——非域基础设施，
    /// 零 DataService/门面构造注入（DI004）——ChannelConfig 经 <see cref="User"/> 懒加载 AOP 解析）。
    /// </summary>
    public WeChatOperationsStatusService(IDomainUser user, ILogger? logger = null) : base(user)
    {
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task<WeChatOperationsStatusDto> GetStatusSummaryAsync(string channelId, CancellationToken ct = default)
    {
        // ① 公众号连接状态（只读、凭证-free——经 DTO HasCredentials 布尔投影，密文/明文均不回显）
        var connected = false;
        string? detail = "no channel config";
        if (!string.IsNullOrWhiteSpace(channelId))
        {
            var channel = (await ChannelConfig.ListAsync(ct)).FirstOrDefault(c => c.ChannelId == channelId);
            if (channel is not null && channel.HasCredentials)
            {
                connected = true;
                detail = "channel config found";
            }
            else if (channel is not null)
            {
                detail = "channel config found but credentials missing";
            }
        }

        // ② 今日消息量 + 自动回复命中率（当日 UTC 自然日半开区间 [today, tomorrow)——对齐
        //    SubscriptionStat 半开区间先例规避 DateTime 等值往返问题；命中口径 = 有回复记录
        //    （ReplyContent 非空 或 ReplyMaterialId 非空）÷ 当日消息总量；除零守卫 → 0.0）
        var today = DateTime.UtcNow.Date;
        var tomorrow = today.AddDays(1);
        var todayTotal = await InteractionLog.CountAsync(
            l => l.ChannelId == channelId && l.MsgTime >= today && l.MsgTime < tomorrow, ct);
        var replied = await InteractionLog.CountAsync(
            l => l.ChannelId == channelId && l.MsgTime >= today && l.MsgTime < tomorrow
                && ((l.ReplyContent != null && l.ReplyContent != "") || l.ReplyMaterialId != null), ct);
        var autoReplyHitRate = todayTotal == 0 ? 0.0 : (double)replied / todayTotal;

        // ③ 转发成功率（当日 MessageForwardLog——Success ÷ (Success+Fail+Timeout)；
        //    Queued/SkippedDuplicate/NoRule 为未决/非转发决策态——分子分母双端排除；除零守卫 → 0.0）
        var success = await ForwardLog.CountAsync(
            l => l.ChannelId == channelId && l.CreateTime >= today && l.CreateTime < tomorrow
                && l.Status == WeChatMessageForwardLogStatus.Success, ct);
        var fail = await ForwardLog.CountAsync(
            l => l.ChannelId == channelId && l.CreateTime >= today && l.CreateTime < tomorrow
                && l.Status == WeChatMessageForwardLogStatus.Fail, ct);
        var timeout = await ForwardLog.CountAsync(
            l => l.ChannelId == channelId && l.CreateTime >= today && l.CreateTime < tomorrow
                && l.Status == WeChatMessageForwardLogStatus.Timeout, ct);
        var forwardTotal = success + fail + timeout;
        var forwardSuccessRate = forwardTotal == 0 ? 0.0 : (double)success / forwardTotal;

        _logger?.LogInformation("系统状态聚合查询完成：ChannelId={ChannelId} Connected={Connected} TodayMessageCount={TodayMessageCount} AutoReplyHitRate={AutoReplyHitRate:F3} ForwardSuccessRate={ForwardSuccessRate:F3}",
            channelId, connected, todayTotal, autoReplyHitRate, forwardSuccessRate);

        return new WeChatOperationsStatusDto(
            Connected: connected,
            ConnectedDetail: detail,
            TodayMessageCount: todayTotal,
            AutoReplyHitRate: autoReplyHitRate,
            ForwardSuccessRate: forwardSuccessRate);
    }
}

/// <summary>
/// 系统状态聚合 DTO（DMP-Lite 5.8 "系统状态"页四张状态卡数据源——只读聚合，<b>不含任何凭证明文/密文</b>，
/// Oracle P1 DTO 投影安全：连接判定只暴露布尔结果 <see cref="Connected"/>，凭证可解析性经
/// <see cref="WeChatChannelConfigDto.HasCredentials"/> 布尔投影消费，解密仅出站瞬时）。
/// </summary>
/// <param name="Connected">公众号连接状态（channel 配置存在且凭证可解析 → true）。</param>
/// <param name="ConnectedDetail">连接状态明细（"channel config found" / "channel config found but credentials missing"
/// / "no channel config"——消费方可直接展示或本地化映射）。</param>
/// <param name="TodayMessageCount">今日消息量（<see cref="InteractionLogEntity"/> 当日 UTC 自然日
/// [UtcNow.Date, +1 天) 条数——含命中与未命中全部消息）。</param>
/// <param name="AutoReplyHitRate">自动回复命中率（0..1——当日有回复记录的消息
/// （<see cref="InteractionLogEntity.ReplyContent"/> 非空 或 <see cref="InteractionLogEntity.ReplyMaterialId"/>
/// 非空）÷ 当日消息总量；当日无消息 → 0.0）。</param>
/// <param name="ForwardSuccessRate">转发成功率（0..1——当日 <see cref="MessageForwardLogEntity"/>
/// Success ÷ (Success+Fail+Timeout)；Queued/SkippedDuplicate/NoRule 未决/非转发决策态双端排除；
/// 当日无有效转发 → 0.0）。</param>
public sealed record WeChatOperationsStatusDto(
    bool Connected,
    string? ConnectedDetail,
    long TodayMessageCount,
    double AutoReplyHitRate,
    double ForwardSuccessRate);
