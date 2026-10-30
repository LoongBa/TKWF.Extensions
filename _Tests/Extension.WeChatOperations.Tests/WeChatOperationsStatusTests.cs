using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="IWeChatOperationsStatusService"/>（WeChatOperationsStatusService 门面）测试——DMP-Lite 5.8
/// "系统状态"页四张状态卡聚合：公众号连接状态 / 今日消息量 / 自动回复命中率 / 转发成功率。
/// <para>生产路径：<see cref="WeChatOperationsTestHost"/> + <c>BindScope()</c> + <c>user.Use&lt;
/// IWeChatOperationsStatusService&gt;()</c> AOP（AddConstructibleService 守卫工厂）；日志行直插经
/// <c>user.Use&lt;具体 DataService&gt;()</c> NoAop 直建（EntityCreateAsync——生产路径等价，不手写 StubDomainUser）。</para>
/// <para>覆盖：无 channel → 未连接 + 全零（空数据集不抛）/ 凭证齐全 → 已连接 / 缺凭证 → 未连接 /
/// 今日消息混合命中回复 → 消息量 + 命中率（昨日行不计数）/ 转发日志混合状态 → 成功率（忽略态双端排除）。</para>
/// </summary>
public class WeChatOperationsStatusTests : WeChatOperationsTestHost
{
    private const string Ch = "ch-status";

    /// <summary>无 channel 配置 → Connected=false + detail="no channel config" + 四卡全零（空数据集零回归不抛）。</summary>
    [Fact]
    public async Task GetStatusSummaryAsync_NoChannel_NotConnectedAndZero()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IWeChatOperationsStatusService>();

        var status = await service.GetStatusSummaryAsync(Ch, TestContext.Current.CancellationToken);

        Assert.False(status.Connected);
        Assert.Equal("no channel config", status.ConnectedDetail);
        Assert.Equal(0, status.TodayMessageCount);
        Assert.Equal(0.0, status.AutoReplyHitRate);
        Assert.Equal(0.0, status.ForwardSuccessRate);
    }

    /// <summary>channel 配置存在且凭证已配置（SaveChannelAsync 明文入 → AES-GCM 密文落库，HasCredentials=true）
    /// → Connected=true + detail="channel config found"。</summary>
    [Fact]
    public async Task GetStatusSummaryAsync_ChannelWithCredentials_Connected()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var channelService = user.Use<IChannelConfigService>();
        await channelService.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: Ch, AppId: "wx-status-1", DisplayName: "状态测试号", IsEnabled: true,
            ChannelType: "wechat_event", AppSecret: "secret-status"), ct: TestContext.Current.CancellationToken);

        var status = await user.Use<IWeChatOperationsStatusService>()
            .GetStatusSummaryAsync(Ch, TestContext.Current.CancellationToken);

        Assert.True(status.Connected);
        Assert.Equal("channel config found", status.ConnectedDetail);
    }

    /// <summary>channel 配置存在但凭证未配置（无明文传入 → 密文为空，HasCredentials=false）→ Connected=false
    /// + detail 指明凭证缺失（连接判定含凭证可解析性）。</summary>
    [Fact]
    public async Task GetStatusSummaryAsync_ChannelWithoutCredentials_NotConnected()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var channelService = user.Use<IChannelConfigService>();
        await channelService.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: Ch, AppId: "wx-status-2", DisplayName: "无凭证号", IsEnabled: true,
            ChannelType: "wechat_event"), ct: TestContext.Current.CancellationToken);

        var status = await user.Use<IWeChatOperationsStatusService>()
            .GetStatusSummaryAsync(Ch, TestContext.Current.CancellationToken);

        Assert.False(status.Connected);
        Assert.Equal("channel config found but credentials missing", status.ConnectedDetail);
    }

    /// <summary>今日消息混合命中回复（3 条今日：2 条有回复记录——ReplyContent 非空 + ReplyMaterialId 非空各 1，
    /// 1 条未回复；+ 昨日 1 条今日窗口外）→ TodayMessageCount=3 + AutoReplyHitRate=2/3（昨日不计数）。</summary>
    [Fact]
    public async Task GetStatusSummaryAsync_TodayMessages_MixedReplies_HitRate()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var interactionLog = user.Use<InteractionLogEntityDataService>();
        var now = DateTime.UtcNow;

        // 今日 3 条：ReplyContent 非空 / ReplyMaterialId 非空（命中 2 条）+ 未回复（未命中 1 条）
        await interactionLog.EntityCreateAsync(new InteractionLogEntity
        {
            ChannelId = Ch, OpenId = "o1", MsgTime = now, ReplyContent = "欢迎语",
        }, TestContext.Current.CancellationToken);
        await interactionLog.EntityCreateAsync(new InteractionLogEntity
        {
            ChannelId = Ch, OpenId = "o2", MsgTime = now.AddMinutes(-5), ReplyMaterialId = 1,
        }, TestContext.Current.CancellationToken);
        await interactionLog.EntityCreateAsync(new InteractionLogEntity
        {
            ChannelId = Ch, OpenId = "o3", MsgTime = now.AddMinutes(-10),
        }, TestContext.Current.CancellationToken);
        // 昨日 1 条（今日半开区间窗口外——不计数）
        await interactionLog.EntityCreateAsync(new InteractionLogEntity
        {
            ChannelId = Ch, OpenId = "o4", MsgTime = DateTime.UtcNow.Date.AddDays(-1), ReplyContent = "昨日回复",
        }, TestContext.Current.CancellationToken);

        var status = await user.Use<IWeChatOperationsStatusService>()
            .GetStatusSummaryAsync(Ch, TestContext.Current.CancellationToken);

        Assert.Equal(3, status.TodayMessageCount);
        Assert.Equal(2.0 / 3.0, status.AutoReplyHitRate, 10);
    }

    /// <summary>今日转发日志混合状态（Success×2 / Fail×1 / Timeout×1 + 忽略态 Queued/SkippedDuplicate/NoRule×3
    /// + 昨日 Success×1 今日窗口外）→ ForwardSuccessRate=2/4=0.5——忽略态分子分母双端排除、昨日不计数。</summary>
    [Fact]
    public async Task GetStatusSummaryAsync_ForwardLogs_MixedStatuses_SuccessRate()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var forwardLog = user.Use<MessageForwardLogEntityDataService>();
        var today = DateTime.UtcNow.Date;
        var ct = TestContext.Current.CancellationToken;

        // 有效转发（分母 = Success+Fail+Timeout = 4）：Success×2 / Fail×1 / Timeout×1
        await forwardLog.EntityCreateAsync(new MessageForwardLogEntity { ChannelId = Ch, MsgId = "m1", Status = WeChatMessageForwardLogStatus.Success, CreateTime = today.AddHours(1) }, ct);
        await forwardLog.EntityCreateAsync(new MessageForwardLogEntity { ChannelId = Ch, MsgId = "m2", Status = WeChatMessageForwardLogStatus.Success, CreateTime = today.AddHours(2) }, ct);
        await forwardLog.EntityCreateAsync(new MessageForwardLogEntity { ChannelId = Ch, MsgId = "m3", Status = WeChatMessageForwardLogStatus.Fail, CreateTime = today.AddHours(3) }, ct);
        await forwardLog.EntityCreateAsync(new MessageForwardLogEntity { ChannelId = Ch, MsgId = "m4", Status = WeChatMessageForwardLogStatus.Timeout, CreateTime = today.AddHours(4) }, ct);
        // 忽略态（未决/非转发决策——分子分母双端排除）：Queued / SkippedDuplicate / NoRule
        await forwardLog.EntityCreateAsync(new MessageForwardLogEntity { ChannelId = Ch, MsgId = "m5", Status = WeChatMessageForwardLogStatus.Queued, CreateTime = today.AddHours(5) }, ct);
        await forwardLog.EntityCreateAsync(new MessageForwardLogEntity { ChannelId = Ch, MsgId = "m6", Status = WeChatMessageForwardLogStatus.SkippedDuplicate, CreateTime = today.AddHours(6) }, ct);
        await forwardLog.EntityCreateAsync(new MessageForwardLogEntity { ChannelId = Ch, MsgId = "m7", Status = WeChatMessageForwardLogStatus.NoRule, CreateTime = today.AddHours(7) }, ct);
        // 昨日 Success×1（今日窗口外——不计数）
        await forwardLog.EntityCreateAsync(new MessageForwardLogEntity { ChannelId = Ch, MsgId = "m8", Status = WeChatMessageForwardLogStatus.Success, CreateTime = today.AddDays(-1).AddHours(8) }, ct);

        var status = await user.Use<IWeChatOperationsStatusService>()
            .GetStatusSummaryAsync(Ch, TestContext.Current.CancellationToken);

        Assert.Equal(2.0 / 4.0, status.ForwardSuccessRate, 10);   // Success 2 ÷ (2+1+1)
    }
}
