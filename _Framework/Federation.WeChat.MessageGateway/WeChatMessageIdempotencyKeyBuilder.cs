namespace TKWF.Federation.WeChat.MessageGateway;

/// <summary>
/// 微信消息幂等键规范化器（纯逻辑——方案 §十·四 ① 幂等键设计）。
/// <para>微信两种推送形态键来源不同：消息（text/image/...）带 <c>MsgId</c>；事件（subscribe/click/...）无 MsgId，
/// 仅 <c>FromUserName + CreateTime</c>。键 = <c>前缀类型隔离 + ChannelId 入键</c>：
/// <c>msg:{channelId}:{msgId}</c>（消息形态）或 <c>evt:{channelId}:{fromUserName}:{createTime}</c>（事件形态）。</para>
/// <para>纯字符串组合、不可变纯函数——进程内无状态，可任意并发调用。</para>
/// </summary>
public static class WeChatMessageIdempotencyKeyBuilder
{
    /// <summary>
    /// 构建规范化幂等键：MsgId 非空 → 消息形态 <c>msg:{channelId}:{msgId}</c>；
    /// MsgId 空 → 事件形态 <c>evt:{channelId}:{fromUserName}:{createTime}</c>。
    /// </summary>
    /// <param name="channelId">公众号隔离 id（入键——跨通道隔离）。</param>
    /// <param name="msgId">微信消息 MsgId（事件推送为空/缺失）。</param>
    /// <param name="fromUserName">发送者 OpenId（事件形态键来源）。</param>
    /// <param name="createTime">微信原始时间戳（事件形态键来源——原样留存，不做时区转换）。</param>
    /// <returns>规范化幂等键。</returns>
    public static string BuildKey(string channelId, string? msgId, string fromUserName, long createTime)
    {
        // 前缀类型隔离：msg:（消息形态）与 evt:（事件形态）永不互相碰撞；
        // ChannelId 入键：跨通道同 MsgId/同 FromUserName 各自独立去重。
        return string.IsNullOrEmpty(msgId)
            ? $"evt:{channelId}:{fromUserName}:{createTime}"
            : $"msg:{channelId}:{msgId}";
    }
}
