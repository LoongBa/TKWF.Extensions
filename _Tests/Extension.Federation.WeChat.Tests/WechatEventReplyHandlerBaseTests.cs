using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;
using TKWF.Ext.TrustCenter;

namespace TKWF.Federation.WeChat.Tests;

/// <summary>
/// <see cref="WechatEventReplyHandlerBase"/>（平台基类——二级分发 + 协议门控 + 图文工具 + Parse）测试。
/// <para>方案 docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md §3.2/§六——G1/G2/G3/G6 覆盖。</para>
/// <para>生产路径宿主（<see cref="WeChatChannelTestHost"/> + <c>BindTestScope()</c>
/// + <c>User.Use&lt;T&gt;()</c> 工厂，对齐 EventReplyHandlerBaseTests 形态）+ 文件内探针子类
/// <see cref="DispatchProbeHandler"/>（记录 Invoked 分支 + LastCtx 强类型上下文 + 可配置 Payload）。</para>
/// <para>覆盖：① 二级分发映射——各 MsgType/Event → 对应虚方法被调 + 强类型参数正确（EventKey / SceneId 去
/// qrscene_ 前缀 / 经纬度 / Scale / Label / Content / MediaId / Status 等——自 Parameters + __decrypted_xml 提取）；
/// ② 未覆盖分支 → OnDefaultAsync → null（不回复不拦截，Oracle P0-2）；③ 协议门控——
/// unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH 虚方法被调（副作用可做）但回复被吞返回 null（G2）；
/// ④ 图文工具——News 多图文信封（<c>&lt;ArticleCount&gt;</c> + <c>&lt;Articles&gt;</c>）+ 文本回退 + News 优先（G6）；
/// ⑤ RawXml 逃逸口（V0.4.2 Oracle P0-1 选 A——原始 XML 原样透传优先（Content == RawXml 防双重信封）+
/// 空/空白回退 Content 零回归）；
/// ⑥ Parse——__decrypted_xml 缺失容错 / 字段提取 / 非法 XML 抛 InvalidOperationException。</para>
/// </summary>
public class WechatEventReplyHandlerBaseTests : WeChatChannelTestHost
{
    /// <summary>探针子类集合注册（对齐 EventReplyHandlerBaseTests——探针经 TryAddEnumerableConstructible
    /// 生产形态注册；测试内 <c>User.Use&lt;DispatchProbeHandler&gt;()</c> NoAop 工厂直建）。</summary>
    protected override void ConfigureHost(IServiceCollection services)
    {
        base.ConfigureHost(services);
        services.TryAddEnumerableConstructible<IEventReplyHandler, DispatchProbeHandler>();
    }

    // ═══════════════════ 二级分发映射（G1/G3——虚方法被调 + 强类型参数正确） ═══════════════════

    /// <summary>二级分发：subscribe 事件 → OnSubscribeAsync（EventKey 带参二维码场景值承载——强类型上下文）。</summary>
    [Fact]
    public async Task Dispatch_Subscribe_OnSubscribeAsync_ContextTyped()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"),
                ("event", "subscribe"),
                ("from_user_name", "openid-1"),
                ("to_user_name", "gh_test"),
                ("__decrypted_xml", WxXml(
                    ("ToUserName", "gh_test"), ("FromUserName", "openid-1"),
                    ("MsgType", "event"), ("Event", "subscribe"), ("EventKey", "qrscene_123456")))),
            CancellationToken.None);

        Assert.Equal("subscribe", handler.Invoked);
        Assert.Equal("subscribe", handler.LastCtx!.Event);
        Assert.Equal("qrscene_123456", handler.LastCtx!.EventKey);   // 带参二维码 EventKey 原样保留
        Assert.Equal("openid-1", handler.LastCtx!.OpenId);
        Assert.Equal("gh_test", handler.LastCtx!.ToUserName);
        Assert.NotNull(reply);                                        // 默认载荷非 null → 文本信封
    }

    /// <summary>二级分发：SCAN 事件（已关注用户扫码带参）→ OnScanAsync（SceneId 去 qrscene_ 前缀）。</summary>
    [Fact]
    public async Task Dispatch_Scan_OnScanAsync_SceneIdStripsQrscenePrefix()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"),
                ("event", "scan"),
                ("from_user_name", "openid-1"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "event"), ("Event", "SCAN"), ("EventKey", "qrscene_1001")))),
            CancellationToken.None);

        Assert.Equal("scan", handler.Invoked);
        Assert.Equal("qrscene_1001", handler.LastCtx!.EventKey);      // EventKey 保留前缀
        Assert.Equal("1001", handler.LastCtx!.SceneId);               // 强类型场景值去前缀
        Assert.NotNull(reply);
    }

    /// <summary>二级分发：LOCATION 事件（上报地理位置事件）→ OnLocationEventAsync（经纬度/精度强类型）。</summary>
    [Fact]
    public async Task Dispatch_LocationEvent_OnLocationEventAsync_TypedCoords()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"),
                ("event", "location"),
                ("from_user_name", "openid-1"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "event"), ("Event", "LOCATION"),
                    ("Latitude", "23.12"), ("Longitude", "113.24"), ("Precision", "30.5")))),
            CancellationToken.None);

        Assert.Equal("location_event", handler.Invoked);
        Assert.Equal(23.12, handler.LastCtx!.Latitude!.Value, 6);     // 自 __decrypted_xml 提取
        Assert.Equal(113.24, handler.LastCtx!.Longitude!.Value, 6);
        Assert.Equal(30.5, handler.LastCtx!.Precision!.Value, 6);
        Assert.NotNull(reply);
    }

    /// <summary>二级分发：CLICK 事件（点击菜单拉取消息）→ OnClickAsync（EventKey 菜单 key）。</summary>
    [Fact]
    public async Task Dispatch_Click_OnClickAsync_WithEventKey()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"),
                ("event", "CLICK"),
                ("from_user_name", "openid-1"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "event"), ("Event", "CLICK"), ("EventKey", "V1001_MENU")))),
            CancellationToken.None);

        Assert.Equal("click", handler.Invoked);
        Assert.Equal("V1001_MENU", handler.LastCtx!.EventKey);
        Assert.NotNull(reply);
    }

    /// <summary>二级分发：VIEW 事件（点击菜单跳转链接）→ OnViewAsync（EventKey = URL）。</summary>
    [Fact]
    public async Task Dispatch_View_OnViewAsync_WithUrl()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"),
                ("event", "view"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "event"), ("Event", "VIEW"), ("EventKey", "https://example.com/menu")))),
            CancellationToken.None);

        Assert.Equal("view", handler.Invoked);
        Assert.Equal("https://example.com/menu", handler.LastCtx!.EventKey);
        Assert.NotNull(reply);
    }

    /// <summary>二级分发：VIEW_MINIPROGRAM 事件 → OnViewMiniProgramAsync（EventKey = 小程序页面参数）。</summary>
    [Fact]
    public async Task Dispatch_ViewMiniProgram_OnViewMiniProgramAsync()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"),
                ("event", "view_miniprogram"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "event"), ("Event", "view_miniprogram"),
                    ("EventKey", "appid=wx12345&path=pages/index")))),
            CancellationToken.None);

        Assert.Equal("view_miniprogram", handler.Invoked);
        Assert.Contains("appid=wx12345", handler.LastCtx!.EventKey);
        Assert.NotNull(reply);                                        // 非门控分支——回复不被吞
    }

    /// <summary>二级分发 + 门控：MASSSENDJOBFINISH 群发结果事件 → OnMassSendJobFinishAsync
    /// （Status/TotalCount/SentCount 等强类型统计）但回复被基类协议门控吞掉 → HandleAsync null。</summary>
    [Fact]
    public async Task Dispatch_MassSendJobFinish_OnMassSendJobFinishAsync_TypedStats_ReplyGated()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"),
                ("event", "MASSSENDJOBFINISH"),
                ("from_user_name", "openid-1"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "event"), ("Event", "MASSSENDJOBFINISH"),
                    ("Status", "success"), ("TotalCount", "1000"), ("FilterCount", "999"),
                    ("SentCount", "998"), ("ErrorCount", "2")))),
            CancellationToken.None);

        Assert.Equal("masssendjobfinish", handler.Invoked);           // 副作用可做（虚方法已执行）
        Assert.Equal("success", handler.LastCtx!.Status);
        Assert.Equal(1000, handler.LastCtx!.TotalCount);
        Assert.Equal(999, handler.LastCtx!.FilterCount);
        Assert.Equal(998, handler.LastCtx!.SentCount);
        Assert.Equal(2, handler.LastCtx!.ErrorCount);
        Assert.Null(reply);                                           // 协议门控——回复被吞（G2）
    }

    /// <summary>二级分发：text 消息 → OnTextAsync（Content 自 Parameters 富化通道字段）。</summary>
    [Fact]
    public async Task Dispatch_Text_OnTextAsync_ContentFromParameters()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "text"),
                ("content", "你好，欢迎使用"),
                ("from_user_name", "openid-1")),
            CancellationToken.None);

        Assert.Equal("text", handler.Invoked);
        Assert.Equal("你好，欢迎使用", handler.LastCtx!.Content);
        Assert.NotNull(reply);
    }

    /// <summary>二级分发：image 消息 → OnImageAsync（MediaId/PicUrl 自 __decrypted_xml 提取）。</summary>
    [Fact]
    public async Task Dispatch_Image_OnImageAsync_MediaIdPicUrl()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "image"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "image"), ("MediaId", "MEDIA_IMG_1"), ("PicUrl", "http://img.example.com/1.png")))),
            CancellationToken.None);

        Assert.Equal("image", handler.Invoked);
        Assert.Equal("MEDIA_IMG_1", handler.LastCtx!.MediaId);
        Assert.Equal("http://img.example.com/1.png", handler.LastCtx!.PicUrl);
        Assert.NotNull(reply);
    }

    /// <summary>二级分发：voice 消息 → OnVoiceAsync（MediaId/Format）。</summary>
    [Fact]
    public async Task Dispatch_Voice_OnVoiceAsync_Format()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "voice"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "voice"), ("MediaId", "MEDIA_VOICE_1"), ("Format", "amr")))),
            CancellationToken.None);

        Assert.Equal("voice", handler.Invoked);
        Assert.Equal("MEDIA_VOICE_1", handler.LastCtx!.MediaId);
        Assert.Equal("amr", handler.LastCtx!.Format);
        Assert.NotNull(reply);
    }

    /// <summary>二级分发：video 消息 → OnVideoAsync（MediaId/ThumbMediaId）。</summary>
    [Fact]
    public async Task Dispatch_Video_OnVideoAsync_ThumbMediaId()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "video"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "video"), ("MediaId", "MEDIA_VIDEO_1"), ("ThumbMediaId", "THUMB_1")))),
            CancellationToken.None);

        Assert.Equal("video", handler.Invoked);
        Assert.Equal("MEDIA_VIDEO_1", handler.LastCtx!.MediaId);
        Assert.Equal("THUMB_1", handler.LastCtx!.ThumbMediaId);
        Assert.NotNull(reply);
    }

    /// <summary>二级分发：shortvideo 消息 → OnShortVideoAsync（MediaId/ThumbMediaId）。</summary>
    [Fact]
    public async Task Dispatch_ShortVideo_OnShortVideoAsync()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "shortvideo"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "shortvideo"), ("MediaId", "MEDIA_SHORT_1"), ("ThumbMediaId", "THUMB_2")))),
            CancellationToken.None);

        Assert.Equal("shortvideo", handler.Invoked);
        Assert.Equal("MEDIA_SHORT_1", handler.LastCtx!.MediaId);
        Assert.Equal("THUMB_2", handler.LastCtx!.ThumbMediaId);
        Assert.NotNull(reply);
    }

    /// <summary>二级分发：location 消息 → OnLocationAsync（Latitude/Longitude/Scale/Label——全部自 __decrypted_xml）。</summary>
    [Fact]
    public async Task Dispatch_Location_OnLocationAsync_TypedFields()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "location"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "location"), ("Latitude", "23.12"), ("Longitude", "113.24"),
                    ("Scale", "17"), ("Label", "广州市越秀区")))),
            CancellationToken.None);

        Assert.Equal("location", handler.Invoked);
        Assert.Equal(23.12, handler.LastCtx!.Latitude!.Value, 6);
        Assert.Equal(113.24, handler.LastCtx!.Longitude!.Value, 6);
        Assert.Equal("17", handler.LastCtx!.Scale);                    // Scale 微信 XML 文本数字
        Assert.Equal("广州市越秀区", handler.LastCtx!.Label);
        Assert.NotNull(reply);
    }

    /// <summary>二级分发：link 消息 → OnLinkAsync（Title/Description/Url）。</summary>
    [Fact]
    public async Task Dispatch_Link_OnLinkAsync_TitleDescUrl()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "link"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "link"), ("Title", "活动链接"), ("Description", "点击查看"),
                    ("Url", "https://example.com/a")))),
            CancellationToken.None);

        Assert.Equal("link", handler.Invoked);
        Assert.Equal("活动链接", handler.LastCtx!.Title);
        Assert.Equal("点击查看", handler.LastCtx!.Description);
        Assert.Equal("https://example.com/a", handler.LastCtx!.Url);
        Assert.NotNull(reply);
    }

    // ═══════════════════ 未覆盖分支 → OnDefaultAsync → null（Oracle P0-2 默认语义） ═══════════════════

    /// <summary>未覆盖 msg_type（如 transfer）→ OnDefaultAsync → 载荷 null → HandleAsync null（不回复不拦截）。</summary>
    [Fact]
    public async Task Dispatch_UnknownMsgType_OnDefaultAsync_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();
        handler.Payload = null;                                        // 兜底返回 null——证明默认不回复

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(("msg_type", "transfer"), ("from_user_name", "openid-1")),
            CancellationToken.None);

        Assert.Equal("default", handler.Invoked);
        Assert.Null(reply);
    }

    /// <summary>缺 msg_type → OnDefaultAsync → null（Parse 容错——未富化字段 null）。</summary>
    [Fact]
    public async Task Dispatch_MissingMsgType_OnDefaultAsync_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();
        handler.Payload = null;

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(("from_user_name", "openid-1")),
            CancellationToken.None);

        Assert.Equal("default", handler.Invoked);
        Assert.Null(reply);
    }

    /// <summary>未覆盖 event 类型（如 WATCH——非门控；templatessendjobfinish 见门控用例）→ OnDefaultAsync → 载荷 null → null。</summary>
    [Fact]
    public async Task Dispatch_UnknownEvent_OnDefaultAsync_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();
        handler.Payload = null;

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "WATCH"), ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "WATCH")))),
            CancellationToken.None);

        Assert.Equal("default", handler.Invoked);
        Assert.Null(reply);
    }

    /// <summary>ChannelId 反查失败（registry 无此通道）→ Channel=null（Base 容错）→ 二级分发仍执行、回复不受影响。</summary>
    [Fact]
    public async Task HandleAsync_ChannelIdNotInRegistry_DispatchStillRuns()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "CLICK"), ("channel_id", "unknown-channel"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "CLICK"), ("EventKey", "KEY_1")))),
            CancellationToken.None);

        Assert.Equal("click", handler.Invoked);                         // 反查失败不阻断分发
        Assert.Equal("KEY_1", handler.LastCtx!.EventKey);
        Assert.Null(handler.LastCtx!.Channel);                          // Channel=null 容错
        Assert.NotNull(reply);
    }

    // ═══════════════════ 协议门控（G2——unsubscribe/MASSSENDJOBFINISH/TEMPLATESENDJOBFINISH 回复被吞） ═══════════════════

    /// <summary>门控：unsubscribe 事件——OnUnsubscribeAsync 已执行（副作用如记录取关可做）但回复被吞返回 null。</summary>
    [Fact]
    public async Task Gate_Unsubscribe_VirtualInvoked_ReplySwallowed()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();                 // Payload 默认非 null——证明"有载荷仍被吞"

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "unsubscribe"), ("from_user_name", "openid-1"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "unsubscribe")))),
            CancellationToken.None);

        Assert.Equal("unsubscribe", handler.Invoked);                   // 虚方法被调（副作用可做）
        Assert.Equal("unsubscribe", handler.LastCtx!.Event);
        Assert.NotNull(handler.Payload);                                // 业务载荷已构造
        Assert.Null(reply);                                             // 协议门控——回复被吞
    }

    /// <summary>门控：MASSSENDJOBFINISH——虚方法已执行 + 有载荷但回复被吞（群发结果不可被动回复）。</summary>
    [Fact]
    public async Task Gate_MassSendJobFinish_VirtualInvoked_ReplySwallowed()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "MASSSENDJOBFINISH"),
                ("__decrypted_xml", WxXml(
                    ("MsgType", "event"), ("Event", "MASSSENDJOBFINISH"), ("Status", "sendsuccess")))),
            CancellationToken.None);

        Assert.Equal("masssendjobfinish", handler.Invoked);
        Assert.Equal("sendsuccess", handler.LastCtx!.Status);
        Assert.NotNull(handler.Payload);
        Assert.Null(reply);
    }

    /// <summary>门控：TEMPLATESENDJOBFINISH（无专分支虚方法——走 OnDefaultAsync 默认）有载荷仍被吞。</summary>
    [Fact]
    public async Task Gate_TemplateSendJobFinish_DefaultInvoked_ReplySwallowed()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "TEMPLATESENDJOBFINISH"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "TEMPLATESENDJOBFINISH")))),
            CancellationToken.None);

        Assert.Equal("default", handler.Invoked);                       // 无专分支 → 默认虚方法
        Assert.NotNull(handler.Payload);                                // 有载荷仍被吞
        Assert.Null(reply);
    }

    /// <summary>门控 OrdinalIgnoreCase：事件大小写混合（Unsubscribe 首字母大写）仍命中门控集合——回复被吞。</summary>
    [Fact]
    public async Task Gate_MixedCaseEvent_OrdinalIgnoreCase_Swallowed()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "Unsubscribe"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "Unsubscribe")))),
            CancellationToken.None);

        Assert.Equal("unsubscribe", handler.Invoked);                   // 大小写不敏感二级分发
        Assert.Null(reply);                                             // 门控吞掉（OrdinalIgnoreCase）
    }

    // ═══════════════════ 图文工具（G6——信封组装/多图文/优先级） ═══════════════════

    /// <summary>图文：子类返回 News 多图文 → HandleAsync 输出 news XML 信封（ArticleCount=2 + Articles 两条 item）。</summary>
    [Fact]
    public async Task BuildReply_NewsMultiArticle_ReturnsNewsEnvelope()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();
        handler.Payload = new WechatReplyPayload(News:
        [
            new EventReplyNewsArticle("标题一", "描述一", "https://example.com/1", "https://img.example.com/1.png"),
            new EventReplyNewsArticle("标题二", "描述二", "https://example.com/2"),
        ]);

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "subscribe"),
                ("from_user_name", "openid-1"), ("to_user_name", "gh_test"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "subscribe")))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal("text/xml", reply!.ContentType);
        Assert.Contains("<MsgType><![CDATA[news]]></MsgType>", reply.Content);
        Assert.Contains("<ArticleCount>2</ArticleCount>", reply.Content);
        Assert.Contains("<Articles>", reply.Content);
        Assert.Equal(2, CountOccurrences(reply.Content, "<item>"));
        Assert.Equal(2, CountOccurrences(reply.Content, "</item>"));
        // 信封地址字段：ToUserName = 原消息发送者（openid）/ FromUserName = 公众号 id
        Assert.Contains("<ToUserName><![CDATA[openid-1]]></ToUserName>", reply.Content);
        Assert.Contains("<FromUserName><![CDATA[gh_test]]></FromUserName>", reply.Content);
        Assert.Contains("<Title><![CDATA[标题一]]></Title>", reply.Content);
        Assert.Contains("<Url><![CDATA[https://example.com/2]]></Url>", reply.Content);
    }

    /// <summary>图文工具：核验子类返回 null 载荷 → HandleAsync null（不占 first-non-null-wins 空槽）。</summary>
    [Fact]
    public async Task BuildReply_NullPayload_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();
        handler.Payload = null;

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "subscribe"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "subscribe")))),
            CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>图文工具：空载荷（Content 与 News 均空）→ BuildReplyResult null → HandleAsync null。</summary>
    [Fact]
    public async Task BuildReply_EmptyPayload_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();
        handler.Payload = new WechatReplyPayload();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "subscribe"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "subscribe")))),
            CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>图文工具：仅 Content → 文本信封（text 回退路径——News 为空时用文本）。</summary>
    [Fact]
    public async Task BuildReply_ContentOnly_ReturnsTextEnvelope()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();
        handler.Payload = new WechatReplyPayload(Content: "欢迎关注！");

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "subscribe"),
                ("from_user_name", "openid-1"), ("to_user_name", "gh_test"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "subscribe")))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal("text/xml", reply!.ContentType);
        Assert.Contains("<MsgType><![CDATA[text]]></MsgType>", reply.Content);
        Assert.Contains("<Content><![CDATA[欢迎关注！]]></Content>", reply.Content);
        Assert.Contains("<ToUserName><![CDATA[openid-1]]></ToUserName>", reply.Content);
        Assert.Contains("<FromUserName><![CDATA[gh_test]]></FromUserName>", reply.Content);
    }

    /// <summary>图文工具：News 优先（Content 与 News 并存时——news 信封胜出，文本不产出）。</summary>
    [Fact]
    public async Task BuildReply_NewsPriorityOverContent()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();
        handler.Payload = new WechatReplyPayload(
            Content: "文本回退不应出现",
            News: [new EventReplyNewsArticle("图文标题", "图文描述", "https://example.com/n")]);

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "subscribe"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "subscribe")))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<MsgType><![CDATA[news]]></MsgType>", reply.Content);
        Assert.Contains("<ArticleCount>1</ArticleCount>", reply.Content);
        Assert.DoesNotContain("<MsgType><![CDATA[text]]></MsgType>", reply.Content);
        Assert.DoesNotContain("文本回退不应出现", reply.Content);
    }

    /// <summary>RawXml 逃逸口：非空 → BuildReplyResult 直接返回该原始 XML 原样（Content == RawXml，
    /// 不包 text/news 信封——Content/News 并存仍被 RawXml 压制，防双重信封，V0.4.2 Oracle P0-1 选 A）。</summary>
    [Fact]
    public async Task BuildReply_RawXml_PassthroughEnvelope()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();
        const string rawXml = "<xml><ToUserName><![CDATA[openid-1]]></ToUserName>" +
                              "<FromUserName><![CDATA[gh_test]]></FromUserName>" +
                              "<CreateTime>1234567890</CreateTime>" +
                              "<MsgType><![CDATA[text]]></MsgType>" +
                              "<Content><![CDATA[下游自组信封]]></Content></xml>";
        handler.Payload = new WechatReplyPayload(
            Content: "文本回退不应出现",
            News: [new EventReplyNewsArticle("图文标题", "图文描述", "https://example.com/n")],
            RawXml: rawXml);

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "subscribe"),
                ("from_user_name", "openid-1"), ("to_user_name", "gh_test"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "subscribe")))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal("text/xml", reply!.ContentType);
        Assert.Equal(rawXml, reply.Content);            // 原样透传——不包任何信封（防双重信封）
        Assert.DoesNotContain("文本回退不应出现", reply.Content);
        Assert.DoesNotContain("<ArticleCount>", reply.Content);
    }

    /// <summary>RawXml 逃逸口回退：空/空白（非空判定不命中）→ 回退 Content 既有 text 路径（存量零回归）。</summary>
    [Fact]
    public async Task BuildReply_RawXmlNullOrWhitespace_FallsBackToContent()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();
        handler.Payload = new WechatReplyPayload(Content: "欢迎关注！", RawXml: "   ");

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventContext(
                ("msg_type", "event"), ("event", "subscribe"),
                ("from_user_name", "openid-1"), ("to_user_name", "gh_test"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "subscribe")))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal("text/xml", reply!.ContentType);
        Assert.Contains("<MsgType><![CDATA[text]]></MsgType>", reply.Content);
        Assert.Contains("<Content><![CDATA[欢迎关注！]]></Content>", reply.Content);
    }

    // ═══════════════════ Parse（__decrypted_xml 数据源——Oracle P1-4） ═══════════════════

    /// <summary>Parse 容错：__decrypted_xml 缺失 → 不抛，未富化字段全 null（兼容未解密场景）。</summary>
    [Fact]
    public void Parse_MissingDecryptedXml_TolerantNoThrow()
    {
        var ctx = WechatEventContext.Parse(new Dictionary<string, string?>
        {
            ["msg_type"] = "text",
            ["content"] = "你好",
            ["from_user_name"] = "openid-1",
            ["to_user_name"] = "gh_test",
        });

        Assert.Equal("text", ctx.MsgType);
        Assert.Equal("你好", ctx.Content);
        Assert.Equal("openid-1", ctx.OpenId);                           // OpenId = external_user_id ?? from_user_name
        Assert.Equal("gh_test", ctx.ToUserName);
        Assert.Null(ctx.EventKey);                                      // 未富化字段 null（容错不抛）
        Assert.Null(ctx.Latitude);
        Assert.Null(ctx.Scale);
        Assert.Null(ctx.MediaId);
        Assert.Null(ctx.Status);
        Assert.Null(ctx.Channel);                                       // Channel 由基类反查富化（Parse 不设）
    }

    /// <summary>Parse 字段提取：__decrypted_xml EventKey/经纬度/PicUrl/群发统计等未富化字段正确类型化。 </summary>
    [Fact]
    public void Parse_WithDecryptedXml_ExtractsTypedFields()
    {
        var ctx = WechatEventContext.Parse(new Dictionary<string, string?>
        {
            ["msg_type"] = "event",
            ["event"] = "SCAN",
            ["external_user_id"] = "openid-9",
            ["uid"] = "u-1",
            ["channel_id"] = "wx-1",
            ["__decrypted_xml"] = WxXml(
                ("ToUserName", "gh_test"), ("FromUserName", "openid-9"),
                ("MsgType", "event"), ("Event", "SCAN"), ("EventKey", "qrscene_888"),
                ("Latitude", "23.12"), ("Longitude", "113.24"), ("Precision", "30.5"),
                ("Scale", "17"), ("Label", "广州市"),
                ("MediaId", "MEDIA_1"), ("PicUrl", "http://img.example.com/1.png"),
                ("Format", "amr"), ("ThumbMediaId", "THUMB_9"),
                ("Title", "标题"), ("Description", "描述"), ("Url", "https://example.com/9"),
                ("Status", "success"), ("TotalCount", "100"),
                ("FilterCount", "99"), ("SentCount", "98"), ("ErrorCount", "1")),
        });

        Assert.Equal("event", ctx.MsgType);
        Assert.Equal("SCAN", ctx.Event);
        Assert.Equal("openid-9", ctx.OpenId);                           // external_user_id 优先
        Assert.Equal("u-1", ctx.UId);
        Assert.Equal("wx-1", ctx.ChannelId);
        Assert.Equal("qrscene_888", ctx.EventKey);
        Assert.Equal("888", ctx.SceneId);                               // 去 qrscene_ 前缀
        Assert.Equal(23.12, ctx.Latitude!.Value, 6);
        Assert.Equal(113.24, ctx.Longitude!.Value, 6);
        Assert.Equal(30.5, ctx.Precision!.Value, 6);
        Assert.Equal("17", ctx.Scale);
        Assert.Equal("广州市", ctx.Label);
        Assert.Equal("MEDIA_1", ctx.MediaId);
        Assert.Equal("http://img.example.com/1.png", ctx.PicUrl);
        Assert.Equal("amr", ctx.Format);
        Assert.Equal("THUMB_9", ctx.ThumbMediaId);
        Assert.Equal("标题", ctx.Title);
        Assert.Equal("描述", ctx.Description);
        Assert.Equal("https://example.com/9", ctx.Url);
        Assert.Equal("success", ctx.Status);
        Assert.Equal(100, ctx.TotalCount);
        Assert.Equal(99, ctx.FilterCount);
        Assert.Equal(98, ctx.SentCount);
        Assert.Equal(1, ctx.ErrorCount);
    }

    /// <summary>SceneId 属性：非 qrscene_ 前缀场景值原样返回；EventKey 为空返回 null。</summary>
    [Fact]
    public void SceneId_Property_RawOrNull()
    {
        // 无前缀 EventKey → 原样
        Assert.Equal("order_001", SceneIdOf("order_001"));
        // EventKey 空 → null
        Assert.Null(SceneIdOf(null));
        Assert.Null(SceneIdOf(""));
    }

    /// <summary>Parse 非法 XML（__decrypted_xml 不可解析——已解密数据应可解析）→ 抛 InvalidOperationException（对齐 WeChatEventCrypto 形态）。</summary>
    [Fact]
    public void Parse_InvalidXml_ThrowsInvalidOperationException()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => WechatEventContext.Parse(
            new Dictionary<string, string?>
            {
                ["msg_type"] = "event",
                ["__decrypted_xml"] = "<xml><Event>CLICK</Event>",       // 未闭合——非法 XML
            }));

        Assert.Contains("非法", ex.Message);
    }

    /// <summary>Parse 参数包 null → ArgumentNullException（防御——装配层 Dictionary 不可能是 null）。</summary>
    [Fact]
    public void Parse_NullParameters_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => WechatEventContext.Parse(null!));
    }

    // ═══════════════════ 入口守卫 ═══════════════════

    /// <summary>HandleAsync(context: null) → null（防护装配层空上下文）。</summary>
    [Fact]
    public async Task HandleAsync_NullContext_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(null!, CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>HandleAsync(Parameters: null) → null（防护空参数包）。</summary>
    [Fact]
    public async Task HandleAsync_NullParameters_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();

        var reply = await handler.HandleAsync(
            new SsoChannelEventContext(null!), CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>HandleAsync(空参数包) → 未覆盖分支 OnDefaultAsync（载荷 null → 不回复）。</summary>
    [Fact]
    public async Task HandleAsync_EmptyParameters_OnDefault_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<DispatchProbeHandler>();
        handler.Payload = null;

        var reply = await handler.HandleAsync(
            new SsoChannelEventContext(new Dictionary<string, string?>()), CancellationToken.None);

        Assert.Equal("default", handler.Invoked);
        Assert.Null(reply);
    }

    /// <summary>构造微信内层消息 XML（CDATA 包裹——对齐 WeChatEventChannelTests.BuildValidEvent 内层形态；
    /// 即 __decrypted_xml 数据源形态，Oracle P1-4）。</summary>
    private static string WxXml(params (string Name, string Value)[] fields)
    {
        var sb = new StringBuilder("<xml>");
        foreach (var (name, value) in fields)
            sb.Append($"<{name}><![CDATA[{value}]]></{name}>");
        return sb.Append("</xml>").ToString();
    }

    private static string? SceneIdOf(string? eventKey)
        => WechatEventContext.Parse(new Dictionary<string, string?>
        {
            ["msg_type"] = "event",
            ["event"] = "scan",
            ["__decrypted_xml"] = eventKey is null ? null : WxXml(("EventKey", eventKey)),
        }).SceneId;

    private static int CountOccurrences(string text, string needle)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(needle, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += needle.Length;
        }
        return count;
    }

    /// <summary>
    /// 二级分发探针子类（对齐 EventReplyHandlerBaseTests.ProbeEventReplyHandler 模式）——
    /// 覆盖全部分支虚方法：记录 Invoked 分支名 + LastCtx（强类型上下文），返回可配置 <see cref="Payload"/>
    /// （默认非 null 文本载荷——门控用例证明"有载荷仍被吞"；置 null 证明默认不回复）。
    /// </summary>
    private sealed class DispatchProbeHandler : WechatEventReplyHandlerBase
    {
        public DispatchProbeHandler(IDomainUser user, ILogger? logger = null) : base(user, logger)
        {
        }

        /// <summary>最近被调用的分支名（subscribe/scan/click/.../default）。</summary>
        public string? Invoked { get; private set; }

        /// <summary>最近一次分发收到的强类型上下文（分支断言用）。</summary>
        public WechatEventContext? LastCtx { get; private set; }

        /// <summary>可配置业务载荷（默认非 null——协议门控用例验证回复被吞；置 null = 不回复）。</summary>
        public WechatReplyPayload? Payload { get; set; } = new WechatReplyPayload(Content: "probe-reply");

        protected override Task<WechatReplyPayload?> OnSubscribeAsync(WechatEventContext ctx, CancellationToken ct) => Record("subscribe", ctx);
        protected override Task<WechatReplyPayload?> OnUnsubscribeAsync(WechatEventContext ctx, CancellationToken ct) => Record("unsubscribe", ctx);
        protected override Task<WechatReplyPayload?> OnScanAsync(WechatEventContext ctx, CancellationToken ct) => Record("scan", ctx);
        protected override Task<WechatReplyPayload?> OnLocationEventAsync(WechatEventContext ctx, CancellationToken ct) => Record("location_event", ctx);
        protected override Task<WechatReplyPayload?> OnClickAsync(WechatEventContext ctx, CancellationToken ct) => Record("click", ctx);
        protected override Task<WechatReplyPayload?> OnViewAsync(WechatEventContext ctx, CancellationToken ct) => Record("view", ctx);
        protected override Task<WechatReplyPayload?> OnViewMiniProgramAsync(WechatEventContext ctx, CancellationToken ct) => Record("view_miniprogram", ctx);
        protected override Task<WechatReplyPayload?> OnMassSendJobFinishAsync(WechatEventContext ctx, CancellationToken ct) => Record("masssendjobfinish", ctx);
        protected override Task<WechatReplyPayload?> OnTextAsync(WechatEventContext ctx, CancellationToken ct) => Record("text", ctx);
        protected override Task<WechatReplyPayload?> OnImageAsync(WechatEventContext ctx, CancellationToken ct) => Record("image", ctx);
        protected override Task<WechatReplyPayload?> OnVoiceAsync(WechatEventContext ctx, CancellationToken ct) => Record("voice", ctx);
        protected override Task<WechatReplyPayload?> OnVideoAsync(WechatEventContext ctx, CancellationToken ct) => Record("video", ctx);
        protected override Task<WechatReplyPayload?> OnShortVideoAsync(WechatEventContext ctx, CancellationToken ct) => Record("shortvideo", ctx);
        protected override Task<WechatReplyPayload?> OnLocationAsync(WechatEventContext ctx, CancellationToken ct) => Record("location", ctx);
        protected override Task<WechatReplyPayload?> OnLinkAsync(WechatEventContext ctx, CancellationToken ct) => Record("link", ctx);
        protected override Task<WechatReplyPayload?> OnDefaultAsync(WechatEventContext ctx, CancellationToken ct) => Record("default", ctx);

        private Task<WechatReplyPayload?> Record(string branch, WechatEventContext ctx)
        {
            Invoked = branch;
            LastCtx = ctx;
            return Task.FromResult(Payload);
        }
    }
}