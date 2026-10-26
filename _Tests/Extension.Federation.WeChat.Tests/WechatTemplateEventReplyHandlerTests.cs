using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.Federation;

namespace TKWF.Federation.WeChat.Tests;

/// <summary>
/// <see cref="WechatTemplateEventReplyHandler"/>（模板驱动 handler——方案
/// docs/Federation/Wechat事件回复Handler基类与二级分发-开发方案.md §3.3/§六）测试。
/// <para>消费方<b>显式注册</b>形态（Oracle P0-1——<c>TryAddEnumerableConstructible&lt;IEventReplyHandler,
/// WechatTemplateEventReplyHandler&gt;</c>）+ Fake <see cref="IChannelRegistry"/> 覆盖（channel_id →
/// ChannelConfig.Extra["news_reply"] 模板 JSON——基类 <c>ResolveChannelAsync</c>
/// 反查数据源）。</para>
/// <para>覆盖：① 模板配置 → 图文回复（占位符 {uid}/{openid}/{channel_id} 替换 + {channel_type}；
/// <c>{ticket}</c> <b>I2-8 起签票替换</b>——本宿主 Fake ILoginEntryService 返回 null（未启用语义）→ 空串替换，回复结构不变）；
/// ② <b>无模板 → null</b>（Oracle P0-2）；
/// ③ Enabled=false → null；④ 模板损坏（非法 JSON）→ null（不拦截）；⑤ Channel 反查失败（channel_id 不在
/// registry）→ null。全部经 <c>OnDefaultAsync</c> 兜底入口（事件路由至默认分支——模板驱动的统一响应语义）。</para>
/// </summary>
public class WechatTemplateEventReplyHandlerTests : WeChatChannelTestHost
{
    private readonly FakeChannelRegistry _registry = new();

    /// <summary>模板 handler 消费方显式注册（Oracle P0-1）+ Fake 通道注册表覆盖（last-registration wins——
    /// <c>User.Use&lt;IChannelRegistry&gt;()</c> 解析 Fake；channel_id → Extra["news_reply"] 模板数据源）。</summary>
    protected override void ConfigureHost(IServiceCollection services)
    {
        base.ConfigureHost(services);

        // 消费方显式注册（方案 §3.5——平台库不默认注册 handler 实例）
        services.TryAddEnumerableConstructible<IEventReplyHandler, WechatTemplateEventReplyHandler>();

        // 覆盖 CompositeChannelRegistry——模板测试聚焦 Channel.Extra 驱动（Fake 通道映射）
        services.AddScoped<IChannelRegistry>(_ => _registry);
    }

    /// <summary>模板驱动：配置模板（title/description/link/pic_url/ttl_seconds/enabled）→ 图文 news 信封；
    /// 占位符 {uid}/{openid}/{channel_id}/{channel_type} 替换成功、<c>{ticket}</c> I2-8 起签票替换
    /// （本宿主 Fake 未启用 → 空串，回复结构不变）。</summary>
    [Fact]
    public async Task Template_Configured_ReplacesPlaceholders_ReturnsNewsXml()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            TemplateEvent("wx-template", uid: "u-100", openId: "openid-100"),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal("text/xml", reply!.ContentType);
        // 图文信封
        Assert.Contains("<MsgType><![CDATA[news]]></MsgType>", reply.Content);
        Assert.Contains("<ArticleCount>1</ArticleCount>", reply.Content);
        Assert.Contains("<Articles>", reply.Content);
        // 占位符替换（uid/openid/channel_id/channel_type）
        Assert.Contains("<Title><![CDATA[欢迎 u-100，openid=openid-100 通道=wx-template]]></Title>", reply.Content);
        Assert.Contains("平台=wechat_event", reply.Content);
        // {ticket} I2-8 签票替换：本宿主 Fake ILoginEntryService 未启用 → 空串（回复结构不变，其余文本照常渲染）
        Assert.Contains("保留ticket=", reply.Content);
        Assert.Contains("https://sso.example.com/login?ticket=", reply.Content);
        // PicUrl 模板字段
        Assert.Contains("https://img.example.com/cover.png", reply.Content);
    }

    /// <summary>模板驱动：模板缺失（Channel.Extra 无 news_reply 键）→ null（Oracle P0-2 不回复不拦截）。</summary>
    [Fact]
    public async Task Template_Missing_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(TemplateEvent("wx-no-template"), CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>模板驱动：Enabled=false → null（不回复不拦截——业务 handler 正常流转）。</summary>
    [Fact]
    public async Task Template_EnabledFalse_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(TemplateEvent("wx-disabled"), CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>模板驱动：Enabled 缺失（null ≠ true）→ null（Same 语义——未显式开启即不回复）。</summary>
    [Fact]
    public async Task Template_EnabledNull_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(TemplateEvent("wx-no-enabled"), CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>模板驱动：模板损坏（非法 JSON）→ null（ReadNewsTemplate JsonException 容错——不拦截）。</summary>
    [Fact]
    public async Task Template_CorruptJson_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(TemplateEvent("wx-corrupt"), CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>模板驱动：Channel 反查失败（channel_id 不在 registry）→ Channel=null → 模板读取 null → 不回复。</summary>
    [Fact]
    public async Task Template_ChannelNotInRegistry_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(TemplateEvent("wx-missing"), CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>模板驱动：门控分支（unsubscribe）即使模板配置也不回复（基类协议门控强制 null）。</summary>
    [Fact]
    public async Task Template_GatedEvent_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventDispatch(
                ("msg_type", "event"),
                ("event", "unsubscribe"),
                ("from_user_name", "openid-100"),
                ("uid", "u-100"),
                ("channel_id", "wx-template"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "unsubscribe")))),
            CancellationToken.None);

        Assert.Null(reply);                                              // 模板已配置仍被门控吞掉（G2 覆盖模板 handler）
    }

    /// <summary>多图文（V0.4.1）：articles 数组（2 篇）→ News 2 条 + ArticleCount=2 + 占位符逐篇替换。</summary>
    [Fact]
    public async Task Template_MultiArticles_ReturnsNewsXmlWithCount()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            TemplateEvent("wx-multi", uid: "u-multi", openId: "openid-multi"),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal("text/xml", reply!.ContentType);
        Assert.Contains("<MsgType><![CDATA[news]]></MsgType>", reply.Content);
        Assert.Contains("<ArticleCount>2</ArticleCount>", reply.Content);
        // 逐篇占位符替换
        Assert.Contains("<Title><![CDATA[第一篇 u-multi openid-multi]]></Title>", reply.Content);
        Assert.Contains("<Title><![CDATA[第二篇 u-multi openid-multi]]></Title>", reply.Content);
        // PicUrl 逐篇
        Assert.Contains("<PicUrl><![CDATA[https://img.example.com/a.png]]></PicUrl>", reply.Content);
        Assert.Contains("<PicUrl><![CDATA[https://img.example.com/b.png]]></PicUrl>", reply.Content);
    }

    /// <summary>多图文（V0.4.1）：articles 超过 8 篇 → Take(8) 钳制（微信图文被动回复上限）。</summary>
    [Fact]
    public async Task Template_MultiArticles_Over8_ClampedTo8()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            TemplateEvent("wx-multi9", uid: "u-9", openId: "openid-9"),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<ArticleCount>8</ArticleCount>", reply!.Content);
    }

    /// <summary>uid 绑定门（v0.7.0 RequireUidForTicket=true）：编排层未富化 uid（openid 未绑定）→
    /// 不回复（登录卡只发给已绑定用户——对齐主包 LandingTicketHandler 门控语义）。</summary>
    [Fact]
    public async Task RequireUidForTicket_True_UnboundUid_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
            services.Configure<WechatEventReplyOptions>(o => o.RequireUidForTicket = true));
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            TemplateEvent("wx-template", uid: null, openId: "openid-unbound"),
            CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>uid 绑定门（v0.7.0 RequireUidForTicket=true）：已富化 uid（openid 已绑定）→ 正常签票回复
    /// （门控只拦截未绑定，不影响已绑定用户）。</summary>
    [Fact]
    public async Task RequireUidForTicket_True_BoundUid_RepliesNews()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
            services.Configure<WechatEventReplyOptions>(o => o.RequireUidForTicket = true));
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            TemplateEvent("wx-template", uid: "u-bound", openId: "openid-bound"),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<MsgType><![CDATA[news]]></MsgType>", reply!.Content);
    }

    /// <summary>uid 绑定门默认关（v0.7.0 RequireUidForTicket=false 默认）：未绑定 openid → 仍签票回复
    /// （通用模板场景任意用户可签——门控是显式开关，默认不拦截）。</summary>
    [Fact]
    public async Task RequireUidForTicket_False_UnboundUid_RepliesNews()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            TemplateEvent("wx-template", uid: null, openId: "openid-unbound"),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<MsgType><![CDATA[news]]></MsgType>", reply!.Content);
    }

    /// <summary>模板解析链第二级：Options.DefaultNewsReplyTemplate 全局兜底（Channel.Extra 无模板但全局配置）
    /// → 图文回复（全局模板占位符替换——覆盖"模板缺失 → Options 全局默认"链）。</summary>
    [Fact]
    public async Task Template_Missing_GlobalDefaultTemplate_Fallback()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
            services.Configure<WechatEventReplyOptions>(o => o.DefaultNewsReplyTemplate = new NewsReplyTemplateV2(
                Enabled: true, Articles: null, Title: "全局默认 {uid}", Description: "global",
                Link: "https://global.example.com/x", PicUrl: null, TtlSeconds: null)));
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            TemplateEvent("wx-no-template", uid: "u-global", openId: "openid-g"),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<MsgType><![CDATA[news]]></MsgType>", reply!.Content);
        Assert.Contains("<Title><![CDATA[全局默认 u-global]]></Title>", reply.Content);
    }

    /// <summary>模板解析链第三级：EnableBuiltInTestTemplate=true 且 ①② 均空 → 内置"链路测试"卡片
    /// （开发期零 seed 冒烟——标题固定、link 含 {ticket} 占位由签票替换）。</summary>
    [Fact]
    public async Task Template_Missing_BuiltInEnTestEnabled_ReturnsNewsXml()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
            services.Configure<WechatEventReplyOptions>(o => o.EnableBuiltInTestTemplate = true));
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            TemplateEvent("wx-no-template", uid: "u-1", openId: "openid-1"),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<MsgType><![CDATA[news]]></MsgType>", reply!.Content);
        // v0.16.0 Q1：内置模板标题带 {type} 占位符（= event ?? msg_type——TemplateEvent 用 event=unknown_event）
        Assert.Contains("<Title><![CDATA[链路测试·unknown_event]]></Title>", reply.Content);
    }

    /// <summary>内置"链路测试"卡片 link 走<b>测试端点</b>而非免登 URL（2026-10-26 复审缺口修复回归锁）：
    /// 内置模板的目的是"点击卡片 → 显示解票信息"——只有测试端点（HandleTestTicket GET 匿名兑票 → 200 JSON）
    /// 满足；免登 URL 走消费方 SPA 落地（Platform.Web 无前端 → 点击 404 死链）。断言 link 含
    /// <c>/test/{channelId}/ticket</c> 路径段（经 RecordingLoginEntryService.BuildTestTicketUrlAsync 产 URL）。</summary>
    [Fact]
    public async Task Template_BuiltIn_UsesTestEndpointUrl()
    {
        var recorded = new RecordingLoginEntryService();
        using var _ = this;
        var (_, user) = BindTestScope(services =>
        {
            services.Configure<WechatEventReplyOptions>(o => o.EnableBuiltInTestTemplate = true);
            services.AddScoped<ILoginEntryService>(_ => recorded);
        });
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            TemplateEvent("wx-no-template", uid: "u-1", openId: "openid-1"),
            CancellationToken.None);

        Assert.NotNull(reply);
        // link = {platformId}/test/{channelId}/ticket-test（BuildTestTicketUrlAsync 固定 URL——测试端点语义）
        Assert.Contains("https://ticket.example.com/wechat/test/wx-no-template/ticket-test", reply!.Content);
        // 免登 URL（/login/ticket?ticket=）不应出现——内置模板不走 ResolveTicketAsync 分支
        Assert.DoesNotContain("/login/ticket?ticket=", reply.Content);
    }

    /// <summary>Q1 类型可区分（v0.16.0）：内置模板标题/描述含 <c>{type}</c> 合并占位符（= event ?? msg_type）——
    /// 文本消息（msg_type=text，无 event）→ 标题"链路测试·text"；菜单点击（msg_type=event + event=CLICK）→
    /// "链路测试·CLICK"——测试反馈可区分触发源。</summary>
    [Fact]
    public async Task Template_BuiltIn_TypePlaceholder_ReflectsTriggerSource()
    {
        using var _ = this;
        var (_, user) = BindTestScope(services =>
            services.Configure<WechatEventReplyOptions>(o => o.EnableBuiltInTestTemplate = true));
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        // 文本消息（msg_type=text，无 event）→ {type} = msg_type = "text"
        var textReply = await handler.HandleAsync(
            WeChatTestHost.CreateEventDispatch(
                ("msg_type", "text"),
                ("from_user_name", "openid-t"),
                ("external_user_id", "openid-t"),
                ("to_user_name", "gh_test"),
                ("uid", "u-t"),
                ("channel_id", "wx-no-template"),
                ("__decrypted_xml", WxXml(("MsgType", "text"), ("Content", "hi")))),
            CancellationToken.None);
        Assert.NotNull(textReply);
        Assert.Contains("<Title><![CDATA[链路测试·text]]></Title>", textReply!.Content);
        Assert.Contains("点击验证完整回复链路（text）", textReply.Content);

        // 菜单点击（msg_type=event + event=CLICK）→ {type} = event = "CLICK"
        var clickReply = await handler.HandleAsync(
            WeChatTestHost.CreateEventDispatch(
                ("msg_type", "event"),
                ("event", "CLICK"),
                ("from_user_name", "openid-c"),
                ("external_user_id", "openid-c"),
                ("to_user_name", "gh_test"),
                ("uid", "u-c"),
                ("channel_id", "wx-no-template"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "CLICK")))),
            CancellationToken.None);
        Assert.NotNull(clickReply);
        Assert.Contains("<Title><![CDATA[链路测试·CLICK]]></Title>", clickReply!.Content);
        Assert.Contains("点击验证完整回复链路（CLICK）", clickReply.Content);
    }

    /// <summary>Q2 uid 体现（v0.16.0）：编排 uid 命中已富化 ctx.UId → ResolveTestTicketUrlAsync 透传
    /// BuildTestTicketUrlAsync uId 参数 → 签票载荷 uid 入展示页（已绑定用户测试反馈可体现绑定关系）。</summary>
    [Fact]
    public async Task Template_BuiltIn_UidForwardedToTestTicketUrl()
    {
        var recorded = new RecordingLoginEntryService();
        using var _ = this;
        var (_, user) = BindTestScope(services =>
        {
            services.Configure<WechatEventReplyOptions>(o => o.EnableBuiltInTestTemplate = true);
            services.AddScoped<ILoginEntryService>(_ => recorded);
        });
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventDispatch(
                ("msg_type", "event"),
                ("event", "unknown_event"),
                ("from_user_name", "openid-u"),
                ("external_user_id", "openid-u"),
                ("to_user_name", "gh_test"),
                ("uid", "u-bound-100"),
                ("channel_id", "wx-no-template"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "unknown_event")))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Equal("u-bound-100", recorded.UId);          // 已绑定 uid 透传签票
        Assert.Equal("openid-u", recorded.ExternalUserId);  // 签票身份仍为 openid
    }

    /// <summary>模板解析链第三级 fail-safe：EnableBuiltInTestTemplate 默认 false（未开启）→ 模板缺失仍 null
    /// （不自动回退内置卡片——生产防护：绝不发"链路测试"给真实用户）。</summary>
    [Fact]
    public async Task Template_Missing_BuiltInDisabled_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            TemplateEvent("wx-no-template"), CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>BaseUrl 透传：ctx.BaseUrl（装配层请求 Host 注入）→ ILoginEntryService.BuildTicketUrlAsync
    /// 第三参（requestBaseUrl——免登链接三级基址解析的请求级兜底）。</summary>
    [Fact]
    public async Task Template_BaseUrl_ForwardedToLoginEntry()
    {
        var recorded = new RecordingLoginEntryService();
        using var _ = this;
        var (_, user) = BindTestScope(services =>
        {
            services.Configure<WechatEventReplyOptions>(o => o.RequireUidForTicket = true);
            services.AddScoped<ILoginEntryService>(_ => recorded);
        });
        var handler = user.Use<WechatTemplateEventReplyHandler>();

        var reply = await handler.HandleAsync(
            WeChatTestHost.CreateEventDispatch(
                ("msg_type", "event"),
                ("event", "unknown_event"),
                ("from_user_name", "openid-b"),
                ("external_user_id", "openid-b"),
                ("to_user_name", "gh_test"),
                ("uid", "u-b"),
                ("channel_id", "wx-template"),
                ("base_url", "https://wx.loongba.cn"),
                ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "unknown_event")))),
            CancellationToken.None);

        Assert.NotNull(reply);                        // uid 绑定门开 + 已富化 → 进签票出图文
        Assert.Equal("https://wx.loongba.cn", recorded.RequestBaseUrl);
        Assert.Equal("openid-b", recorded.ExternalUserId);   // 签票身份 = openid（非 uid）
    }

    /// <summary>满足 <see cref="WechatEventReplyHandlerBase.ResolveTicketAsync"/> 签票调用（记录参数供断言；
    /// 返回固定 URL——占位符替换验证透传链）。</summary>
    private sealed class RecordingLoginEntryService : ILoginEntryService
    {
        public string? RequestBaseUrl { get; private set; }
        public string? ExternalUserId { get; private set; }
        public string? UId { get; private set; }   // v0.16.0 Q2：测试端点签票透传 uid 记录（断言已绑定体现）

        public Task<string?> BuildTicketUrlAsync(string channelId, string channelType, string externalUserId, int? ttlSeconds = null, string? requestBaseUrl = null, CancellationToken ct = default)
        {
            RequestBaseUrl = requestBaseUrl;
            ExternalUserId = externalUserId;
            return Task.FromResult<string?>($"https://ticket.example.com/t?ch={channelId}");
        }

        public Task<string?> BuildTestTicketUrlAsync(string channelId, string platformId, string channelType, string externalUserId, int? ttlSeconds = null, string? requestBaseUrl = null, string? uId = null, CancellationToken ct = default)
        {
            RequestBaseUrl = requestBaseUrl;
            ExternalUserId = externalUserId;
            UId = uId;
            return Task.FromResult<string?>($"https://ticket.example.com/{platformId}/test/{channelId}/ticket-test");
        }

        public Task<string?> BuildSilentAuthorizeUrlAsync(string channelId, string returnUrl, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    /// <summary>构造模板驱动事件上下文（unknown_event 类型——路由至 OnDefaultAsync 兜底入口，
    /// 含 uid/openid/channel_id——占位符替换数据源 + registry 反查键）。</summary>
    private static ChannelEventDispatch TemplateEvent(string channelId, string? uid = "u-1", string? openId = "openid-1")
        => WeChatTestHost.CreateEventDispatch(
            ("msg_type", "event"),
            ("event", "unknown_event"),
            ("from_user_name", openId),
            ("external_user_id", openId),
            ("to_user_name", "gh_test"),
            ("uid", uid),
            ("channel_id", channelId),
            ("__decrypted_xml", WxXml(("MsgType", "event"), ("Event", "unknown_event"))));

    private static string WxXml(params (string Name, string Value)[] fields)
    {
        var sb = new System.Text.StringBuilder("<xml>");
        foreach (var (name, value) in fields)
            sb.Append($"<{name}><![CDATA[{value}]]></{name}>");
        return sb.Append("</xml>").ToString();
    }

    /// <summary>
    /// Fake 通道注册表（channel_id → ChannelConfig.Extra["news_reply"] 模板 JSON 场景矩阵）。
    /// <para>wx-template：完整模板 enabled=true；wx-no-template：无 news_reply 键（Oracle P0-2）；
    /// wx-disabled：enabled=false；wx-no-enabled：无 enabled 字段；wx-corrupt：非法 JSON。</para>
    /// </summary>
    private sealed class FakeChannelRegistry : IChannelRegistry
    {
        public List<ChannelConfig> Channels { get; set; } = new()
        {
            new()
            {
                ChannelId = "wx-template",
                PlatformType = "wechat",
                IsEnabled = true,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["news_reply"] =
                        """{"title":"欢迎 {uid}，openid={openid} 通道={channel_id}","description":"平台={channel_type} 保留ticket={ticket}","link":"https://sso.example.com/login?ticket={ticket}","pic_url":"https://img.example.com/cover.png","ttl_seconds":300,"enabled":true}""",
                },
            },
            new()
            {
                ChannelId = "wx-no-template",
                PlatformType = "wechat",
                IsEnabled = true,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["another_key"] = "no template here",
                },
            },
            new()
            {
                ChannelId = "wx-disabled",
                PlatformType = "wechat",
                IsEnabled = true,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["news_reply"] =
                        """{"title":"禁用模板","description":"disabled","link":"https://example.com/x","enabled":false}""",
                },
            },
            new()
            {
                ChannelId = "wx-no-enabled",
                PlatformType = "wechat",
                IsEnabled = true,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["news_reply"] =
                        """{"title":"无 Enabled 字段","description":"enabled 缺省 null","link":"https://example.com/x"}""",
                },
            },
            new()
            {
                ChannelId = "wx-corrupt",
                PlatformType = "wechat",
                IsEnabled = true,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["news_reply"] = "{json 损坏",
                },
            },
            new()
            {
                ChannelId = "wx-multi",
                PlatformType = "wechat",
                IsEnabled = true,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["news_reply"] =
                        """{"enabled":true,"articles":[{"title":"第一篇 {uid} {openid}","description":"desc-a","link":"https://example.com/a","pic_url":"https://img.example.com/a.png"},{"title":"第二篇 {uid} {openid}","description":"desc-b","link":"https://example.com/b","pic_url":"https://img.example.com/b.png"}]}""",
                },
            },
            new()
            {
                ChannelId = "wx-multi9",
                PlatformType = "wechat",
                IsEnabled = true,
                Extra = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["news_reply"] =
                        """{"enabled":true,"articles":[{"title":"第1篇","link":"https://example.com/1","pic_url":"https://img.example.com/1.png"},{"title":"第2篇","link":"https://example.com/2","pic_url":"https://img.example.com/2.png"},{"title":"第3篇","link":"https://example.com/3","pic_url":"https://img.example.com/3.png"},{"title":"第4篇","link":"https://example.com/4","pic_url":"https://img.example.com/4.png"},{"title":"第5篇","link":"https://example.com/5","pic_url":"https://img.example.com/5.png"},{"title":"第6篇","link":"https://example.com/6","pic_url":"https://img.example.com/6.png"},{"title":"第7篇","link":"https://example.com/7","pic_url":"https://img.example.com/7.png"},{"title":"第8篇","link":"https://example.com/8","pic_url":"https://img.example.com/8.png"},{"title":"第9篇","link":"https://example.com/9","pic_url":"https://img.example.com/9.png"}]}""",
                },
            },
        };

        public Task<ChannelConfig?> GetAsync(string channelId, CancellationToken ct = default)
            => Task.FromResult(Channels.FirstOrDefault(c => c.ChannelId == channelId));

        public Task<ChannelConfig?> GetByAliasOrIdAsync(string key, CancellationToken ct = default)
            => Task.FromResult(Channels.FirstOrDefault(c => c.ChannelId == key || c.Alias == key));

        public Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ChannelConfig>>(Channels);

        public Task<ChannelConfig?> GetDefaultAsync(CancellationToken ct = default)
            => Task.FromResult(Channels.FirstOrDefault(c => c.IsDefault) ?? Channels.FirstOrDefault(c => c.IsEnabled));
    }
}