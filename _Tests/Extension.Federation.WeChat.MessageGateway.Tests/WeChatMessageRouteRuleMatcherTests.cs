using TKWF.Federation.WeChat.MessageGateway;

namespace TKWF.Federation.WeChat.MessageGateway.Tests;

/// <summary>
/// <see cref="WeChatMessageRouteRuleMatcher"/> 路由规则判定单测——纯逻辑直测（零 DI 零 DB）。
/// 覆盖：MatchMode 三态（None/Exact/Contains）/ MsgType 通配与相等 / Event 仅 event 参与 / IsEnabled 跳过 /
/// SortOrder 升序首个命中 / ChannelId 隔离 / 未知模式 fail-safe。
/// </summary>
public class WeChatMessageRouteRuleMatcherTests
{
    private const long Ep1 = 101;

    private static MessageRouteRuleDescriptor Rule(
        string channelId = "ch-main",
        string? msgType = null,
        string? @event = null,
        int matchMode = WeChatMessageMatchModes.None,
        string? triggerKey = null,
        int forwardMode = WeChatMessageForwardModes.AsyncCopy,
        long? endpointId = Ep1,
        int sortOrder = 0,
        bool isEnabled = true)
        => new(Id: 1, channelId, msgType, @event, matchMode, triggerKey, forwardMode, endpointId, sortOrder, isEnabled);

    private static RouteRuleCriteria Criteria(string channelId = "ch-main", string? msgType = null, string? @event = null, string? content = null)
        => new(channelId, msgType, @event, content);

    // ---------- MatchMode ----------

    [Fact]
    public void Match_NoneMode_MatchesRegardlessOfContent()
    {
        var rule = Rule(matchMode: WeChatMessageMatchModes.None, triggerKey: null);
        var criteria = Criteria(msgType: WeChatMessageTypes.Text, content: "任意内容");

        Assert.True(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_ExactMode_MatchesEqualContent()
    {
        var rule = Rule(matchMode: WeChatMessageMatchModes.Exact, triggerKey: "优惠券");
        var criteria = Criteria(msgType: WeChatMessageTypes.Text, content: "优惠券");

        Assert.True(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_ExactMode_MismatchContent_NoMatch()
    {
        var rule = Rule(matchMode: WeChatMessageMatchModes.Exact, triggerKey: "优惠券");
        var criteria = Criteria(msgType: WeChatMessageTypes.Text, content: "优惠券领取");

        Assert.False(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_ContainsMode_SubstringContent_Matches()
    {
        var rule = Rule(matchMode: WeChatMessageMatchModes.Contains, triggerKey: "优惠");
        var criteria = Criteria(msgType: WeChatMessageTypes.Text, content: "今天有什么优惠活动吗");

        Assert.True(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_ContainsMode_NoSubstring_NoMatch()
    {
        var rule = Rule(matchMode: WeChatMessageMatchModes.Contains, triggerKey: "优惠");
        var criteria = Criteria(msgType: WeChatMessageTypes.Text, content: "今天天气不错");

        Assert.False(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_NonNoneMode_EmptyTriggerKey_NoMatch()
    {
        // 畸形规则防御：非 None 模式但 TriggerKey 空 → 不命中（防 Contains("") 恒真放行）
        var exact = Rule(matchMode: WeChatMessageMatchModes.Exact, triggerKey: null);
        var contains = Rule(matchMode: WeChatMessageMatchModes.Contains, triggerKey: "");
        var criteria = Criteria(msgType: WeChatMessageTypes.Text, content: "x");

        Assert.False(WeChatMessageRouteRuleMatcher.Matches(exact, criteria));
        Assert.False(WeChatMessageRouteRuleMatcher.Matches(contains, criteria));
    }

    [Fact]
    public void Match_UnknownMatchMode_NoMatch()
    {
        var rule = Rule(matchMode: 99, triggerKey: null);
        var criteria = Criteria(msgType: WeChatMessageTypes.Text);

        Assert.False(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    // ---------- MsgType 通配与相等 ----------

    [Fact]
    public void Match_MsgTypeEmpty_Wildcard_AllTypes()
    {
        var rule = Rule(msgType: null);
        var criteria = Criteria(msgType: WeChatMessageTypes.Image);

        Assert.True(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_MsgTypeEqual_MatchesSameType()
    {
        var rule = Rule(msgType: WeChatMessageTypes.Text);
        var criteria = Criteria(msgType: WeChatMessageTypes.Text, content: "x");

        Assert.True(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_MsgTypeDifferent_NoMatch()
    {
        var rule = Rule(msgType: WeChatMessageTypes.Text);
        var criteria = Criteria(msgType: WeChatMessageTypes.Image);

        Assert.False(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    // ---------- MsgType 逗号分隔多值（V0.3.0——消息类型多选） ----------

    [Fact]
    public void Match_MsgTypeMultiValue_AnyValueMatches()
    {
        // "text,image" 命中任一（text 与 image 均命中——消息类型多选）
        var rule = Rule(msgType: "text,image");
        var criteriaText = Criteria(msgType: WeChatMessageTypes.Text, content: "x");
        var criteriaImage = Criteria(msgType: WeChatMessageTypes.Image);

        Assert.True(WeChatMessageRouteRuleMatcher.Matches(rule, criteriaText));
        Assert.True(WeChatMessageRouteRuleMatcher.Matches(rule, criteriaImage));
    }

    [Fact]
    public void Match_MsgTypeMultiValue_NonContainedValue_NoMatch()
    {
        var rule = Rule(msgType: "text,image");
        var criteria = Criteria(msgType: WeChatMessageTypes.Video);

        Assert.False(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_MsgTypeMultiValue_ValuesTrimmed_CaseInsensitive()
    {
        // 值 trim + OrdinalIgnoreCase（" text , IMAGE " → [text, IMAGE]——命中 image）
        var rule = Rule(msgType: " text , IMAGE ");
        var criteria = Criteria(msgType: WeChatMessageTypes.Image);

        Assert.True(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_MsgTypeMultiValue_EmptyCriterion_NoMatch()
    {
        // 非空规则（含纯分隔符）对空 criteria.MsgType 恒不命中——防 Split 空数组误判通配
        var rule = Rule(msgType: "text,image");
        var criteria = Criteria(msgType: null);

        Assert.False(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_MsgTypeMultiValue_EventRule_MatchesEvent()
    {
        // 多值含 event → 事件规则正常参与（Event 匹配语义不受多值影响）
        var rule = Rule(msgType: "text,event", @event: "subscribe");
        var criteria = Criteria(msgType: WeChatMessageTypes.Event, @event: "subscribe");

        Assert.True(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    // ---------- Event 仅 event 参与 ----------

    [Fact]
    public void Match_EventRule_EventCriteria_Matches()
    {
        var rule = Rule(msgType: WeChatMessageTypes.Event, @event: "subscribe");
        var criteria = Criteria(msgType: WeChatMessageTypes.Event, @event: "subscribe");

        Assert.True(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_EventRule_DifferentEvent_NoMatch()
    {
        var rule = Rule(msgType: WeChatMessageTypes.Event, @event: "subscribe");
        var criteria = Criteria(msgType: WeChatMessageTypes.Event, @event: "click");

        Assert.False(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_EventRule_NonEventCriteria_NoMatch()
    {
        // Event 仅 MsgType=event 时参与——非 event 消息恒不参与事件匹配
        var rule = Rule(msgType: WeChatMessageTypes.Event, @event: "subscribe");
        var criteria = Criteria(msgType: WeChatMessageTypes.Text, content: "x");

        Assert.False(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_EventEmpty_Wildcard_AllEvents()
    {
        var rule = Rule(msgType: WeChatMessageTypes.Event, @event: null);
        var criteria = Criteria(msgType: WeChatMessageTypes.Event, @event: "scan");

        Assert.True(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    // ---------- IsEnabled / ChannelId / SortOrder ----------

    [Fact]
    public void Match_DisabledRule_Skipped()
    {
        var rule = Rule(isEnabled: false);
        var criteria = Criteria();

        Assert.False(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_DisabledRule_NotReturnedByMatch()
    {
        var rule = Rule(isEnabled: false);
        var criteria = Criteria();

        Assert.Null(WeChatMessageRouteRuleMatcher.Match([rule], criteria));
    }

    [Fact]
    public void Match_ChannelMismatch_NoMatch()
    {
        var rule = Rule(channelId: "ch-a");
        var criteria = Criteria(channelId: "ch-b");

        Assert.False(WeChatMessageRouteRuleMatcher.Matches(rule, criteria));
    }

    [Fact]
    public void Match_MultipleRules_SortOrderAscending_FirstHitWins()
    {
        // 输入乱序 + 两条均命中（同为 None 通配）——SortOrder 升序取首个（0 优先于 10）
        var low = Rule(sortOrder: 0, forwardMode: WeChatMessageForwardModes.LocalOnly, endpointId: null);
        var high = Rule(sortOrder: 10, forwardMode: WeChatMessageForwardModes.AsyncCopy);
        var criteria = Criteria();

        var hit = WeChatMessageRouteRuleMatcher.Match([high, low], criteria);

        Assert.NotNull(hit);
        Assert.Equal(WeChatMessageForwardModes.LocalOnly, hit.ForwardMode);
    }

    [Fact]
    public void Match_FirstMatchingRule_SkipsNonMatchingEarlier()
    {
        // 低 SortOrder 规则不命中（Channel 不符）→ 跳过，取其后首个命中
        var mismatch = Rule(channelId: "ch-other", sortOrder: 0);
        var hit = Rule(sortOrder: 1, forwardMode: WeChatMessageForwardModes.SyncProxy);
        var criteria = Criteria();

        var result = WeChatMessageRouteRuleMatcher.Match([mismatch, hit], criteria);

        Assert.NotNull(result);
        Assert.Equal(1, result.SortOrder);
        Assert.Equal(WeChatMessageForwardModes.SyncProxy, result.ForwardMode);
    }

    [Fact]
    public void Match_NoHit_ReturnsNull()
    {
        var rule = Rule(msgType: WeChatMessageTypes.Event, @event: "subscribe");
        var criteria = Criteria(msgType: WeChatMessageTypes.Text, content: "x");

        Assert.Null(WeChatMessageRouteRuleMatcher.Match([rule], criteria));
    }
}
