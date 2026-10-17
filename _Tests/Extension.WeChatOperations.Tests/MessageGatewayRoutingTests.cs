using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;
using TKWF.Federation.WeChat;
using TKWF.Federation.WeChat.MessageGateway;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="IMessageGatewayService.RouteAsync"/> 转发决策测试（V0.2.0 增网关模块——批次 3；V0.3.0 SyncProxy 真透传）——
/// 生产路径 <see cref="WeChatOperationsTestHost"/> + <c>ConfigureGatewayOptions</c> 开网关 + <c>User.Use&lt;IMessageGatewayService&gt;()</c>。
/// <para>覆盖：LocalOnly（Handled=false 本地渲染继续 + 零转发日志 + 幂等 Forwarded）/ AsyncCopy（Handled=false +
/// 转发日志 Queued 落库 + 幂等 Forwarded——BackgroundJob 基础设施未入宿主，断言 Queued 落库为生产路径等价）/
/// NoRule（NoRule 日志 + 幂等 Skipped + Handled=false）/ MatchMode 三态（None/Exact/Contains）/ MsgType 通配与相等 /
/// Event 仅 MsgType=event 参与 / <b>SyncProxy/PreprocessSync 真透传（V0.3.0——出站透传 → RawXml 原样接管回复；
/// 超时/失败/非 XML 降级本地渲染链；熔断三态；幂等重复跳过）</b>。</para>
/// <para>运行库 matcher/decider/熔断纯逻辑单测见 Extension.Federation.WeChat.MessageGateway.Tests（52 用例）；
/// 本单元为网关侧集成（经门面 RouteAsync 验证真实规则投影→匹配→决策→出站→落库全链）。</para>
/// </summary>
public class MessageGatewayRoutingTests : WeChatOperationsTestHost
{
    private const string ChA = "ch-gw-route";
    private const string OpenId = "openid-gw-route";
    private const string DownstreamXml =
        "<xml><ToUserName><![CDATA[openid-gw-route]]></ToUserName><MsgType><![CDATA[text]]></MsgType><Content><![CDATA[下游同步回复]]></Content></xml>";

    // ═══════════════════ LocalOnly ═══════════════════

    /// <summary>命中规则 ForwardMode=LocalOnly → Handled=false（handler 继续本地渲染——零转发）+ 幂等表 Forwarded +
    /// 无转发日志（本地处理不转发）。</summary>
    [Fact]
    public async Task LocalOnly_Matched_HandledFalse_NoForwardLog()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var ruleId = await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();

        var result = await svc.RouteAsync(WxCtx("text", content: "本地回复", msgId: "m-local"), TestContext.Current.CancellationToken);

        Assert.False(result.Handled);
        Assert.Equal(ForwardAction.LocalOnly, result.Action);
        Assert.NotNull(result.MatchedRule);
        Assert.Equal(ruleId, result.MatchedRule!.Id);
        Assert.False(result.SkippedDuplicate);
        // 本地处理不转发——无转发日志
        var logs = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Empty(logs);
        // 幂等 Forwarded（已处理语义——防重复回调重复渲染）
        var idem = await provider.GetRequiredService<IFreeSql>().Select<WeChatMessageIdempotencyEntity>()
            .Where(i => i.ChannelId == ChA).FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WeChatMessageIdempotencyStatus.Forwarded, idem!.LastStatus);
    }

    // ═══════════════════ AsyncCopy ═══════════════════

    /// <summary>命中规则 ForwardMode=AsyncCopy → Handled=false（本地为主响应）+ 转发日志 Status=Queued 落库
    /// + 幂等表 Forwarded（语义=已入队待投递）。⚠️ 测试宿主未注册 BackgroundJob 基础设施（IBackgroundJobManager
    /// 不可解析——EnqueueAsync 落 Queued 后入队抛守卫异常被网关 fail-safe 捕获）——断言 Queued 落库为生产路径等价。</summary>
    [Fact]
    public async Task AsyncCopy_Matched_HandledFalse_QueuedLogPersisted()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var epId = await SeedEndpointAsync(user, ChA, "copy-ep", "https://down.example.com");
        var ruleId = await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.AsyncCopy, endpointId: epId, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();

        var result = await svc.RouteAsync(WxCtx("text", content: "异步抄送", msgId: "m-copy", rawXml: "<xml><MsgType><![CDATA[text]]></MsgType></xml>"),
            TestContext.Current.CancellationToken);

        Assert.False(result.Handled);
        Assert.Equal(ForwardAction.AsyncCopy, result.Action);
        Assert.NotNull(result.MatchedRule);

        // 转发日志 Queued 落库（EnqueueAsync 首步——入队前先落 Queued 行；投递状态归 MessageForwardLogEntity）
        var queued = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.Queued)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(queued);
        Assert.Equal(ruleId, queued!.MatchRuleId);
        Assert.Equal(epId, queued.DownstreamEndpointId);
        Assert.Equal(WeChatMessageForwardModes.AsyncCopy, queued.ForwardMode);
        Assert.Equal("m-copy", queued.MsgId);
        // 幂等 Forwarded（P1-2：已入队待投递语义）
        var idem = await provider.GetRequiredService<IFreeSql>().Select<WeChatMessageIdempotencyEntity>()
            .Where(i => i.ChannelId == ChA).FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WeChatMessageIdempotencyStatus.Forwarded, idem!.LastStatus);
    }

    // ═══════════════════ NoRule ═══════════════════

    /// <summary>无命中规则 → 转发日志 Status=NoRule + 幂等表 Skipped + Handled=false（handler 走既有默认回复）。</summary>
    [Fact]
    public async Task NoRule_NoMatch_NoRuleLog_SkippedIdempotency()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();

        var result = await svc.RouteAsync(WxCtx("text", content: "无规则", msgId: "m-norule"), TestContext.Current.CancellationToken);

        Assert.False(result.Handled);          // handler 继续本地渲染（既有默认兜底）
        Assert.Null(result.MatchedRule);
        var log = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.NoRule)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);
        Assert.Null(log!.MatchRuleId);         // NoRule 无命中规则
        var idem = await provider.GetRequiredService<IFreeSql>().Select<WeChatMessageIdempotencyEntity>()
            .Where(i => i.ChannelId == ChA).FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WeChatMessageIdempotencyStatus.Skipped, idem!.LastStatus);
    }

    // ═══════════════════ MatchMode（经 RouteAsync 集成——真实规则投影→matcher） ═══════════════════

    /// <summary>MatchMode=Exact：TriggerKey == content 命中；内容不等 → 不命中（NoRule 降级）。</summary>
    [Fact]
    public async Task MatchMode_Exact_EqualHits_DifferentNoRule()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: "text",
            matchMode: WeChatMessageMatchModes.Exact, triggerKey: "优惠券");
        var svc = user.Use<IMessageGatewayService>();

        var hit = await svc.RouteAsync(WxCtx("text", content: "优惠券", msgId: "m-exact-1"), TestContext.Current.CancellationToken);
        Assert.False(hit.Handled);
        Assert.NotNull(hit.MatchedRule);
        Assert.Equal(WeChatMessageMatchModes.Exact, hit.MatchedRule!.MatchMode);
        Assert.Equal("优惠券", hit.MatchedRule.TriggerKey);

        var miss = await svc.RouteAsync(WxCtx("text", content: "优惠券领取", msgId: "m-exact-2"), TestContext.Current.CancellationToken);
        Assert.False(miss.Handled);
        Assert.Null(miss.MatchedRule);         // Exact 不等 → NoRule
    }

    /// <summary>MatchMode=Contains：content 含 TriggerKey 命中。</summary>
    [Fact]
    public async Task MatchMode_Contains_SubstringMatches()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: "text",
            matchMode: WeChatMessageMatchModes.Contains, triggerKey: "促销");
        var svc = user.Use<IMessageGatewayService>();

        var hit = await svc.RouteAsync(WxCtx("text", content: "本周促销活动开启", msgId: "m-contains"), TestContext.Current.CancellationToken);
        Assert.False(hit.Handled);
        Assert.NotNull(hit.MatchedRule);
        Assert.Equal(WeChatMessageMatchModes.Contains, hit.MatchedRule!.MatchMode);
    }

    /// <summary>MatchMode=None（纯类型路由——TriggerKey 恒空）→ 任意内容命中。</summary>
    [Fact]
    public async Task MatchMode_None_PureTypeRouting_AnyContent()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();

        var result = await svc.RouteAsync(WxCtx("text", content: "任意内容", msgId: "m-none"), TestContext.Current.CancellationToken);
        Assert.False(result.Handled);
        Assert.NotNull(result.MatchedRule);
        Assert.Equal(WeChatMessageMatchModes.None, result.MatchedRule!.MatchMode);
    }

    // ═══════════════════ MsgType 匹配 ═══════════════════

    /// <summary>MsgType 空 = 通配（全类型命中）；相等 = 仅同类型命中；SortOrder 升序首个命中（相等规则优先于通配）。</summary>
    [Fact]
    public async Task MsgType_Wildcard_And_Equal_SortOrderFirstHit()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        // 通配规则（sortOrder=10，后位）+ image 精确规则（sortOrder=1，前位）——image 消息命中精确；text 消息命中通配
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: null, matchMode: WeChatMessageMatchModes.None, sortOrder: 10);
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: "image", matchMode: WeChatMessageMatchModes.None, sortOrder: 1);
        var svc = user.Use<IMessageGatewayService>();

        var image = await svc.RouteAsync(WxCtx("image", content: null, msgId: "m-img"), TestContext.Current.CancellationToken);
        Assert.False(image.Handled);
        Assert.NotNull(image.MatchedRule);
        Assert.Equal("image", image.MatchedRule!.MsgType);    // 精确规则优先（低 SortOrder）

        var text = await svc.RouteAsync(WxCtx("text", content: "x", msgId: "m-txt"), TestContext.Current.CancellationToken);
        Assert.False(text.Handled);
        Assert.NotNull(text.MatchedRule);
        Assert.Null(text.MatchedRule!.MsgType);               // image 规则不匹配 text → 通配规则命中
    }

    // ═══════════════════ Event 匹配 ═══════════════════

    /// <summary>Event 仅 MsgType=event 参与：event/subscribe 命中 event 规则；text 消息不参与事件匹配（event 规则跳过）。</summary>
    [Fact]
    public async Task Event_Rule_Participates_OnlyWhenMsgTypeEvent()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.LocalOnly, msgType: "event", @event: "subscribe", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();

        var evt = await svc.RouteAsync(WxCtx("event", @event: "subscribe", msgId: null, createTime: 1730000000), TestContext.Current.CancellationToken);
        Assert.False(evt.Handled);
        Assert.NotNull(evt.MatchedRule);
        Assert.Equal("subscribe", evt.MatchedRule!.Event);

        var text = await svc.RouteAsync(WxCtx("text", content: "x", msgId: "m-txt"), TestContext.Current.CancellationToken);
        Assert.False(text.Handled);
        Assert.Null(text.MatchedRule);    // event 规则不参与非 event 消息 → NoRule
    }

    // ═══════════════════ 降级链（decider 直测 + SyncProxy 集成） ═══════════════════

    /// <summary>运行库 decider 直测：SyncProxy 失败（syncSucceeded=false）→ LocalFallback + LocalRender 降级起步。</summary>
    [Fact]
    public void Decider_SyncProxyFailed_DegradesToLocalFallback()
    {
        var rule = new MessageRouteRuleDescriptor(1, "ch", "text", null, WeChatMessageMatchModes.None, null,
            WeChatMessageForwardModes.SyncProxy, 101, 0);

        var decision = WeChatMessageForwardDecider.Decide(rule, syncSucceeded: false);

        Assert.True(decision.IsDegraded);
        Assert.Equal(ForwardAction.LocalFallback, decision.Action);
        Assert.Equal(ForwardDegradeStage.LocalRender, decision.DegradeStage);
    }

    /// <summary>运行库 decider 直测：SyncProxy 成功 → SyncProxy 透传（无降级）。</summary>
    [Fact]
    public void Decider_SyncProxySucceeded_Proxies()
    {
        var rule = new MessageRouteRuleDescriptor(1, "ch", "text", null, WeChatMessageMatchModes.None, null,
            WeChatMessageForwardModes.SyncProxy, 101, 0);

        var decision = WeChatMessageForwardDecider.Decide(rule, syncSucceeded: true);

        Assert.False(decision.IsDegraded);
        Assert.Equal(ForwardAction.SyncProxy, decision.Action);
        Assert.Equal(ForwardDegradeStage.None, decision.DegradeStage);
    }

    /// <summary>降级链步进：LocalRender → Template → DefaultReplyText → None（链尽）。</summary>
    [Fact]
    public void Decider_NextDegradeStage_StepsThroughChain()
    {
        Assert.Equal(ForwardDegradeStage.Template, WeChatMessageForwardDecider.NextDegradeStage(ForwardDegradeStage.LocalRender));
        Assert.Equal(ForwardDegradeStage.DefaultReplyText, WeChatMessageForwardDecider.NextDegradeStage(ForwardDegradeStage.Template));
        Assert.Equal(ForwardDegradeStage.None, WeChatMessageForwardDecider.NextDegradeStage(ForwardDegradeStage.DefaultReplyText));
        Assert.Equal(ForwardDegradeStage.None, WeChatMessageForwardDecider.NextDegradeStage(ForwardDegradeStage.None));
    }

    // ═══════════════════ SyncProxy/PreprocessSync 真透传（V0.3.0——P0-1 选 A 落地） ═══════════════════

    /// <summary>SyncProxy 成功：出站透传（__decrypted_xml 明文 XML 原样 POST 下游）→ 下游完整响应 XML 经
    /// <c>WechatReplyPayload.RawXml</c> <b>原样返回</b>（逐字符相等断言——RawXml 逃逸口防双重信封）+ Handled=true
    /// + 转发日志 Status=Success（响应截断 500 留痕）+ 幂等 Forwarded。</summary>
    [Fact]
    public async Task SyncProxy_Success_ReturnsDownstreamRawXml()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        var stub = new StubDownstreamHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(DownstreamXml, Encoding.UTF8, "text/xml"),
        }));
        Handler = stub;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var epId = await SeedEndpointAsync(user, ChA, "sync-ep", "https://down.example.com");
        var ruleId = await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.SyncProxy, endpointId: epId, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();
        var incomingXml = WeChatOperationsTestSupport.WxXml(("MsgType", "text"), ("Content", "同步透传"));

        var result = await svc.RouteAsync(WxCtx("text", content: "同步透传", msgId: "m-sync-ok", rawXml: incomingXml),
            TestContext.Current.CancellationToken);

        Assert.True(result.Handled);
        Assert.Equal(ForwardAction.SyncProxy, result.Action);
        Assert.Equal(ForwardDegradeStage.None, result.DegradeStage);
        Assert.NotNull(result.Result);
        Assert.Equal(DownstreamXml, result.Result!.RawXml);   // RawXml 原样返回（完整 XML 逐字符相等——防双重信封）
        Assert.NotNull(result.MatchedRule);
        Assert.Equal(WeChatMessageForwardModes.SyncProxy, result.MatchedRule!.ForwardMode);
        // 出站收到原始报文（__decrypted_xml 明文 XML 原样 POST 下游——不重新组装）
        Assert.Single(stub.ReceivedBodies);
        Assert.Equal(incomingXml, stub.ReceivedBodies[0]);
        // 转发日志 Success（响应截断 500 留痕）+ 幂等 Forwarded
        var log = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.Success)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);
        Assert.Equal(ruleId, log!.MatchRuleId);
        Assert.Equal(epId, log.DownstreamEndpointId);
        Assert.Equal(WeChatMessageForwardModes.SyncProxy, log.ForwardMode);
        Assert.Equal(200, log.StatusCode);
        Assert.Equal(DownstreamXml, log.ResponseBody);   // 响应截断 500 留痕（原文短于上限完整保存）
        var idem = await provider.GetRequiredService<IFreeSql>().Select<WeChatMessageIdempotencyEntity>()
            .Where(i => i.ChannelId == ChA).FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WeChatMessageIdempotencyStatus.Forwarded, idem!.LastStatus);
        Assert.Equal(log.Id, idem.ForwardLogId);
    }

    /// <summary>PreprocessSync 成功：本期与 SyncProxy 同语义（透传+降级）——Handled=true + RawXml 原样返回
    /// （前置处理差异——本地渲染结果作透传载荷前缀——归后续迭代，文档注明）。</summary>
    [Fact]
    public async Task PreprocessSync_Success_ProxiesLikeSyncProxy()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        var stub = new StubDownstreamHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(DownstreamXml, Encoding.UTF8, "text/xml"),
        }));
        Handler = stub;
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        var epId = await SeedEndpointAsync(user, ChA, "pre-ep", "https://down.example.com");
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.PreprocessSync, endpointId: epId, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();

        var result = await svc.RouteAsync(WxCtx("text", content: "前置透传", msgId: "m-pre", rawXml: WeChatOperationsTestSupport.WxXml(("MsgType", "text"))),
            TestContext.Current.CancellationToken);

        Assert.True(result.Handled);
        Assert.Equal(ForwardAction.PreprocessSync, result.Action);
        Assert.Equal(DownstreamXml, result.Result!.RawXml);
    }

    /// <summary>SyncProxy 下游响应非 XML（HTTP 200 但 body 非微信被动回复 XML）→ 熔断失败计数 + 转发日志
    /// Status=Fail（ErrorMsg 注明非合法 XML）+ 降级 LocalFallback（Handled=false——handler 本地渲染链）。</summary>
    [Fact]
    public async Task SyncProxy_NonXmlResponse_DegradesLocalFallback()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        var stub = new StubDownstreamHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("error: not a wechat reply xml", Encoding.UTF8, "text/plain"),
        }));
        Handler = stub;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var epId = await SeedEndpointAsync(user, ChA, "sync-bad-ep", "https://down.example.com");
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.SyncProxy, endpointId: epId, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();

        var result = await svc.RouteAsync(WxCtx("text", content: "非XML", msgId: "m-bad-xml", rawXml: WeChatOperationsTestSupport.WxXml(("MsgType", "text"))),
            TestContext.Current.CancellationToken);

        Assert.False(result.Handled);
        Assert.Equal(ForwardAction.LocalFallback, result.Action);
        Assert.Equal(ForwardDegradeStage.LocalRender, result.DegradeStage);
        Assert.Null(result.Result);
        // 转发日志 Fail（非合法 XML）+ ErrorMsg 脱敏注明原因
        var log = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.Fail)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);
        Assert.Contains("非合法微信被动回复 XML", log!.ErrorMsg);
        Assert.DoesNotContain("AuthToken", log.ErrorMsg);   // ErrorMsg 脱敏（不含凭证）
        // 幂等 Forwarded（降级也算已处理）
        var idem = await provider.GetRequiredService<IFreeSql>().Select<WeChatMessageIdempotencyEntity>()
            .Where(i => i.ChannelId == ChA).FirstAsync(TestContext.Current.CancellationToken);
        Assert.Equal(WeChatMessageIdempotencyStatus.Forwarded, idem!.LastStatus);
    }

    /// <summary>SyncProxy 下游 HTTP 失败（500）→ 转发日志 Status=Fail（StatusCode=500）+ 降级 LocalFallback。</summary>
    [Fact]
    public async Task SyncProxy_DownstreamHttp500_DegradesLocalFallback()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        var stub = new StubDownstreamHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        Handler = stub;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var epId = await SeedEndpointAsync(user, ChA, "sync-500-ep", "https://down.example.com");
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.SyncProxy, endpointId: epId, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();

        var result = await svc.RouteAsync(WxCtx("text", content: "下游500", msgId: "m-500", rawXml: WeChatOperationsTestSupport.WxXml(("MsgType", "text"))),
            TestContext.Current.CancellationToken);

        Assert.False(result.Handled);
        Assert.Equal(ForwardAction.LocalFallback, result.Action);
        var log = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.Fail)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);
        Assert.Equal(500, log!.StatusCode);
        Assert.Contains("HTTP 失败", log.ErrorMsg);
    }

    /// <summary>SyncProxy 端点超时（端点 TimeoutSeconds=1，下游延迟 3s）→ 转发日志 Status=Timeout + 降级
    /// LocalFallback（微信 5 秒时限留本地兜底余量——超时不阻断回复）。</summary>
    [Fact]
    public async Task SyncProxy_Timeout_DegradesLocalFallback_TimeoutLog()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        var stub = new StubDownstreamHandler(async (_, ct) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(3), ct);   // 远超端点超时 1s——CancelAfter 触发
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(DownstreamXml, Encoding.UTF8, "text/xml"),
            };
        });
        Handler = stub;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var epId = await SeedEndpointAsync(user, ChA, "sync-slow-ep", "https://down.example.com", timeoutSeconds: 1);
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.SyncProxy, endpointId: epId, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();

        var result = await svc.RouteAsync(WxCtx("text", content: "超时", msgId: "m-timeout", rawXml: WeChatOperationsTestSupport.WxXml(("MsgType", "text"))),
            TestContext.Current.CancellationToken);

        Assert.False(result.Handled);
        Assert.Equal(ForwardAction.LocalFallback, result.Action);
        var log = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.Timeout)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);
        Assert.Contains("超时", log!.ErrorMsg);
    }

    /// <summary>熔断开闸（连续失败 ≥ 阈值 → Open）：第 3 次调用 <b>不再出站</b>（TryCall 拒绝——开窗期直接
    /// LocalFallback 不转发）+ 转发日志 Fail 注明"熔断开闸"（运维对账可见）。</summary>
    [Fact]
    public async Task SyncProxy_CircuitBreaker_Open_NoForward_DirectLocalFallback()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        CircuitBreaker = new WeChatCircuitBreaker(failureThreshold: 2, openWindow: TimeSpan.FromSeconds(60));
        var stub = new StubDownstreamHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)));
        Handler = stub;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var epId = await SeedEndpointAsync(user, ChA, "sync-br-ep", "https://down.example.com");
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.SyncProxy, endpointId: epId, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();

        // 两次失败 → 熔断开闸（关态连续失败 ≥ 2）
        for (var i = 1; i <= 2; i++)
        {
            var r = await svc.RouteAsync(WxCtx("text", content: "熔断", msgId: $"m-br-{i}", rawXml: WeChatOperationsTestSupport.WxXml(("MsgType", "text"))),
                TestContext.Current.CancellationToken);
            Assert.False(r.Handled);
            Assert.Equal(ForwardAction.LocalFallback, r.Action);
        }
        Assert.Equal(2, stub.RequestCount);

        // 第 3 次：开窗期内 TryCall 拒绝 → 不转发（RequestCount 不增）+ Fail 日志注明熔断
        var third = await svc.RouteAsync(WxCtx("text", content: "熔断", msgId: "m-br-3", rawXml: WeChatOperationsTestSupport.WxXml(("MsgType", "text"))),
            TestContext.Current.CancellationToken);
        Assert.False(third.Handled);
        Assert.Equal(ForwardAction.LocalFallback, third.Action);
        Assert.Equal(2, stub.RequestCount);   // 开闸期不转发（无第 3 次出站）
        var brLog = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.Fail)
            .OrderByDescending(l => l.Id).FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(brLog);
        Assert.Contains("熔断开闸", brLog!.ErrorMsg);
    }

    /// <summary>熔断半开探测恢复：开窗期满 → 放行单次半开探测（成功 → RecordSuccess 回关）→ 后续调用正常透传
    /// 接管回复（Handled=true + RawXml 返回）。</summary>
    [Fact]
    public async Task SyncProxy_CircuitBreaker_HalfOpenProbe_Recovers()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        CircuitBreaker = new WeChatCircuitBreaker(failureThreshold: 2, openWindow: TimeSpan.FromMilliseconds(120));
        var failCount = 0;
        var stub = new StubDownstreamHandler((_, _) =>
        {
            if (Interlocked.Increment(ref failCount) <= 2)
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(DownstreamXml, Encoding.UTF8, "text/xml"),
            });
        });
        Handler = stub;
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        var epId = await SeedEndpointAsync(user, ChA, "sync-br2-ep", "https://down.example.com");
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.SyncProxy, endpointId: epId, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();

        // 两次失败 → 开闸
        for (var i = 1; i <= 2; i++)
        {
            var r = await svc.RouteAsync(WxCtx("text", content: "熔断", msgId: $"m-probe-{i}", rawXml: WeChatOperationsTestSupport.WxXml(("MsgType", "text"))),
                TestContext.Current.CancellationToken);
            Assert.False(r.Handled);
        }

        // 开窗期满（120ms）→ 放行单次半开探测 → 下游恢复成功 → RecordSuccess 回关
        await Task.Delay(TimeSpan.FromMilliseconds(300), TestContext.Current.CancellationToken);
        var probe = await svc.RouteAsync(WxCtx("text", content: "探测", msgId: "m-probe-3", rawXml: WeChatOperationsTestSupport.WxXml(("MsgType", "text"))),
            TestContext.Current.CancellationToken);
        Assert.True(probe.Handled);
        Assert.Equal(DownstreamXml, probe.Result!.RawXml);

        // 回关后后续调用正常透传
        var after = await svc.RouteAsync(WxCtx("text", content: "恢复", msgId: "m-probe-4", rawXml: WeChatOperationsTestSupport.WxXml(("MsgType", "text"))),
            TestContext.Current.CancellationToken);
        Assert.True(after.Handled);
        Assert.Equal(DownstreamXml, after.Result!.RawXml);
        Assert.Equal(4, stub.RequestCount);   // 2 失败 + 1 探测 + 1 恢复后
    }

    /// <summary>SyncProxy 幂等联动：同键（MsgId）重复回调 → 幂等跳过（Handled=true + Result=null 静默跳过）——
    /// <b>不重复透传</b>（RequestCount 不增）+ SkippedDuplicate 转发日志留痕。</summary>
    [Fact]
    public async Task SyncProxy_Idempotent_SameKey_SkipsWithoutSecondForward()
    {
        ConfigureGatewayOptions = o => o.GatewayEnabled = true;
        var stub = new StubDownstreamHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(DownstreamXml, Encoding.UTF8, "text/xml"),
        }));
        Handler = stub;
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var epId = await SeedEndpointAsync(user, ChA, "sync-idem-ep", "https://down.example.com");
        await SeedRuleAsync(user, ChA, WeChatMessageForwardModes.SyncProxy, endpointId: epId, msgType: "text", matchMode: WeChatMessageMatchModes.None);
        var svc = user.Use<IMessageGatewayService>();
        var ctx = WxCtx("text", content: "幂等", msgId: "m-idem", rawXml: WeChatOperationsTestSupport.WxXml(("MsgType", "text")));

        var first = await svc.RouteAsync(ctx, TestContext.Current.CancellationToken);
        Assert.True(first.Handled);
        Assert.Equal(DownstreamXml, first.Result!.RawXml);
        Assert.Equal(1, stub.RequestCount);

        var second = await svc.RouteAsync(ctx, TestContext.Current.CancellationToken);   // 同键重复
        Assert.True(second.Handled);
        Assert.Null(second.Result);          // 幂等跳过——静默跳过不回复
        Assert.True(second.SkippedDuplicate);
        Assert.Equal(1, stub.RequestCount);  // 未重复透传

        // SkippedDuplicate 转发日志留痕（审计对账可见）
        var skipLog = await provider.GetRequiredService<IFreeSql>().Select<MessageForwardLogEntity>()
            .Where(l => l.ChannelId == ChA && l.Status == WeChatMessageForwardLogStatus.SkippedDuplicate)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(skipLog);
    }

    // ═══════════════════ 辅助 ═══════════════════

    /// <summary>构造微信消息/事件上下文（WechatEventContext 直构——RouteAsync 入参形态）。</summary>
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

    /// <summary>种子下游端点（DownstreamEndpointEntityDataService 生产路径直写——无鉴权端点；
    /// <paramref name="timeoutSeconds"/> 可覆盖默认 2s——超时测试用）。</summary>
    private static async Task<long> SeedEndpointAsync(DomainUser<TestUserInfo> user, string channelId, string name, string baseUrl,
        int timeoutSeconds = 2)
    {
        var ds = user.Use<DownstreamEndpointEntityDataService>();
        var created = await ds.EntityCreateAsync(new DownstreamEndpointEntity
        {
            ChannelId = channelId,
            Name = name,
            BaseUrl = baseUrl,
            HttpMethod = "POST",
            TimeoutSeconds = timeoutSeconds,
            IsEnabled = true,
            AuthType = WeChatEndpointAuthTypes.None,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, TestContext.Current.CancellationToken);
        return created.Id;
    }
}

/// <summary>
/// 下游出站桩 handler（SyncProxy 透传测试——<see cref="WeChatOperationsTestHost.Handler"/> 赋值，
/// <c>StubHttpClientFactory</c> 包装后经 typed client 打到下游；记录请求体/计数，按委托响应）。
/// </summary>
internal sealed class StubDownstreamHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _responder;
    private int _count;

    /// <summary>出站调用次数（Interlocked——并发安全计数；断言"开闸期不转发/幂等不重复透传"用）。</summary>
    public int RequestCount => Volatile.Read(ref _count);

    /// <summary>收到的出站请求体（<c>__decrypted_xml</c> 明文 XML 原样 POST 断言用）。</summary>
    public List<string> ReceivedBodies { get; } = [];

    /// <summary>构造——按请求委托响应（<c>ct</c> 为客户端 linked CTS——超时测试经
    /// <c>Task.Delay(..., ct)</c> 触发端点 CancelAfter）。</summary>
    public StubDownstreamHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        => _responder = responder ?? throw new ArgumentNullException(nameof(responder));

    /// <inheritdoc />
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Interlocked.Increment(ref _count);
        if (request.Content is not null)
            ReceivedBodies.Add(await request.Content.ReadAsStringAsync(ct));
        return await _responder(request, ct);
    }
}
