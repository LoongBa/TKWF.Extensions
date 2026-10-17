namespace TKWF.Federation.WeChat.MessageGateway;

/// <summary>
/// 微信消息类型协议串（<see cref="RouteRuleCriteria.MsgType"/> / <see cref="MessageRouteRuleDescriptor.MsgType"/> 取值，
/// 对齐微信公众平台协议小写串）。
/// </summary>
public static class WeChatMessageTypes
{
    /// <summary>事件消息（subscribe/unsubscribe/click/scan/location 等——无 MsgId，幂等键走事件形态）。</summary>
    public const string Event = "event";

    /// <summary>文本消息。</summary>
    public const string Text = "text";

    /// <summary>图片消息。</summary>
    public const string Image = "image";

    /// <summary>语音消息。</summary>
    public const string Voice = "voice";

    /// <summary>视频消息。</summary>
    public const string Video = "video";

    /// <summary>小视频消息。</summary>
    public const string ShortVideo = "shortvideo";

    /// <summary>地理位置消息。</summary>
    public const string Location = "location";

    /// <summary>链接消息。</summary>
    public const string Link = "link";
}

/// <summary>
/// 文本关键词匹配模式（<see cref="MessageRouteRuleDescriptor.MatchMode"/> 取值——
/// 对齐既有 <c>ReplyRuleEntity.MatchMode</c> 语义并扩 0，方案 §十·一 字段 5）。
/// </summary>
public static class WeChatMessageMatchModes
{
    /// <summary>不按关键词匹配（TriggerKey 恒空）。</summary>
    public const int None = 0;

    /// <summary>完全匹配（TriggerKey == 消息内容）。</summary>
    public const int Exact = 1;

    /// <summary>包含匹配（消息内容含 TriggerKey）。</summary>
    public const int Contains = 2;
}

/// <summary>
/// 消息路由转发策略（<see cref="MessageRouteRuleDescriptor.ForwardMode"/> 取值——方案 §十·一 字段 7）。
/// </summary>
public static class WeChatMessageForwardModes
{
    /// <summary>本地处理不转发。</summary>
    public const int LocalOnly = 1;

    /// <summary>本地处理 + 异步抄送（推荐默认——本地回复与转发两不误）。</summary>
    public const int AsyncCopy = 2;

    /// <summary>同步透传接管本次回复（转发下游 XML 原样作被动回复）。</summary>
    public const int SyncProxy = 3;

    /// <summary>前置处理 + 同步透传（谨慎使用）。</summary>
    public const int PreprocessSync = 4;
}

/// <summary>
/// 下游端点鉴权类型（方案 §十·一 字段 9——常量类，沿用 <c>WeChatReplySceneTypes</c> 静态 int 常量风格）。
/// </summary>
public static class WeChatEndpointAuthTypes
{
    /// <summary>无鉴权。</summary>
    public const int None = 0;

    /// <summary>Bearer Token（Authorization: Bearer xxx）。</summary>
    public const int Bearer = 1;

    /// <summary>自定义 Header（键值对 Header）。</summary>
    public const int Header = 2;
}
