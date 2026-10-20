using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;
using TKWF.Federation.WeChat;
using TKWF.Federation.WeChat.MessageGateway;

using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="WeChatOperationsReplyHandler"/> 网关挂载测试（V0.2.0 增网关模块——批次 3）——生产路径
/// <see cref="WeChatOperationsTestHost"/> + <c>user.Use&lt;WeChatOperationsReplyHandler&gt;()</c> NoAop 直建
/// （handler 内部经 <c>User.Use&lt;IMessageGatewayService&gt;()</c> AOP 帧内调用 RouteAsync）。
/// <para>覆盖：<b>默认关零回归</b>（GatewayEnabled=false——RouteAsync 首行短路不参与，既有 handler 行为零变化）/
/// 网关开 + 幂等跳过（同事件两次——第二次不触发交互日志/订阅统计）/ <b>LOCATION 事件挂载</b>（P2-2——
/// OnLocationEventAsync 挂载后 LOCATION 事件可转发）。</para>
/// </summary>
public class MessageGatewayHandlerTests : WeChatOperationsTestHost
{
    private const string ChA = "ch-gw-h";
    private const string OpenId = "openid-gw-h";

    // ═══════════════════ 默认关零回归 ═══════════════════

    /// <summary>默认网关关（不设 ConfigureGatewayOptions——GatewayEnabled=false fail-closed）——既有 handler
    /// 行为零变化：关键词规则照常回复；RouteAsync 首行短路不参与（不查库不建幂等行不写转发日志）。</summary>
    [Fact]
    public async Task DefaultOff_Handler_ZeroRegression()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        await SeedReplyRuleAsync(user, ChA, WeChatReplySceneTypes.Keyword, triggerKey: "你好", matchMode: 1, replyText: "你好呀！");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "你好"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        // 既有回复行为零变化（关键词规则命中）
        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[你好呀！]]></Content>", reply!.Content);

        // RouteAsync 不参与——未建幂等记录/未写转发日志（未启用首行短路，零 DB 写）
        var idemCount = await provider.GetRequiredService<IFreeSql>().Select<WeChatMessageIdempotencyEntity>()
            .Where(i => i.ChannelId == ChA).CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, idemCount);
        var logCount = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA).CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(0, logCount);
    }

    // ═══════════════════ 网关开 + 幂等跳过 ═══════════════════

    /// <summary>网关开（GatewayEnabled=true）+ 同事件两次——第一次正常处理（欢迎语回复 + 订阅统计 +1 + 交互日志），
    /// 第二次幂等跳过（gate.Result=null 直接 return——不触发交互日志/订阅统计，防微信重试重复计数）。</summary>
    [Fact]
    public async Task GatewayOn_IdempotentSkip_NoDoubleStatsOrLogs()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        // 网关路由规则（event/subscribe LocalOnly——防重推重复处理）+ 既有回复规则（欢迎语）
        await SeedGatewayRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: "event", @event: "subscribe");
        await SeedReplyRuleAsync(user, ChA, WeChatReplySceneTypes.Subscribe, replyText: "欢迎关注！");

        var ctx = Ctx(("msg_type", "event"), ("event", "subscribe"), ("from_user_name", OpenId), ("channel_id", ChA));
        var first = await handler(user).HandleAsync(ctx, CancellationToken.None);
        Assert.NotNull(first);
        Assert.Contains("<Content><![CDATA[欢迎关注！]]></Content>", first!.Content);

        var second = await handler(user).HandleAsync(ctx, CancellationToken.None);
        Assert.Null(second);   // 幂等跳过——网关接管（gate.Result=null），handler 直接 return

        // 订阅统计仅 +1（重复回调不计数——最前短路 RecordStat 不执行）
        var stat = await StatAsync(provider, ChA);
        Assert.Equal(1, stat!.SubscribeCount);
        Assert.Equal(0, stat.UnsubscribeCount);
        // 交互日志仅 1 条（重复回调不写日志）
        var logCount = await provider.GetRequiredService<IFreeSql>().Select<InteractionLogEntity>()
            .Where(l => l.ChannelId == ChA).CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, logCount);
        // 幂等跳过错痕（SkippedDuplicate 日志——审计对账可见）
        var skipLog = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.SkippedDuplicate)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(skipLog);
    }

    // ═══════════════════ LOCATION 事件挂载（P2-2） ═══════════════════

    /// <summary>LOCATION 事件（上报地理位置）经 OnLocationEventAsync 挂载后可转发——AsyncCopy 规则命中 →
    /// 转发日志 Queued 落库；本地无回复逻辑 → handler 返回 null（网关已接管转发）。</summary>
    [Fact]
    public async Task LocationEvent_Mounted_Forwardable()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var epId = await SeedEndpointAsync(user, ChA, "loc-ep", "https://down.example.com");
        var ruleId = await SeedGatewayRuleAsync(user, ChA, WeChatMessageForwardModes.AsyncCopy, endpointId: epId, msgType: "event", @event: "LOCATION");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "LOCATION"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.Null(reply);   // LOCATION 事件本地无回复逻辑 → null（转发已由网关处理）

        // 转发日志 Queued（异步抄送已入队——LOCATION 事件可转发，P2-2 挂载验证）
        var log = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.Queued)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);
        Assert.Equal(ruleId, log!.MatchRuleId);
        Assert.Equal(epId, log.DownstreamEndpointId);
        Assert.Equal("event", log.MessageType);   // 转发日志 MessageType 为消息类型协议串（event 事件类消息）
    }

    // ═══════════════════ 辅助 ═══════════════════

    private WeChatOperationsReplyHandler handler(DomainUser<TestUserInfo> user)
        => user.Use<WeChatOperationsReplyHandler>();

    /// <summary>构造事件上下文参数包（SsoChannelEventContext——handler 入参形态，平台基类 Parse 富化）。</summary>
    private static SsoChannelEventContext Ctx(params (string Key, string? Value)[] parameters)
        => WeChatOperationsTestSupport.CreateEventContext(parameters);

    /// <summary>种子既有回复规则（ReplyRuleEntityDataService 生产路径直写）。</summary>
    private static async Task<long> SeedReplyRuleAsync(DomainUser<TestUserInfo> user, string channelId, int sceneType,
        string? triggerKey = null, int matchMode = 0, string? replyText = null)
    {
        var ds = user.Use<ReplyRuleEntityDataService>();
        var created = await ds.EntityCreateAsync(new ReplyRuleEntity
        {
            ChannelId = channelId,
            SceneType = sceneType,
            TriggerKey = triggerKey,
            MatchMode = matchMode,
            ReplyText = replyText,
            IsEnabled = true,
            SortOrder = 1,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, TestContext.Current.CancellationToken);
        return created.Id;
    }

    /// <summary>种子网关路由规则（MessageRouteRuleEntityDataService 生产路径直写）。</summary>
    private static async Task<long> SeedGatewayRuleAsync(DomainUser<TestUserInfo> user, string channelId, int forwardMode,
        long? endpointId = null, string? msgType = null, string? @event = null)
    {
        var ds = user.Use<MessageRouteRuleEntityDataService>();
        var created = await ds.EntityCreateAsync(new MessageRouteRuleEntity
        {
            ChannelId = channelId,
            MsgType = msgType,
            Event = @event,
            MatchMode = WeChatMessageMatchModes.None,
            ForwardMode = forwardMode,
            EndpointId = endpointId,
            IsEnabled = true,
            SortOrder = 1,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, TestContext.Current.CancellationToken);
        return created.Id;
    }

    /// <summary>种子下游端点（DownstreamEndpointEntityDataService 生产路径直写——无鉴权端点）。</summary>
    private static async Task<long> SeedEndpointAsync(DomainUser<TestUserInfo> user, string channelId, string name, string baseUrl)
    {
        var ds = user.Use<DownstreamEndpointEntityDataService>();
        var created = await ds.EntityCreateAsync(new DownstreamEndpointEntity
        {
            ChannelId = channelId,
            Name = name,
            BaseUrl = baseUrl,
            HttpMethod = "POST",
            TimeoutSeconds = 2,
            IsEnabled = true,
            AuthType = WeChatEndpointAuthTypes.None,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, TestContext.Current.CancellationToken);
        return created.Id;
    }

    /// <summary>查询 channel 订阅统计（DB 直查）。</summary>
    private static async Task<SubscriptionStatEntity?> StatAsync(ServiceProvider provider, string channelId)
    {
        var fsql = provider.GetRequiredService<IFreeSql>();
        return await fsql.Select<SubscriptionStatEntity>()
            .Where(s => s.ChannelId == channelId)
            .FirstAsync(TestContext.Current.CancellationToken);
    }
}
