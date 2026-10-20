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
/// <c>{ticket}</c> <b>保留原样</b>——方案 §3.3 无签票耦合）；② <b>无模板 → null</b>（Oracle P0-2）；
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
    /// 占位符 {uid}/{openid}/{channel_id}/{channel_type} 替换成功、<c>{ticket}</c> 保留原样（无签票耦合）。</summary>
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
        // {ticket} 保留原样（方案 §3.3——签票前不可用，业务 handler 自行替换）
        Assert.Contains("保留ticket={ticket}", reply.Content);
        Assert.Contains("https://sso.example.com/login?ticket={ticket}", reply.Content);
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
            WeChatTestHost.CreateEventContext(
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

    /// <summary>构造模板驱动事件上下文（unknown_event 类型——路由至 OnDefaultAsync 兜底入口，
    /// 含 uid/openid/channel_id——占位符替换数据源 + registry 反查键）。</summary>
    private static SsoChannelEventContext TemplateEvent(string channelId, string? uid = "u-1", string? openId = "openid-1")
        => WeChatTestHost.CreateEventContext(
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