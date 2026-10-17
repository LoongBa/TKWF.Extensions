using System;
using System.Collections.Generic;
using System.Linq;

namespace TKWF.Federation.WeChat.MessageGateway;

/// <summary>
/// 微信消息路由规则判定器（纯逻辑——方案 §十·〇 ① 运行库）。
/// <para>输入规则列表 + 消息特征（<see cref="RouteRuleCriteria"/>），按 <see cref="MessageRouteRuleDescriptor.SortOrder"/>
/// 升序取首个命中规则，输出命中规则描述（<see cref="MessageRouteRuleDescriptor"/>）。</para>
/// <para>判定语义：IsEnabled=false 跳过 / ChannelId 过滤 / MsgType 空=通配 or 逗号分隔多值任一命中（值 trim）/
/// Event 仅 MsgType=event 时参与、空=通配 / MatchMode 关键词（None 忽略 TriggerKey、Exact==content、Contains 包含）。</para>
/// </summary>
public static class WeChatMessageRouteRuleMatcher
{
    /// <summary>
    /// 规则判定：在 <paramref name="rules"/> 中按 <see cref="MessageRouteRuleDescriptor.SortOrder"/> 升序尝试，
    /// 返回首个命中规则描述（无命中返回 <c>null</c>）。
    /// </summary>
    /// <param name="rules">候选规则列表（扩展把路由规则实体投影为 descriptor 传入——运行库不引用扩展实体）。</param>
    /// <param name="criteria">消息特征（ChannelId/MsgType/Event/Content）。</param>
    /// <returns>首个命中规则；无命中返回 <c>null</c>。</returns>
    public static MessageRouteRuleDescriptor? Match(IEnumerable<MessageRouteRuleDescriptor> rules, RouteRuleCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(rules);
        ArgumentNullException.ThrowIfNull(criteria);

        // 规则按 SortOrder 升序尝试，取首个命中（输入顺序无关——排序语义由运行库持有）
        return rules.OrderBy(r => r.SortOrder).FirstOrDefault(r => Matches(r, criteria));
    }

    /// <summary>
    /// 单条规则判定（供组合使用/单测直测）：IsEnabled 跳过 → ChannelId 过滤 → MsgType 通配或多值任一命中 →
    /// Event 仅 event 参与（空=通配）→ MatchMode 关键词。
    /// </summary>
    /// <param name="rule">待判定规则描述。</param>
    /// <param name="criteria">消息特征。</param>
    /// <returns>是否命中。</returns>
    public static bool Matches(MessageRouteRuleDescriptor rule, RouteRuleCriteria criteria)
    {
        ArgumentNullException.ThrowIfNull(rule);
        ArgumentNullException.ThrowIfNull(criteria);

        // ① 启用门控：禁用 → 规则跳过
        if (!rule.IsEnabled)
            return false;

        // ② 公众号隔离：规则读一律按 ChannelId 过滤
        if (!string.Equals(rule.ChannelId, criteria.ChannelId, StringComparison.Ordinal))
            return false;

        // ③ MsgType 匹配：空 = 全类型通配；非空 = 逗号分隔多值任一命中（V0.3.0——消息类型多选："text,image" 命中
        //   text/image；值 trim + OrdinalIgnoreCase 对齐 Event 匹配大小写不敏感语义；criteria.MsgType 空/纯分隔符恒不命中）
        if (!string.IsNullOrEmpty(rule.MsgType))
        {
            var types = rule.MsgType.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (types.Length == 0
                || !types.Any(t => string.Equals(t, criteria.MsgType, StringComparison.OrdinalIgnoreCase)))
                return false;
        }

        // ④ Event 匹配：仅 MsgType=event 时参与；规则 Event 空 = 全事件通配
        if (!string.IsNullOrEmpty(rule.Event))
        {
            // 非 event 消息不参与事件匹配（事件规则只对 event 消息有意义）
            if (!string.Equals(criteria.MsgType, WeChatMessageTypes.Event, StringComparison.OrdinalIgnoreCase))
                return false;
            if (!string.Equals(rule.Event, criteria.Event, StringComparison.OrdinalIgnoreCase))
                return false;
        }

        // ⑤ 关键词匹配（MatchMode）
        return MatchModeMatches(rule, criteria);
    }

    /// <summary>MatchMode 关键词判定（None 恒命中 / Exact 相等 / Contains 包含；未知模式 fail-safe 不命中）。</summary>
    private static bool MatchModeMatches(MessageRouteRuleDescriptor rule, RouteRuleCriteria criteria)
    {
        switch (rule.MatchMode)
        {
            case WeChatMessageMatchModes.None:
                // 不按关键词——TriggerKey 恒空，恒命中
                return true;

            case WeChatMessageMatchModes.Exact:
                // 完全匹配：TriggerKey == 内容（TriggerKey 空视为畸形规则，不命中）
                return !string.IsNullOrEmpty(rule.TriggerKey)
                    && string.Equals(rule.TriggerKey, criteria.Content, StringComparison.Ordinal);

            case WeChatMessageMatchModes.Contains:
                // 包含匹配：内容含 TriggerKey（空内容/空键均不命中，防 Contains("") 恒真）
                return !string.IsNullOrEmpty(rule.TriggerKey)
                    && !string.IsNullOrEmpty(criteria.Content)
                    && criteria.Content.Contains(rule.TriggerKey, StringComparison.Ordinal);

            default:
                // 未知匹配模式：fail-safe 不命中（不静默全量放行）
                return false;
        }
    }
}
