namespace TKWF.Federation.WeChat.MessageGateway;

/// <summary>
/// 消息特征（路由规则判定输入——扩展把微信回调消息投影为特征，运行库不引用扩展实体）。
/// </summary>
/// <param name="ChannelId">公众号隔离 id（规则读一律按 ChannelId 过滤）。</param>
/// <param name="MsgType">消息类型协议串（text/image/.../event）；可为空。</param>
/// <param name="Event">事件类型（subscribe/unsubscribe/click/scan/...）；仅 MsgType=event 时参与匹配，可为空。</param>
/// <param name="Content">消息内容（文本关键词匹配输入；非文本消息可为空）。</param>
public sealed record RouteRuleCriteria(string ChannelId, string? MsgType, string? Event, string? Content);

/// <summary>
/// 路由规则描述（运行库输入——扩展把 <c>MessageRouteRuleEntity</c> 投影为 descriptor 传入，运行库不引用扩展实体）。
/// </summary>
/// <param name="Id">规则主键。</param>
/// <param name="ChannelId">公众号隔离 id。</param>
/// <param name="MsgType">消息类型（空 = 全类型通配）。</param>
/// <param name="Event">事件类型（仅 MsgType=event 时参与匹配，空 = 全事件通配；非 event 规则恒空）。</param>
/// <param name="MatchMode">文本关键词匹配模式（0=None 不按关键词 / 1=Exact 完全匹配 / 2=Contains 包含匹配）。</param>
/// <param name="TriggerKey">关键词文本（仅 MatchMode≠None 时非空）。</param>
/// <param name="ForwardMode">转发策略（1=LocalOnly / 2=AsyncCopy / 3=SyncProxy / 4=PreprocessSync）。</param>
/// <param name="EndpointId">下游端点引用（LocalOnly 恒空；转发类策略必填）。</param>
/// <param name="SortOrder">优先级（同 ChannelId+MsgType+Event 多规则按 SortOrder 升序取首个命中）。</param>
/// <param name="IsEnabled">启用（默认 true；禁用 → 规则跳过）。</param>
public sealed record MessageRouteRuleDescriptor(
    long Id,
    string ChannelId,
    string? MsgType,
    string? Event,
    int MatchMode,
    string? TriggerKey,
    int ForwardMode,
    long? EndpointId,
    int SortOrder,
    bool IsEnabled = true);
