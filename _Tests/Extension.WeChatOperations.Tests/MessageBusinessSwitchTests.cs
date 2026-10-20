using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;
using TKWF.Federation.WeChat;
using TKWF.Federation.WeChat.MessageGateway;

using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// 消息业务总开关测试（DMP-Lite 5.8——<see cref="WeChatOperationsOptions.MessageBusinessEnabled"/>）：
/// 默认 true 既有行为零回归（关键词回复照常）/ 关闭（false）本地回复停发——text 返回维护提示
/// （<see cref="WeChatOperationsOptions.DefaultReplyText"/> 兜底文案）、subscribe 不再出欢迎语 /
/// <b>转发网关不受控</b>——总开关关闭但网关 <see cref="IMessageGatewayService.RouteAsync"/> 先行仍转发生效
/// （AsyncCopy 命中 → 转发日志 Queued 落库——网关先行于总开关判定，handler 首行挂载序）。
/// <para>生产路径：<c>user.Use&lt;WeChatOperationsReplyHandler&gt;()</c> NoAop 直建（守卫工厂 ctor 帧内供给
/// IDomainUser + IOptions 从 DI 解析——<see cref="WeChatOperationsTestHost.ConfigureOperationsOptions"/> 编程覆盖）；
/// 总开关每测试独立宿主（<c>ConfigureOperationsOptions</c> 逐宿主注入——无静态污染）。</para>
/// </summary>
public class MessageBusinessSwitchTests : WeChatOperationsTestHost
{
    private const string ChA = "ch-switch";
    private const string OpenId = "openid-switch";
    private const string MaintenanceText = "系统维护中，请稍后再试。";

    // ═══════════════════ 默认 true（既有行为零回归） ═══════════════════

    /// <summary>默认开关（不配置）MessageBusinessEnabled=true → 关键词回复照常（既有行为零回归）。</summary>
    [Fact]
    public async Task Default_True_KeywordReply_ZeroRegression()
    {
        using var _ = this;
        var (_, user) = BindScope();
        await SeedReplyRuleAsync(user, ChA, WeChatReplySceneTypes.Keyword, triggerKey: "你好", matchMode: 1, replyText: "你好呀！");

        var reply = await HandlerFor(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "你好"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[你好呀！]]></Content>", reply!.Content);
    }

    // ═══════════════════ 关闭（本地回复停发） ═══════════════════

    /// <summary>开关关闭 → text 消息本地回复停发——返回维护提示（DefaultReplyText 兜底文案），关键词规则不再回复。</summary>
    [Fact]
    public async Task Disabled_TextMessage_MaintenanceReply_KeywordNotReplied()
    {
        ConfigureOperationsOptions = o => { o.MessageBusinessEnabled = false; o.DefaultReplyText = MaintenanceText; };
        using var _ = this;
        var (_, user) = BindScope();
        await SeedReplyRuleAsync(user, ChA, WeChatReplySceneTypes.Keyword, triggerKey: "你好", matchMode: 1, replyText: "你好呀！");

        var reply = await HandlerFor(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "你好"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        // 本地回复停发 → 统一维护提示；关键词规则文案不再出现
        Assert.NotNull(reply);
        Assert.Contains($"<Content><![CDATA[{MaintenanceText}]]></Content>", reply!.Content);
        Assert.DoesNotContain("你好呀！", reply.Content);
    }

    /// <summary>开关关闭 → subscribe 事件不再出欢迎语（欢迎语规则文案不回复——统一维护提示）。</summary>
    [Fact]
    public async Task Disabled_SubscribeEvent_NoWelcomeReply()
    {
        ConfigureOperationsOptions = o => { o.MessageBusinessEnabled = false; o.DefaultReplyText = MaintenanceText; };
        using var _ = this;
        var (_, user) = BindScope();
        await SeedReplyRuleAsync(user, ChA, WeChatReplySceneTypes.Subscribe, replyText: "欢迎关注本公众号！");

        var reply = await HandlerFor(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "subscribe"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains($"<Content><![CDATA[{MaintenanceText}]]></Content>", reply!.Content);
        Assert.DoesNotContain("欢迎关注本公众号！", reply.Content);   // 欢迎语不再出
    }

    // ═══════════════════ 转发网关不受控 ═══════════════════

    /// <summary>总开关关闭但网关先行：handler 首行 <see cref="IMessageGatewayService.RouteAsync"/> 仍执行——
    /// AsyncCopy 规则命中 → 转发日志 Queued 落库（网关先行于总开关判定，不受控）；随后总开关判定 →
    /// 本地回复停发返回维护提示（双断言：转发仍生效 + 本地停发）。</summary>
    [Fact]
    public async Task Disabled_GatewayStillForwards_AsyncCopyQueuedPersisted()
    {
        ConfigureOperationsOptions = o => { o.MessageBusinessEnabled = false; o.DefaultReplyText = MaintenanceText; };
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var epId = await SeedEndpointAsync(user, ChA, "copy-ep", "https://down.example.com");
        await SeedRouteRuleAsync(user, ChA, WeChatMessageForwardModes.AsyncCopy, endpointId: epId, msgType: "text", matchMode: WeChatMessageMatchModes.None);

        var reply = await HandlerFor(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "异步抄送"),
            ("from_user_name", OpenId), ("channel_id", ChA),
            ("__decrypted_xml", WeChatOperationsTestSupport.WxXml(("MsgType", "text"), ("Content", "异步抄送"),
                ("MsgId", "m-switch"), ("CreateTime", "1730000000")))), CancellationToken.None);

        // ① 网关先行不受控——AsyncCopy 命中 → 转发日志 Queued 落库（EnqueueAsync 首步）
        var queued = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.Queued)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(queued);
        Assert.Equal(epId, queued!.DownstreamEndpointId);
        Assert.Equal(WeChatMessageForwardModes.AsyncCopy, queued.ForwardMode);
        Assert.Equal("m-switch", queued.MsgId);

        // ② 总开关关闭 → 本地回复停发（维护提示兜底）
        Assert.NotNull(reply);
        Assert.Contains($"<Content><![CDATA[{MaintenanceText}]]></Content>", reply!.Content);
    }

    // ═══════════════════ 辅助 ═══════════════════

    private static WeChatOperationsReplyHandler HandlerFor(DomainUser<TestUserInfo> user)
        => user.Use<WeChatOperationsReplyHandler>();

    /// <summary>构造事件上下文参数包（对齐 WeChatOperationsReplyHandlerTests）。</summary>
    private static TKWF.Ext.Federation.SsoChannelEventContext Ctx(params (string Key, string? Value)[] parameters)
        => WeChatOperationsTestSupport.CreateEventContext(parameters);

    /// <summary>种子回复规则（ReplyRuleEntityDataService 生产路径直写——欢迎语/关键词规则数据源）。</summary>
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

    /// <summary>种子网关路由规则（MessageRouteRuleEntityDataService 生产路径直写——对齐 MessageGatewayRoutingTests）。</summary>
    private static async Task<long> SeedRouteRuleAsync(DomainUser<TestUserInfo> user, string channelId, int forwardMode,
        long? endpointId = null, string? msgType = null, int matchMode = WeChatMessageMatchModes.None)
    {
        var ds = user.Use<MessageRouteRuleEntityDataService>();
        var created = await ds.EntityCreateAsync(new MessageRouteRuleEntity
        {
            ChannelId = channelId,
            MsgType = msgType,
            MatchMode = matchMode,
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
}
