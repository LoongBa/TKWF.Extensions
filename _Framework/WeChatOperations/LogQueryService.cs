using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations;

/// <summary>
/// 日志查询门面（运维排查刚需——交互消息日志 + 菜单发布审计；设计文档 §3.3.2）。
/// <para>消费方：<c>User.Use&lt;ILogQueryService&gt;()</c>（AddConstructibleService 守卫工厂，帧内解析）。</para>
/// </summary>
public interface ILogQueryService : IDomainService
{
    /// <summary>
    /// 交互日志筛选（ChannelId 必填；<paramref name="openId"/>/<paramref name="from"/>（含）/<paramref name="to"/>（含）/
    /// <paramref name="triggerType"/>（InteractionLog.TriggerType 值域：1=Subscribe/2=Keyword/3=Click/4=Image/5=Voice/
    /// 6=Video/7=ShortVideo/8=Location/9=Link/10=Scan/11=Default/12=None）可空 = 不过滤；MsgTime 降序）。
    /// </summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<IReadOnlyList<InteractionLogEntity>> ListInteractionsAsync(
        string channelId, string? openId = null, DateTime? from = null, DateTime? to = null,
        int? triggerType = null, CancellationToken ct = default);

    /// <summary>菜单发布审计（ChannelId + Action=Read/Create/Delete/Test；OperateAt 降序）。</summary>
    [RequirePermission("WeChatOperations.Query")]
    Task<IReadOnlyList<MenuPublishLogEntity>> ListPublishLogsAsync(string channelId, CancellationToken ct = default);
}

/// <inheritdoc />
[DiContractIgnore]
internal sealed class LogQueryService : DomainServiceBase, ILogQueryService
{
    private InteractionLogEntityDataService? _iDs;
    private InteractionLogEntityDataService IDs => _iDs ??= User.Use<InteractionLogEntityDataService>();
    private MenuPublishLogEntityDataService? _pDs;
    private MenuPublishLogEntityDataService PDs => _pDs ??= User.Use<MenuPublishLogEntityDataService>();

    public LogQueryService(IDomainUser user) : base(user)
    {
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<InteractionLogEntity>> ListInteractionsAsync(
        string channelId, string? openId = null, DateTime? from = null, DateTime? to = null,
        int? triggerType = null, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return Array.Empty<InteractionLogEntity>();

        return await IDs.EntitySelectAsync(
            l => l.ChannelId == channelId
                 && (openId == null || l.OpenId == openId)
                 && (from == null || l.MsgTime >= from.Value)
                 && (to == null || l.MsgTime <= to.Value)
                 && (triggerType == null || l.TriggerType == triggerType.Value),
            0, 200,
            q => q.OrderByDescending(l => l.MsgTime),
            ct);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<MenuPublishLogEntity>> ListPublishLogsAsync(string channelId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(channelId))
            return Array.Empty<MenuPublishLogEntity>();

        return await PDs.EntitySelectAsync(
            l => l.ChannelId == channelId,
            0, 200,
            q => q.OrderByDescending(l => l.OperateAt),
            ct);
    }
}