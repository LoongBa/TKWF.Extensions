using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Exceptions;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>有界文本列（ReplyRuleEntity.ReplyText [MaxLength(2000)]）服务层守卫测试——超长输入须在写库前被友好拒绝。</summary>
public sealed class ReplyRuleServiceLengthGuardTests : WeChatOperationsTestHost
{
    [Fact]
    public async Task SaveReplyRule_OverlongReplyText_ThrowsFriendlyError()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IReplyRuleService>();

        var rule = new ReplyRuleEntity
        {
            ChannelId = "ch-guard",
            SceneType = 11,
            ReplyText = new string('x', 2001),
        };

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.SaveReplyRuleAsync(rule, TestContext.Current.CancellationToken));

        Assert.Equal("WECHAT_REPLY_TEXT_TOO_LONG", ex.ErrorCode);
        Assert.Contains("2000", ex.Message);
        Assert.Empty(await service.ListReplyRulesAsync("ch-guard", ct: TestContext.Current.CancellationToken));
    }
}
