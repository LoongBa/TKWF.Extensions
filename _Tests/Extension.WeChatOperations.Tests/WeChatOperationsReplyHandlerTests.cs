using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.TrustCenter;
using TKWF.Federation.WeChat;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="WeChatOperationsReplyHandler"/> 测试——覆盖全消息类型回复配置（设计文档 §3.3.3/§六）：
/// subscribe 欢迎语（规则素材回复 + SubscriptionStat 计数）/ unsubscribe 取关计数（回复被协议门控吞）/
/// text 关键词（完全匹配→包含匹配→Default 兜底→template 兜底→Options 默认文案）/ click 映射（TriggerKey=EventKey）/
/// image/voice/video/shortvideo/location/link 对应 SceneType 规则 / scan 场景规则 / view null /
/// channel 过滤（A channel 规则不影响 B channel）/ template 兜底（Channel.Extra["news_reply"]——handler 内部组合）/
/// 素材渲染（Kind=Text→文本 / Kind=News→图文）/ 交互日志记录（best-effort）。
/// <para>生产路径宿主（<see cref="WeChatOperationsTestHost"/> + <c>BindScope()</c>）——
/// handler 经 <c>user.Use&lt;WeChatOperationsReplyHandler&gt;()</c> NoAop 直建（public sealed 类，
/// ctor 可选 IOptions/ILogger）；内部 DataService 经基类 <c>User.Use&lt;具体类&gt;()</c> 懒加载、
/// 统计经 <c>User.Use&lt;ISubscriptionStatService&gt;()</c> AOP 帧内调用——真实生产路径。</para>
/// </summary>
public class WeChatOperationsReplyHandlerTests : WeChatOperationsTestHost
{
    private const string ChA = "ch-a";
    private const string ChB = "ch-b";
    private const string OpenId = "openid-test-1";

    // ═══════════════════ subscribe（欢迎语 + 统计） ═══════════════════

    /// <summary>subscribe → 欢迎语规则（ReplyText）回复 + SubscriptionStat 计数 +1。</summary>
    [Fact]
    public async Task Subscribe_WithWelcomeRule_RepliesText_AndCountsStat()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Subscribe, replyText: "欢迎关注本公众号！");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "subscribe"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<MsgType><![CDATA[text]]></MsgType>", reply!.Content);
        Assert.Contains("欢迎关注本公众号！", reply.Content);

        // 订阅统计 +1（副作用——即使回复也可计数）
        var stat = await StatAsync(provider, ChA);
        Assert.NotNull(stat);
        Assert.Equal(1, stat!.SubscribeCount);
        Assert.Equal(0, stat.UnsubscribeCount);
        Assert.Equal(1, stat.NetCount);
    }

    /// <summary>subscribe 无规则 → template 兜底（Channel.Extra["news_reply"]——handler 内部组合，不依赖编排器）。</summary>
    [Fact]
    public async Task Subscribe_NoRule_TemplateFallback_RepliesNews()
    {
        using var _ = this;
        ExtraChannels = [ChannelWithTemplate(ChA)];
        var (_, user) = BindScope();

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "subscribe"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<MsgType><![CDATA[news]]></MsgType>", reply!.Content);
        Assert.Contains("<ArticleCount>1</ArticleCount>", reply.Content);
        Assert.Contains("<Title><![CDATA[欢迎关注]]></Title>", reply.Content);
        // 占位符替换（{openid}）
        Assert.Contains($"<Url><![CDATA[https://example.com/welcome?openid={OpenId}]]></Url>", reply.Content);
    }

    /// <summary>subscribe 无规则无 template → null（Options.DefaultReplyText 仅 text 分支兜底——Subscribe 分支
    /// 无规则无 template 不回复，对齐 handler 渲染四层语义）。</summary>
    [Fact]
    public async Task Subscribe_NoRuleNoTemplate_Null()
    {
        using var _ = this;
        var (_, user) = BindScope();

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "subscribe"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.Null(reply);   // Subscribe 分支无 Options 兜底——null 不回复
    }

    /// <summary>subscribe 规则引用素材（Kind=Text）→ 素材文本回复 + 交互日志含 ReplyMaterialId。</summary>
    [Fact]
    public async Task Subscribe_RuleRefsTextMaterial_RepliesMaterialContent_LogsReplyMaterialId()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        var matId = await SeedMaterialAsync(user, kind: 1, name: "欢迎素材", content: "素材欢迎语");
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Subscribe, materialId: matId);

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "subscribe"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[素材欢迎语]]></Content>", reply!.Content);

        // 交互日志记录（触发类型 1=Subscribe + 素材引用）
        var log = await provider.GetRequiredService<IFreeSql>().Select<InteractionLogEntity>()
            .Where(l => l.ChannelId == ChA).FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);
        Assert.Equal(OpenId, log!.OpenId);
        Assert.Equal(1, log.TriggerType);
        Assert.Equal("素材欢迎语", log.ReplyContent);
        Assert.Equal(matId, log.ReplyMaterialId);
    }

    /// <summary>交互日志记录耗时字段（V0.3.0——DMP-Lite 5.6 交互日志"耗时 ms"）：<see cref="InteractionLogEntity.DurationMs"/>
    /// 非空（Stopwatch 自 Handler 入口起、记录时止——文本回复路径断言）。</summary>
    [Fact]
    public async Task Text_Reply_InteractionLog_DurationMsRecorded()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Keyword, triggerKey: "你好", matchMode: 1, replyText: "你好呀！");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "你好"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.NotNull(reply);

        var log = await provider.GetRequiredService<IFreeSql>().Select<InteractionLogEntity>()
            .Where(l => l.ChannelId == ChA).FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);
        Assert.True(log!.DurationMs is >= 0, $"DurationMs 应为非负（null 或负值即字段未记录/语义错误）：{log.DurationMs}");
    }

    // ═══════════════════ unsubscribe（取关计数——回复被协议门控吞） ═══════════════════

    /// <summary>unsubscribe → 取关计数 +1 + HandleAsync null（基类协议门控——回复被吞，副作用可做）。</summary>
    [Fact]
    public async Task Unsubscribe_CountsStat_ReplyNull()
    {
        using var _ = this;
        var (provider, user) = BindScope();

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "unsubscribe"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.Null(reply);   // 协议门控——回复被吞

        var stat = await StatAsync(provider, ChA);
        Assert.NotNull(stat);
        Assert.Equal(0, stat!.SubscribeCount);
        Assert.Equal(1, stat.UnsubscribeCount);
        Assert.Equal(-1, stat.NetCount);
    }

    // ═══════════════════ text 关键词（完全→包含→Default→template→Options） ═══════════════════

    /// <summary>text 完全匹配（MatchMode=1——TriggerKey == 内容）→ 回复。</summary>
    [Fact]
    public async Task Text_ExactKeyword_MatchMode1_Replies()
    {
        using var _ = this;
        var (_, user) = BindScope();
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Keyword, triggerKey: "你好", matchMode: 1, replyText: "你好呀！");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "你好"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[你好呀！]]></Content>", reply!.Content);
    }

    /// <summary>text 完全匹配不中（内容 != 关键词）→ 不走 Exact，落入包含匹配分支。</summary>
    [Fact]
    public async Task Text_ExactMismatch_FallsToContains()
    {
        using var _ = this;
        var (_, user) = BindScope();
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Keyword, triggerKey: "促销", matchMode: 2, replyText: "本周促销开启！");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "本周促销活动"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[本周促销开启！]]></Content>", reply!.Content);
    }

    /// <summary>text 无关键词命中 → Default 兜底规则（SceneType=11）。</summary>
    [Fact]
    public async Task Text_NoKeyword_DefaultRule_Fallback()
    {
        using var _ = this;
        var (_, user) = BindScope();
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Keyword, triggerKey: "专属词", matchMode: 1, replyText: "专属回复");
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Default, replyText: "默认兜底文案");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "随便聊聊"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[默认兜底文案]]></Content>", reply!.Content);
    }

    // ═══════════════════ click 映射（TriggerKey=EventKey） ═══════════════════

    /// <summary>click → 规则 TriggerKey==EventKey → 回复。</summary>
    [Fact]
    public async Task Click_MappedEventKey_Replies()
    {
        using var _ = this;
        var (_, user) = BindScope();
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Click, triggerKey: "V1001_MENU", replyText: "菜单回复内容");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "CLICK"),
            ("from_user_name", OpenId), ("channel_id", ChA),
            ("__decrypted_xml", WeChatOperationsTestSupport.WxXml(("MsgType", "event"), ("Event", "CLICK"), ("EventKey", "V1001_MENU")))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[菜单回复内容]]></Content>", reply!.Content);
    }

    /// <summary>click 未映射（EventKey 无规则）→ null（不回复）。</summary>
    [Fact]
    public async Task Click_Unmapped_Null()
    {
        using var _ = this;
        var (_, user) = BindScope();

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "CLICK"),
            ("from_user_name", OpenId), ("channel_id", ChA),
            ("__decrypted_xml", WeChatOperationsTestSupport.WxXml(("MsgType", "event"), ("Event", "CLICK"), ("EventKey", "NOPE")))),
            CancellationToken.None);

        Assert.Null(reply);
    }

    // ═══════════════════ 消息类（image/voice/video/shortvideo/location/link → 对应 SceneType） ═══════════════════

    /// <summary>六种消息类型 → 对应 SceneType 规则命中回复（参数化）。</summary>
    [Theory]
    [InlineData("image", WeChatReplySceneTypes.Image)]
    [InlineData("voice", WeChatReplySceneTypes.Voice)]
    [InlineData("video", WeChatReplySceneTypes.Video)]
    [InlineData("shortvideo", WeChatReplySceneTypes.ShortVideo)]
    [InlineData("location", WeChatReplySceneTypes.Location)]
    [InlineData("link", WeChatReplySceneTypes.Link)]
    public async Task MessageTypes_SceneRule_Replies(string msgType, int sceneType)
    {
        using var _ = this;
        var (_, user) = BindScope();
        await SeedRuleAsync(user, ChA, sceneType, replyText: $"{msgType}-回复");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", msgType), ("from_user_name", OpenId), ("channel_id", ChA)),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains($"<Content><![CDATA[{msgType}-回复]]></Content>", reply!.Content);
    }

    // ═══════════════════ scan 场景规则 ═══════════════════

    /// <summary>scan → 场景规则（TriggerKey 空——不限场景）命中回复；SceneId 自 EventKey 去前缀。</summary>
    [Fact]
    public async Task Scan_SceneRule_Replies()
    {
        using var _ = this;
        var (_, user) = BindScope();
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Scan, replyText: "扫码回复");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "SCAN"),
            ("from_user_name", OpenId), ("channel_id", ChA),
            ("__decrypted_xml", WeChatOperationsTestSupport.WxXml(("MsgType", "event"), ("Event", "SCAN"), ("EventKey", "qrscene_1001")))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[扫码回复]]></Content>", reply!.Content);
    }

    /// <summary>scan 场景值匹配（规则 TriggerKey=场景值）——带参扫码精准回复。</summary>
    [Fact]
    public async Task Scan_SceneValueMatched_Replies()
    {
        using var _ = this;
        var (_, user) = BindScope();
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Scan, triggerKey: "1001", replyText: "活动 1001 回复");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "SCAN"),
            ("from_user_name", OpenId), ("channel_id", ChA),
            ("__decrypted_xml", WeChatOperationsTestSupport.WxXml(("MsgType", "event"), ("Event", "SCAN"), ("EventKey", "qrscene_1001")))),
            CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[活动 1001 回复]]></Content>", reply!.Content);
    }

    // ═══════════════════ view / view_miniprogram（null 微信直跳） ═══════════════════

    /// <summary>view / view_miniprogram → null（微信直接跳转，无回复）。</summary>
    [Theory]
    [InlineData("view")]
    [InlineData("view_miniprogram")]
    public async Task ViewEvents_Null(string eventType)
    {
        using var _ = this;
        var (_, user) = BindScope();

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", eventType),
            ("from_user_name", OpenId), ("channel_id", ChA),
            ("__decrypted_xml", WeChatOperationsTestSupport.WxXml(("MsgType", "event"), ("Event", eventType), ("EventKey", "url-or-params")))),
            CancellationToken.None);

        Assert.Null(reply);
    }

    // ═══════════════════ channel 过滤 ═══════════════════

    /// <summary>A channel 规则不影响 B channel（B 无规则 → template 无 → Options 默认兜底——而非 A 的规则）。</summary>
    [Fact]
    public async Task ChannelFilter_A_Rule_NotApplied_To_B()
    {
        using var _ = this;
        var (_, user) = BindScope();
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Keyword, triggerKey: "跨通道", matchMode: 1, replyText: "A 通道专属回复");

        // B 通道发同内容——不应命中 A 的规则
        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "跨通道"),
            ("from_user_name", OpenId), ("channel_id", ChB)), CancellationToken.None);

        Assert.NotNull(reply);   // 未命中 A 规则 → Options.DefaultReplyText 兜底（非 A 专属文案）
        Assert.DoesNotContain("A 通道专属回复", reply!.Content);
    }

    // ═══════════════════ 素材渲染（Kind=News→图文信封） ═══════════════════

    /// <summary>规则引用 News 素材 → 图文回复信封（ArticleCount=1 + 标题/链接）。</summary>
    [Fact]
    public async Task RuleRefsNewsMaterial_RepliesNewsEnvelope()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var matId = await SeedMaterialAsync(user, kind: 2, name: "图文素材",
            title: "活动图文", description: "点击查看活动", url: "https://example.com/act", picUrl: "https://img.example.com/act.png");
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Subscribe, materialId: matId);

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "subscribe"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<MsgType><![CDATA[news]]></MsgType>", reply!.Content);
        Assert.Contains("<ArticleCount>1</ArticleCount>", reply.Content);
        Assert.Contains("<Title><![CDATA[活动图文]]></Title>", reply.Content);
        Assert.Contains("<Url><![CDATA[https://example.com/act]]></Url>", reply.Content);
    }

    /// <summary>规则引用禁用素材 → null（禁用素材跳过——不回复）。</summary>
    [Fact]
    public async Task RuleRefsDisabledMaterial_Null()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var matId = await SeedMaterialAsync(user, kind: 1, name: "禁用素材", content: "不应出现", isEnabled: false);
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Subscribe, materialId: matId);

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "subscribe"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.Null(reply);
    }

    /// <summary>规则 ReplyText 优先于素材（存在 ReplyText 时忽略 MaterialId——设计 §3.3.1）。</summary>
    [Fact]
    public async Task Rule_ReplyText_PriorityOverMaterial()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var matId = await SeedMaterialAsync(user, kind: 1, name: "素材", content: "素材内容不应出现");
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Subscribe, materialId: matId, replyText: "直接文本优先");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "event"), ("event", "subscribe"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[直接文本优先]]></Content>", reply!.Content);
        Assert.DoesNotContain("素材内容不应出现", reply!.Content);
    }

    // ═══════════════════ 辅助 ═══════════════════

    private WeChatOperationsReplyHandler handler(DomainUser<TestUserInfo> user)
        => user.Use<WeChatOperationsReplyHandler>();

    /// <summary>构造事件上下文参数包。</summary>
    private static TKWF.Ext.TrustCenter.SsoChannelEventContext Ctx(params (string Key, string? Value)[] parameters)
        => WeChatOperationsTestSupport.CreateEventContext(parameters);

    /// <summary>含 news_reply 模板的通道配置（ChannelConfig——template 兜底数据源 Channel.Extra；
    /// 经测试宿主 ExtraChannels 注入 IChannelSource 集合，StaticChannelRegistry 聚合选区）。</summary>
    private static TKWF.Ext.Federation.ChannelConfig ChannelWithTemplate(string channelId)
        => new()
        {
            ChannelId = channelId,
            PlatformType = "wechat",
            AppId = $"wx-{channelId}",
            AppSecret = "secret",
            IsEnabled = true,
            Extra = new Dictionary<string, string?>
            {
                ["news_reply"] = "{\"enabled\":true,\"articles\":[{\"title\":\"欢迎关注\",\"description\":\"感谢关注\",\"link\":\"https://example.com/welcome?openid={openid}\",\"pic_url\":\"https://img.example.com/w.png\"}]}",
            },
        };

    /// <summary>种子规则（ReplyRuleEntityDataService 生产路径直写）。</summary>
    private static async Task<long> SeedRuleAsync(DomainUser<TestUserInfo> user, string channelId, int sceneType,
        string? triggerKey = null, int matchMode = 0, long? materialId = null, string? replyText = null, bool isEnabled = true)
    {
        var ds = user.Use<ReplyRuleEntityDataService>();
        var rule = new ReplyRuleEntity
        {
            ChannelId = channelId,
            SceneType = sceneType,
            TriggerKey = triggerKey,
            MatchMode = matchMode,
            MaterialId = materialId,
            ReplyText = replyText,
            IsEnabled = isEnabled,
            SortOrder = 1,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        };
        var created = await ds.EntityCreateAsync(rule, TestContext.Current.CancellationToken);
        return created.Id;
    }

    /// <summary>种子素材（MaterialEntityDataService 生产路径直写）。</summary>
    private static async Task<long> SeedMaterialAsync(DomainUser<TestUserInfo> user, int kind, string name,
        string? content = null, string? title = null, string? description = null, string? url = null,
        string? picUrl = null, bool isEnabled = true)
    {
        var ds = user.Use<MaterialEntityDataService>();
        var created = await ds.EntityCreateAsync(new MaterialEntity
        {
            Name = name,
            Kind = kind,
            Content = content,
            Title = title,
            Description = description,
            Url = url,
            PicUrl = picUrl,
            IsEnabled = isEnabled,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, TestContext.Current.CancellationToken);
        return created.Id;
    }

    /// <summary>查询当日订阅统计（DB 直查）。</summary>
    private static async Task<SubscriptionStatEntity?> StatAsync(ServiceProvider provider, string channelId)
    {
        var fsql = provider.GetRequiredService<IFreeSql>();
        return await fsql.Select<SubscriptionStatEntity>()
            .Where(s => s.ChannelId == channelId)
            .FirstAsync(TestContext.Current.CancellationToken);
    }
}
