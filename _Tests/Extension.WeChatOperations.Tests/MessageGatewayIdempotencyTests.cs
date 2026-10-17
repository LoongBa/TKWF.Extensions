using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;
using TKWF.Federation.WeChat;
using TKWF.Federation.WeChat.MessageGateway;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="IMessageGatewayService.RouteAsync"/> 幂等测试（V0.2.0 增网关模块——批次 3）——生产路径
/// <see cref="WeChatOperationsTestHost"/> + <c>ConfigureGatewayOptions</c> 开网关（fail-closed 默认关）+
/// <c>user.Use&lt;IMessageGatewayService&gt;()</c> AOP。
/// <para>覆盖：幂等键构建（消息形态 msg: / 事件形态 evt:）/ 同键重复回调跳过（转发日志 SkippedDuplicate 留痕）/
/// 并发败者重查（唯一索引兜底）/ Pending 超时复用键（P1-5）/ 首次处理状态迁移 Forwarded（P1-2 语义=已处理待投递）。</para>
/// <para>运行库键构建纯逻辑（<see cref="WeChatMessageIdempotencyKeyBuilder"/>）直测 + 门面集成双覆盖；
/// matcher/decider 纯逻辑单测见 Extension.Federation.WeChat.MessageGateway.Tests（52 用例——本单元不重复）。</para>
/// </summary>
public class MessageGatewayIdempotencyTests : WeChatOperationsTestHost
{
    private const string ChA = "ch-gw-idem";
    private const string OpenId = "openid-gw-idem";

    // ═══════════════════ 幂等键构建（运行库直测） ═══════════════════

    /// <summary>消息形态（MsgId 非空）→ <c>msg:{channelId}:{msgId}</c>——跨 channel 隔离入键。</summary>
    [Fact]
    public void KeyBuilder_MessageForm_UsesMsgId()
    {
        var key = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-main", "1234567890", "openid-x", 1730000000);
        Assert.Equal("msg:ch-main:1234567890", key);
    }

    /// <summary>事件形态（无 MsgId）→ <c>evt:{channelId}:{fromUserName}:{createTime}</c>——FromUserName+CreateTime 键来源。</summary>
    [Fact]
    public void KeyBuilder_EventForm_UsesFromAndCreateTime()
    {
        var key = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-main", null, "openid-x", 1730000000);
        Assert.Equal("evt:ch-main:openid-x:1730000000", key);
    }

    /// <summary>同 MsgId 跨 channel → 键不同（ChannelId 入键——杜绝跨通道误判重复）。</summary>
    [Fact]
    public void KeyBuilder_SameMsgId_DifferentChannel_DistinctKeys()
    {
        var a = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-a", "100", "openid", 1);
        var b = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-b", "100", "openid", 1);
        Assert.NotEqual(a, b);
    }

    // ═══════════════════ 重复回调跳过（消息形态） ═══════════════════

    /// <summary>同键两次 RouteAsync——第一次正常处理（Handled=false + MatchedRule 命中）；第二次幂等跳过
    /// （Handled=true + Result=null + SkippedDuplicate=true）——转发日志 SkippedDuplicate 留痕 + 幂等表仅一行 Forwarded。</summary>
    [Fact]
    public async Task RouteAsync_SameKeyTwice_SecondSkipped_NoDuplicateForward()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var ruleId = await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();
        var ctx = WxCtx("text", content: "重复回调", msgId: "msg-100");

        var first = await svc.RouteAsync(ctx, TestContext.Current.CancellationToken);
        Assert.False(first.Handled);
        Assert.NotNull(first.MatchedRule);
        Assert.Equal(ruleId, first.MatchedRule!.Id);
        Assert.False(first.SkippedDuplicate);

        var second = await svc.RouteAsync(ctx, TestContext.Current.CancellationToken);
        Assert.True(second.Handled);          // 网关接管本次回复
        Assert.Null(second.Result);           // 静默跳过不回复
        Assert.True(second.SkippedDuplicate); // 重复留痕

        // 转发日志 SkippedDuplicate 留痕（重试风暴对账可见）
        var log = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.SkippedDuplicate)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);
        Assert.Equal("msg-100", log!.MsgId);
        // 幂等表仅一行（键存在即判决）——首次处理已迁 Forwarded
        var idem = await provider.GetRequiredService<IFreeSql>().Select<WeChatMessageIdempotencyEntity>()
            .Where(i => i.ChannelId == ChA).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(idem);
        Assert.Equal(WeChatMessageIdempotencyStatus.Forwarded, idem[0].LastStatus);
    }

    /// <summary>事件形态（无 MsgId——evt: 键）首次处理 → 幂等表 LastStatus=Forwarded（P1-2：语义=已处理待投递）。</summary>
    [Fact]
    public async Task RouteAsync_EventForm_FirstProcess_LastStatusForwarded()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: "event", @event: "subscribe", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();
        var ctx = WxCtx("event", @event: "subscribe", msgId: null, createTime: 1730000000);

        var result = await svc.RouteAsync(ctx, TestContext.Current.CancellationToken);
        Assert.False(result.Handled);
        Assert.NotNull(result.MatchedRule);

        // 事件形态幂等键 = evt:{ch}:{from}:{createTime}
        var key = $"evt:{ChA}:{OpenId}:1730000000";
        var idem = await provider.GetRequiredService<IFreeSql>().Select<WeChatMessageIdempotencyEntity>()
            .Where(i => i.IdempotencyKey == key).FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(idem);
        Assert.Equal(WeChatMessageIdempotencyStatus.Forwarded, idem!.LastStatus);
        Assert.StartsWith("evt:", idem.IdempotencyKey);   // 事件形态键前缀
    }

    // ═══════════════════ 并发败者重查 ═══════════════════

    /// <summary>同键并发双 RouteAsync——唯一索引兜底：仅一行幂等记录；败者重查转跳过（SkippedDuplicate）不重复转发
    /// （对齐 SubscriptionStatService 查→建→唯一索引兜底→败者重查模式）。</summary>
    [Fact]
    public async Task RouteAsync_Concurrent_UniqueIndex_LoserSkipped()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();
        var ctx = WxCtx("text", content: "并发", msgId: "msg-concurrent");

        var results = await Task.WhenAll(
            svc.RouteAsync(ctx, TestContext.Current.CancellationToken),
            svc.RouteAsync(ctx, TestContext.Current.CancellationToken));

        // 唯一索引兜底核心不变量：仅一行幂等记录（无重复转发/重复建键）
        var idemCount = await provider.GetRequiredService<IFreeSql>().Select<WeChatMessageIdempotencyEntity>()
            .Where(i => i.ChannelId == ChA).CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, idemCount);

        // 败者重查转跳过（SkippedDuplicate=true 留痕）；胜者正常处理（Handled=false 本地继续）
        Assert.Contains(results, r => r.SkippedDuplicate);
        Assert.Contains(results, r => !r.SkippedDuplicate && !r.Handled && r.Action == ForwardAction.LocalOnly);

        // 重试风暴对账——至少一条 SkippedDuplicate 日志
        var skippedLogs = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.SkippedDuplicate)
            .ToListAsync(TestContext.Current.CancellationToken);
        Assert.NotEmpty(skippedLogs);
    }

    // ═══════════════════ Pending 超时复用键（P1-5） ═══════════════════

    /// <summary>Pending 记录 FirstSeenAt 超过 PendingTimeoutMinutes（默认 5）→ 复用键重建 Pending + 继续处理
    /// （不跳过不丢弃——陈旧 Pending 数据丢失窗口兜底）。预置陈旧 Pending 直改库中 FirstSeenAt。</summary>
    [Fact]
    public async Task RouteAsync_PendingExpired_ReusesKeyAndContinues()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: "text", matchMode: WeChatMessageMatchModes.None);

        // 预置 10 分钟前首见的 Pending 记录（超时窗口 5 分钟）
        var key = $"msg:{ChA}:msg-p1";
        var idemDs = user.Use<WeChatMessageIdempotencyEntityDataService>();
        await idemDs.EntityCreateAsync(new WeChatMessageIdempotencyEntity
        {
            IdempotencyKey = key,
            ChannelId = ChA,
            MsgId = "msg-p1",
            FromUserName = OpenId,
            CreateTime = ToUtc(1730000000),
            FirstSeenAt = DateTime.UtcNow.AddMinutes(-10),
            LastStatus = WeChatMessageIdempotencyStatus.Pending,
            UpdateTime = DateTime.UtcNow,
        }, TestContext.Current.CancellationToken);

        var svc = user.Use<IMessageGatewayService>();
        var result = await svc.RouteAsync(WxCtx("text", content: "超时复用", msgId: "msg-p1"), TestContext.Current.CancellationToken);

        // 超时 Pending 复用——继续处理（命中 LocalOnly → 不跳过）
        Assert.False(result.Handled);
        Assert.False(result.SkippedDuplicate);
        Assert.NotNull(result.MatchedRule);

        // 幂等行已迁 Forwarded + FirstSeenAt 刷新为当前（复用键重建语义）
        var row = await provider.GetRequiredService<IFreeSql>().Select<WeChatMessageIdempotencyEntity>()
            .Where(i => i.IdempotencyKey == key).FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(row);
        Assert.Equal(WeChatMessageIdempotencyStatus.Forwarded, row!.LastStatus);
        Assert.True(DateTime.UtcNow - row.FirstSeenAt < TimeSpan.FromMinutes(1), "FirstSeenAt 应被刷新为当前时间（复用键重建）");
    }

    // ═══════════════════ 辅助 ═══════════════════

    /// <summary>构造微信消息/事件上下文（WechatEventContext 直构——RouteAsync 入参形态；MsgId/CreateTime 幂等键数据源）。</summary>
    private static WechatEventContext WxCtx(string? msgType, string? @event = null, string? content = null,
        string? msgId = null, long? createTime = null, string? rawXml = null)
        => new(
            MsgType: msgType, Event: @event, FromUserName: OpenId, ToUserName: null, EventKey: null,
            Latitude: null, Longitude: null, Precision: null, Scale: null, Label: null,
            Content: content, MediaId: null, PicUrl: null, Format: null, ThumbMediaId: null,
            Title: null, Description: null, Url: null, Status: null, TotalCount: null, FilterCount: null,
            SentCount: null, ErrorCount: null, OpenId: OpenId, UId: null, ChannelId: ChA,
            MsgId: msgId, CreateTime: createTime, RawXml: rawXml);

    /// <summary>种子网关路由规则（MessageRouteRuleEntityDataService 生产路径直写）。</summary>
    private static async Task<long> SeedRuleAsync(DomainUser<TestUserInfo> user, string channelId, int forwardMode,
        long? endpointId = null, string? msgType = null, string? @event = null,
        int matchMode = WeChatMessageMatchModes.None, string? triggerKey = null, int sortOrder = 1)
    {
        var ds = user.Use<MessageRouteRuleEntityDataService>();
        var created = await ds.EntityCreateAsync(new MessageRouteRuleEntity
        {
            ChannelId = channelId,
            MsgType = msgType,
            Event = @event,
            MatchMode = matchMode,
            TriggerKey = triggerKey,
            ForwardMode = forwardMode,
            EndpointId = endpointId,
            IsEnabled = true,
            SortOrder = sortOrder,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, TestContext.Current.CancellationToken);
        return created.Id;
    }

    /// <summary>微信 Unix 秒时间戳 → DateTime（UTC——对齐网关 ToDateTime 语义）。</summary>
    private static DateTime ToUtc(long unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds).UtcDateTime;
}
