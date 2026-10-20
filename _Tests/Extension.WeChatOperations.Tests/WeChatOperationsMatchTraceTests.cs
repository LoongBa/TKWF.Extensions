using System;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;
using TKWF.Federation.WeChat;

using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="WeChatOperationsReplyHandler"/> 匹配过程逐行推演测试（P2-2——DMP-Lite 5.6 §4 日志详情"匹配过程"灰框）：
/// 断言每次自动回复匹配尝试落 <see cref="InteractionLogEntity.MatchTraceJson"/>（JSON 数组——规则 id/规则名/场景类型/
/// 匹配模式/触发键/命中与否/简短原因），消费方前端可逐行渲染"为什么命中/未命中"。
/// <para>覆盖三条 text 分支：① 命中包含匹配规则（Keyword Contains）→ ② 落入 Default 兜底规则 → ③ 无规则落到
/// Options.DefaultReplyText（全链 miss）。仅旁路采集——回复决策/载荷不变（各用例同时断言回复内容与既有行为一致）。</para>
/// <para>生产路径宿主（<see cref="WeChatOperationsTestHost"/> + <c>BindScope()</c>）——同
/// <see cref="WeChatOperationsReplyHandlerTests"/> 形态：handler 经 <c>user.Use&lt;WeChatOperationsReplyHandler&gt;()</c>
/// NoAop 直建，内部 DataService 经基类 <c>User.Use&lt;具体类&gt;()</c> 懒加载——真实生产路径。</para>
/// </summary>
public class WeChatOperationsMatchTraceTests : WeChatOperationsTestHost
{
    private const string ChA = "ch-a";
    private const string OpenId = "openid-match-trace-1";

    /// <summary>text 命中包含匹配规则（MatchMode=2——内容含关键词）→ MatchTraceJson 存在且结构正确：
    /// ① 完全匹配 miss → ② 包含匹配候选规则 hit（RuleId 命中/Reason=content contains key）。</summary>
    [Fact]
    public async Task Text_ContainsHit_MatchTraceJson_PresentAndCorrect()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        var ruleId = await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Keyword, triggerKey: "促销", matchMode: 2, replyText: "本周促销开启！");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "本周促销活动"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        // 回复行为不变（对齐 Text_ExactMismatch_FallsToContains）
        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[本周促销开启！]]></Content>", reply!.Content);

        var log = await SingleLogAsync(provider);
        Assert.NotNull(log);
        Assert.NotNull(log!.MatchTraceJson);
        var steps = ParseTrace(log.MatchTraceJson!);
        Assert.NotEmpty(steps);

        // ① 完全匹配 miss（无完全匹配规则）
        var exactMiss = steps.First(s => s.MatchMode == 1);
        Assert.Equal(WeChatReplySceneTypes.Keyword, exactMiss.SceneType);
        Assert.False(exactMiss.Hit);
        Assert.Null(exactMiss.RuleId);
        Assert.Contains("完全匹配", exactMiss.Reason);

        // ② 包含匹配候选规则 hit（RuleId=种子规则，TriggerKey=促销）
        var containsHit = steps.Last();
        Assert.Equal(WeChatReplySceneTypes.Keyword, containsHit.SceneType);
        Assert.Equal(2, containsHit.MatchMode);
        Assert.Equal(ruleId, containsHit.RuleId);
        Assert.Equal("促销", containsHit.TriggerKey);
        Assert.True(containsHit.Hit);
        Assert.Equal("content contains key", containsHit.Reason);
    }

    /// <summary>text 无关键词命中 → 落入 Default 兜底规则（SceneType=11）→ MatchTraceJson 存在且末步为
    /// Default 规则 hit（不回落到 Options 文案——行为不变对齐 Text_NoKeyword_DefaultRule_Fallback）。</summary>
    [Fact]
    public async Task Text_FallsToDefaultRule_MatchTraceJson_Present()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Keyword, triggerKey: "专属词", matchMode: 1, replyText: "专属回复");
        var defRuleId = await SeedRuleAsync(user, ChA, WeChatReplySceneTypes.Default, replyText: "默认兜底文案");

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "随便聊聊"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        // 回复行为不变（对齐 Text_NoKeyword_DefaultRule_Fallback）
        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[默认兜底文案]]></Content>", reply!.Content);

        var log = await SingleLogAsync(provider);
        Assert.NotNull(log);
        Assert.NotNull(log!.MatchTraceJson);
        var steps = ParseTrace(log.MatchTraceJson!);

        // ③ Default 兜底规则 hit（末步——不再走 template/Options）
        var defHit = steps.Last();
        Assert.Equal(WeChatReplySceneTypes.Default, defHit.SceneType);
        Assert.Equal(defRuleId, defHit.RuleId);
        Assert.Null(defHit.MatchMode);
        Assert.True(defHit.Hit);
        Assert.Equal("Default 规则命中", defHit.Reason);
    }

    /// <summary>text 无任何规则 → 落到 Options.DefaultReplyText（全链 miss）→ MatchTraceJson 存在且
    /// 四步全部 miss（完全匹配/包含匹配/Default/template），末步 reason 含 DefaultReplyText 兜底——
    /// 回复行为不变（对齐 ChannelFilter Options 兜底语义）。</summary>
    [Fact]
    public async Task Text_SinksToDefaultReplyText_MatchTraceJson_FullChainMiss()
    {
        using var _ = this;
        ConfigureOperationsOptions = o => o.DefaultReplyText = "无规则兜底文案-测试";
        var (provider, user) = BindScope();

        var reply = await handler(user).HandleAsync(Ctx(("msg_type", "text"), ("content", "随便聊聊"),
            ("from_user_name", OpenId), ("channel_id", ChA)), CancellationToken.None);

        // 回复行为不变（未命中 → Options.DefaultReplyText 兜底文案）
        Assert.NotNull(reply);
        Assert.Contains("<Content><![CDATA[无规则兜底文案-测试]]></Content>", reply!.Content);

        var log = await SingleLogAsync(provider);
        Assert.NotNull(log);
        Assert.NotNull(log!.MatchTraceJson);
        var steps = ParseTrace(log.MatchTraceJson!);
        Assert.Equal(4, steps.Count);   // ① Exact miss → ② Contains miss → ③ Default miss → ④ template miss → Options 兜底
        Assert.All(steps, s => Assert.False(s.Hit));

        // ④ 末步 = template 兜底 miss → Options.DefaultReplyText
        var sink = steps.Last();
        Assert.Contains("DefaultReplyText", sink.Reason);
        Assert.Equal(WeChatReplySceneTypes.Keyword, sink.SceneType);
    }

    // ═══════════════════ 辅助 ═══════════════════

    private WeChatOperationsReplyHandler handler(DomainUser<TestUserInfo> user)
        => user.Use<WeChatOperationsReplyHandler>();

    /// <summary>构造事件上下文参数包。</summary>
    private static TKWF.Ext.Federation.SsoChannelEventContext Ctx(params (string Key, string? Value)[] parameters)
        => WeChatOperationsTestSupport.CreateEventContext(parameters);

    /// <summary>取该 channel 最近的交互日志（DB 直查——单条回复场景）。</summary>
    private static async Task<InteractionLogEntity?> SingleLogAsync(ServiceProvider provider)
    {
        var fsql = provider.GetRequiredService<IFreeSql>();
        return await fsql.Select<InteractionLogEntity>()
            .Where(l => l.ChannelId == ChA)
            .OrderByDescending(l => l.Id)
            .FirstAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>解析 MatchTraceJson（PascalCase——JsonSerializer 默认）为结构化追踪步清单。</summary>
    private static List<(long? RuleId, int? SceneType, int? MatchMode, string? TriggerKey, bool Hit, string Reason)> ParseTrace(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var steps = new List<(long? RuleId, int? SceneType, int? MatchMode, string? TriggerKey, bool Hit, string Reason)>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            steps.Add((
                RuleId: e.TryGetProperty("RuleId", out var rid) && rid.ValueKind == JsonValueKind.Number ? rid.GetInt64() : null,
                SceneType: e.TryGetProperty("SceneType", out var st) && st.ValueKind == JsonValueKind.Number ? st.GetInt32() : null,
                MatchMode: e.TryGetProperty("MatchMode", out var mm) && mm.ValueKind == JsonValueKind.Number ? mm.GetInt32() : null,
                TriggerKey: e.TryGetProperty("TriggerKey", out var tk) && tk.ValueKind == JsonValueKind.String ? tk.GetString() : null,
                Hit: e.TryGetProperty("Hit", out var h) && h.ValueKind == JsonValueKind.True,
                Reason: e.TryGetProperty("Reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString()! : string.Empty));
        }
        return steps;
    }

    /// <summary>种子规则（ReplyRuleEntityDataService 生产路径直写——对齐 WeChatOperationsReplyHandlerTests）。</summary>
    private static async Task<long> SeedRuleAsync(DomainUser<TestUserInfo> user, string channelId, int sceneType,
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
}