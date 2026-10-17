using TKWF.Federation.WeChat.MessageGateway;

namespace TKWF.Federation.WeChat.MessageGateway.Tests;

/// <summary>
/// <see cref="WeChatMessageIdempotencyKeyBuilder"/> 幂等键规范化单测——纯逻辑直测。
/// 覆盖：消息形态（msg:）/ 事件形态（evt:）/ 跨通道隔离 / 前缀类型隔离。
/// </summary>
public class WeChatMessageIdempotencyKeyBuilderTests
{
    [Fact]
    public void BuildKey_MsgIdPresent_MessageForm()
    {
        var key = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-main", "1234567890", "o_user", 1718000000);

        Assert.Equal("msg:ch-main:1234567890", key);
    }

    [Fact]
    public void BuildKey_MsgIdNull_EventForm()
    {
        var key = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-main", null, "o_user", 1718000000);

        Assert.Equal("evt:ch-main:o_user:1718000000", key);
    }

    [Fact]
    public void BuildKey_MsgIdEmpty_EventForm()
    {
        // 事件推送 MsgId 缺失（空串等价 null）→ 事件形态
        var key = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-main", "", "o_user", 1718000000);

        Assert.Equal("evt:ch-main:o_user:1718000000", key);
    }

    [Fact]
    public void BuildKey_SameMsgId_DifferentChannel_Isolated()
    {
        var a = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-a", "12345", "o_user", 0);
        var b = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-b", "12345", "o_user", 0);

        Assert.NotEqual(a, b);
        Assert.Equal("msg:ch-a:12345", a);
        Assert.Equal("msg:ch-b:12345", b);
    }

    [Fact]
    public void BuildKey_SameEvent_DifferentChannel_Isolated()
    {
        var a = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-a", null, "o_user", 1718000000);
        var b = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-b", null, "o_user", 1718000000);

        Assert.NotEqual(a, b);
        Assert.Equal("evt:ch-a:o_user:1718000000", a);
        Assert.Equal("evt:ch-b:o_user:1718000000", b);
    }

    [Fact]
    public void BuildKey_PrefixTypeIsolation_MsgAndEventNeverCollide()
    {
        // 同 channel + 同 msgId 数值：消息形态 msg: 与事件形态 evt: 前缀隔离，永不碰撞
        var msgForm = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-main", "1718000000", "o_user", 1718000000);
        var evtForm = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-main", null, "o_user", 1718000000);

        Assert.NotEqual(msgForm, evtForm);
        Assert.StartsWith("msg:", msgForm);
        Assert.StartsWith("evt:", evtForm);
    }

    [Fact]
    public void BuildKey_EventForm_SameUserSameTime_Duplicates()
    {
        var k1 = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-main", null, "o_user", 1718000000);
        var k2 = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-main", null, "o_user", 1718000000);

        Assert.Equal(k1, k2); // 微信重试同事件 → 同键 → 幂等命中
    }

    [Fact]
    public void BuildKey_EventForm_RetrySameMsg_Stable()
    {
        var k1 = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-main", "888", "o_user", 0);
        var k2 = WeChatMessageIdempotencyKeyBuilder.BuildKey("ch-main", "888", "o_user", 0);

        Assert.Equal(k1, k2); // 微信重试同消息 → 同键 → 幂等命中
    }
}
