using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.WeChat.Tests;

/// <summary>
/// <see cref="WechatProbeEventReplyHandler"/>（诊断探针 handler——方案
/// docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md §3.3/§六）测试。
/// <para>探针<b>无条件注册</b>（<see cref="WeChatFederationServiceCollectionExtensions.AddWeChatFederationChannels"/>
/// 内 TryAddEnumerableConstructible）+ <see cref="IOptions{O}"/> 惰性判断 <see cref="WechatEventReplyOptions"/>
/// （Oracle P1-3）：开关<b>关</b>（默认 false，P1-1 fail-safe）→ 全分支 null（不回复不干预）；
/// 开关<b>开</b> → 可回复分支返回 <c>PROBE:branch=...</c> 可区分文案；<b>门控分支</b>
/// （unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH）基类协议门控吞掉回复 + <see cref="ILogger"/>
/// 输出（<c>PROBE branch=gated swallowed</c>——探针验证门控分支的证据）。</para>
/// <para>覆盖：① 开关关（默认）→ 全分支 null（生产路径宿主注册 + IOptions 默认值）；② 开关开 → 每分支
/// 可区分文案（subscribe/SCAN/CLICK/text/unknown 等 + 显式断言 4 摘录分支）；③ 门控分支开关开 →
/// HandleAsync null + 日志输出；（④ 生产路径：宿主 <c>User.Use&lt;WechatProbeEventReplyHandler&gt;()</c>
/// NoAop 工厂构造——含 options 编程配置）。</para>
/// </summary>
public class WechatProbeEventReplyHandlerTests : WeChatChannelTestHost
{
    // ═══════════════════ 开关关（默认 false——P1-1 fail-safe，Oracle P1-3 惰性判断） ═══════════════════

    /// <summary>开关关（默认）→ 全分支 null（不回复不干预——业务 handler 正常流转）；
    /// 生产路径宿主：AddWeChatFederationChannels 内无条件注册 + IOptions 默认 false。</summary>
    [Fact]
    public async Task Disabled_Default_AllBranches_ReturnNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();                                  // 未配置——默认开关关
        var handler = user.Use<WechatProbeEventReplyHandler>();

        foreach (var (_, ctx) in AllBranchScenarios())
        {
            var reply = await handler.HandleAsync(ctx, CancellationToken.None);
            Assert.Null(reply);                                           // 关 → 无任何分支回复
        }
    }

    /// <summary>开关关（直构 Options.Create 显式 false——惰性判断）→ 全分支 null。</summary>
    [Fact]
    public async Task Disabled_ExplicitFalse_DirectConstruct_AllBranches_ReturnNull()
    {
        var handler = CreateProbe(enabled: false);

        foreach (var (_, ctx) in AllBranchScenarios())
        {
            var reply = await handler.HandleAsync(ctx, CancellationToken.None);
            Assert.Null(reply);
        }
    }

    // ═══════════════════ 开关开——每分支可区分文案 ═══════════════════

    /// <summary>开关开 → 可回复分支全部返回 PROBE:branch=...（每分支文案不同——验证分支命中；
    /// 门控分支回复被基类吞掉返回 null 且不在本矩阵断言）。</summary>
    [Fact]
    public async Task Enabled_AllBranches_ReturnDistinctProbeText()
    {
        var logs = new List<string>();
        var handler = CreateProbe(enabled: true, logs);
        var texts = new List<string>();

        foreach (var (expected, ctx) in NonGatedScenarios())
        {
            var reply = await handler.HandleAsync(ctx, CancellationToken.None);
            Assert.NotNull(reply);                                        // 开关开 → 非门控分支回复
            Assert.Equal("text/xml", reply!.ContentType);
            Assert.Contains(expected, reply.Content);
            texts.Add(reply.Content);
        }

        // 每分支可区分文案——非门控分支回复内容两两不同
        Assert.Equal(texts.Count, texts.Distinct(StringComparer.Ordinal).Count());
    }

    /// <summary>开关开（生产路径宿主 + 编程配置）→ subscribe → PROBE:branch=subscribe key=...（EventKey 携带）。</summary>
    [Fact]
    public async Task Enabled_Subscribe_ReturnsProbeText()
    {
        using var _ = this;
        var (_, user) = BindTestScope(s => s.Configure<WechatEventReplyOptions>(o => o.ProbeEventReplyHandlerEnabled = true));
        var handler = user.Use<WechatProbeEventReplyHandler>();

        var reply = await handler.HandleAsync(
            Ctx("event", "subscribe", WxXml(("MsgType", "event"), ("Event", "subscribe"), ("EventKey", "qrscene_123456"))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("PROBE:branch=subscribe key=qrscene_123456", reply!.Content);
    }

    /// <summary>开关开 → CLICK → PROBE:branch=CLICK key=...（菜单 key）。</summary>
    [Fact]
    public async Task Enabled_Click_ReturnsProbeText()
    {
        var handler = CreateProbe(enabled: true);

        var reply = await handler.HandleAsync(
            Ctx("event", "CLICK", WxXml(("MsgType", "event"), ("Event", "CLICK"), ("EventKey", "V1001_MENU"))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("PROBE:branch=CLICK key=V1001_MENU", reply!.Content);
    }

    /// <summary>开关开 → SCAN → PROBE:branch=SCAN scene=...（场景值去 qrscene_ 前缀）。</summary>
    [Fact]
    public async Task Enabled_Scan_ReturnsProbeText_SceneId()
    {
        var handler = CreateProbe(enabled: true);

        var reply = await handler.HandleAsync(
            Ctx("event", "scan", WxXml(("MsgType", "event"), ("Event", "SCAN"), ("EventKey", "qrscene_1001"))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("PROBE:branch=SCAN scene=1001", reply!.Content);   // SceneId 去前缀
    }

    /// <summary>开关开 → text → PROBE:branch=text content=...（消息原文）。</summary>
    [Fact]
    public async Task Enabled_Text_ReturnsProbeText()
    {
        var handler = CreateProbe(enabled: true);

        var reply = await handler.HandleAsync(
            Ctx("text", eventName: null, xml: null, content: "你好，欢迎使用"),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("PROBE:branch=text content=你好，欢迎使用", reply!.Content);
    }

    /// <summary>开关开 → unknown（未覆盖 msg_type）→ PROBE:branch=unknown msg_type=... event=...（诊断兜底）。</summary>
    [Fact]
    public async Task Enabled_Unknown_ReturnsProbeText()
    {
        var handler = CreateProbe(enabled: true);

        var reply = await handler.HandleAsync(
            Ctx("transfer", eventName: null),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("PROBE:branch=unknown msg_type=transfer event=", reply!.Content);
    }

    // ═══════════════════ 门控分支开关开——回复被基类吞掉 + ILogger 输出 ═══════════════════

    /// <summary>门控：unsubscribe 开关开 → HandleAsync null（基类协议门控吞掉）+ 日志 PROBE branch=gated swallowed。</summary>
    [Fact]
    public async Task Gated_Unsubscribe_Enabled_ReturnsNull_LogsSwallowed()
    {
        var logs = new List<string>();
        var handler = CreateProbe(enabled: true, logs);

        var reply = await handler.HandleAsync(
            Ctx("event", "unsubscribe", WxXml(("MsgType", "event"), ("Event", "unsubscribe"))),
            CancellationToken.None);

        Assert.Null(reply);                                              // 门控分支强制 null
        Assert.Contains(logs, m => m.Contains("PROBE branch=gated swallowed", StringComparison.Ordinal));
        Assert.Contains(logs, m => m.Contains("Event=unsubscribe", StringComparison.Ordinal));
    }

    /// <summary>门控：MASSSENDJOBFINISH 开关开 → 探针已产出 PROBE 载荷但被基类吞掉 → null + 日志。</summary>
    [Fact]
    public async Task Gated_MassSendJobFinish_Enabled_ReturnsNull_LogsSwallowed()
    {
        var logs = new List<string>();
        var handler = CreateProbe(enabled: true, logs);

        var reply = await handler.HandleAsync(
            Ctx("event", "MASSSENDJOBFINISH",
                WxXml(("MsgType", "event"), ("Event", "MASSSENDJOBFINISH"), ("Status", "sendsuccess"), ("SentCount", "98"))),
            CancellationToken.None);

        Assert.Null(reply);
        Assert.Contains(logs, m => m.Contains("PROBE branch=gated swallowed", StringComparison.Ordinal));
        Assert.Contains(logs, m => m.Contains("Event=MASSSENDJOBFINISH", StringComparison.Ordinal));
    }

    /// <summary>门控：TEMPLATESENDJOBFINISH（探针无专分支 → unknown 载荷）仍被基类吞掉 → null + 日志。</summary>
    [Fact]
    public async Task Gated_TemplateSendJobFinish_Enabled_ReturnsNull_LogsSwallowed()
    {
        var logs = new List<string>();
        var handler = CreateProbe(enabled: true, logs);

        var reply = await handler.HandleAsync(
            Ctx("event", "TEMPLATESENDJOBFINISH",
                WxXml(("MsgType", "event"), ("Event", "TEMPLATESENDJOBFINISH"))),
            CancellationToken.None);

        Assert.Null(reply);
        Assert.Contains(logs, m => m.Contains("PROBE branch=gated swallowed", StringComparison.Ordinal));
        Assert.Contains(logs, m => m.Contains("Event=TEMPLATESENDJOBFINISH", StringComparison.Ordinal));
    }

    // ═══════════════════ 辅助 ═══════════════════

    /// <summary>直构探针（不经 DI——测试纯分发/门控/日志逻辑；带 CapturingLogger 收集日志）。</summary>
    private static WechatProbeEventReplyHandler CreateProbe(bool enabled, List<string>? logs = null)
        => new(
            new StubDomainUser(),
            Options.Create(new WechatEventReplyOptions { ProbeEventReplyHandlerEnabled = enabled }),
            logs is null ? null : new CapturingLogger(logs));

    /// <summary>全部演练分支场景（含门控——开关关时全 null / 开关开时门控由基类吞噬）。</summary>
    private static IReadOnlyList<(string? Expected, SsoChannelEventContext Context)> AllBranchScenarios()
    {
        var scenarios = new List<(string?, SsoChannelEventContext)>();
        foreach (var (expected, ctx) in NonGatedScenarios())
            scenarios.Add((expected, ctx));
        scenarios.Add((null, Ctx("event", "unsubscribe", WxXml(("MsgType", "event"), ("Event", "unsubscribe")))));
        scenarios.Add((null, Ctx("event", "MASSSENDJOBFINISH", WxXml(("MsgType", "event"), ("Event", "MASSSENDJOBFINISH")))));
        scenarios.Add((null, Ctx("event", "TEMPLATESENDJOBFINISH", WxXml(("MsgType", "event"), ("Event", "TEMPLATESENDJOBFINISH")))));
        return scenarios;
    }

    /// <summary>非门控分支场景（开关开 → 各返回可区分 PROBE 文案；Expected = 应包含的区分串）。</summary>
    private static IReadOnlyList<(string Expected, SsoChannelEventContext Context)> NonGatedScenarios() =>
    [
        (
            "PROBE:branch=subscribe key=qrscene_123456",
            Ctx("event", "subscribe", WxXml(("MsgType", "event"), ("Event", "subscribe"), ("EventKey", "qrscene_123456")))
        ),
        (
            "PROBE:branch=SCAN scene=1001",
            Ctx("event", "scan", WxXml(("MsgType", "event"), ("Event", "SCAN"), ("EventKey", "qrscene_1001")))
        ),
        (
            "PROBE:branch=LOCATION lat=23.12 lng=113.24 precision=30.5",
            Ctx("event", "location", WxXml(("MsgType", "event"), ("Event", "LOCATION"),
                ("Latitude", "23.12"), ("Longitude", "113.24"), ("Precision", "30.5")))
        ),
        (
            "PROBE:branch=CLICK key=V1001_MENU",
            Ctx("event", "CLICK", WxXml(("MsgType", "event"), ("Event", "CLICK"), ("EventKey", "V1001_MENU")))
        ),
        (
            "PROBE:branch=VIEW url=https://example.com/menu",
            Ctx("event", "view", WxXml(("MsgType", "event"), ("Event", "VIEW"), ("EventKey", "https://example.com/menu")))
        ),
        (
            "PROBE:branch=VIEW_MINIPROGRAM key=appid=wx1",
            Ctx("event", "view_miniprogram", WxXml(("MsgType", "event"), ("Event", "view_miniprogram"), ("EventKey", "appid=wx1")))
        ),
        (
            "PROBE:branch=text content=你好，欢迎使用",
            Ctx("text", eventName: null, xml: null, content: "你好，欢迎使用")
        ),
        (
            "PROBE:branch=image media_id=MEDIA_IMG_1",
            Ctx("image", eventName: null, xml: WxXml(("MsgType", "image"), ("MediaId", "MEDIA_IMG_1")))
        ),
        (
            "PROBE:branch=voice media_id=MEDIA_VOICE_1 format=amr",
            Ctx("voice", eventName: null, xml: WxXml(("MsgType", "voice"), ("MediaId", "MEDIA_VOICE_1"), ("Format", "amr")))
        ),
        (
            "PROBE:branch=video media_id=MEDIA_VIDEO_1 thumb_media_id=THUMB_1",
            Ctx("video", eventName: null, xml: WxXml(("MsgType", "video"), ("MediaId", "MEDIA_VIDEO_1"), ("ThumbMediaId", "THUMB_1")))
        ),
        (
            "PROBE:branch=shortvideo media_id=MEDIA_SHORT_1 thumb_media_id=THUMB_2",
            Ctx("shortvideo", eventName: null, xml: WxXml(("MsgType", "shortvideo"), ("MediaId", "MEDIA_SHORT_1"), ("ThumbMediaId", "THUMB_2")))
        ),
        (
            "PROBE:branch=location lat=23.12 lng=113.24 scale=17 label=广州市",
            Ctx("location", eventName: null, xml: WxXml(("MsgType", "location"), ("Latitude", "23.12"), ("Longitude", "113.24"),
                ("Scale", "17"), ("Label", "广州市")))
        ),
        (
            "PROBE:branch=link title=活动 url=https://example.com/a",
            Ctx("link", eventName: null, xml: WxXml(("MsgType", "link"), ("Title", "活动"), ("Description", "描述"), ("Url", "https://example.com/a")))
        ),
        (
            "PROBE:branch=unknown msg_type=transfer event=",
            Ctx("transfer", eventName: null)
        ),
    ];

    /// <summary>构造事件上下文（msg_type/event/__decrypted_xml/content——参数化，无 channel_id——纯分发直构路径）。</summary>
    private static SsoChannelEventContext Ctx(string msgType, string? eventName, string? xml = null, string? content = null)
    {
        var list = new List<(string, string?)> { ("msg_type", msgType) };
        if (eventName is not null) list.Add(("event", eventName));
        if (content is not null) list.Add(("content", content));
        if (xml is not null) list.Add(("__decrypted_xml", xml));
        return WeChatTestHost.CreateEventContext(list.ToArray());
    }

    private static string WxXml(params (string Name, string Value)[] fields)
    {
        var sb = new System.Text.StringBuilder("<xml>");
        foreach (var (name, value) in fields)
            sb.Append($"<{name}><![CDATA[{value}]]></{name}>");
        return sb.Append("</xml>").ToString();
    }

    /// <summary>捕获 ILogger（收集格式化的日志消息——门控分支日志断言）。</summary>
    private sealed class CapturingLogger : ILogger
    {
        private readonly List<string> _messages;

        public CapturingLogger(List<string> messages) => _messages = messages;

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            => _messages.Add(formatter(state, exception));
    }
}