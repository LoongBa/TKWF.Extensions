using System;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKWF.Ext.WeChatChannels;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// 通道在用守卫端到端测试（2026-11-27）——<see cref="WeChatOperationsChannelUsageGuard"/> 经
/// <c>TryAddEnumerableConstructible&lt;IChannelUsageGuard, …&gt;()</c> 注册后，<see cref="IChannelConfigService.DeleteChannelAsync"/>
/// 枚举该守卫；通道存在运营配置引用（回复规则等）时拒删（ErrorCode=CHANNEL_IN_USE），清除引用后放行。
/// </summary>
public class ChannelUsageGuardTests : WeChatOperationsTestHost
{
    [Fact]
    public async Task Delete_ChannelWithReplyRule_RejectedUntilRuleRemoved()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var ct = TestContext.Current.CancellationToken;
        const string channelId = "ch-usage-guard";

        var channelService = user.Use<IChannelConfigService>();
        var channelRowId = await channelService.SaveChannelAsync(
            new SaveChannelRequest(null, channelId, "wx-usage-guard", "在用通道", IsEnabled: true), ct);

        var ruleDs = user.Use<ReplyRuleEntityDataService>();
        var rule = await ruleDs.EntityCreateAsync(new ReplyRuleEntity
        {
            ChannelId = channelId,
            SceneType = 2,
            TriggerKey = "在吗",
            ReplyText = "在的",
            IsEnabled = true,
            SortOrder = 1,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, ct);

        var ex = await Assert.ThrowsAsync<DomainException>(() => channelService.DeleteChannelAsync(channelRowId, ct));
        Assert.Equal("CHANNEL_IN_USE", ex.ErrorCode);
        Assert.Contains("回复规则", ex.Message);
        Assert.NotNull(await channelService.GetByChannelIdAsync(channelId, ct));

        await ruleDs.EntityDeleteBatchAsync(new[] { rule.Id }, ct);

        Assert.True(await channelService.DeleteChannelAsync(channelRowId, ct));
        Assert.Null(await channelService.GetByChannelIdAsync(channelId, ct));
    }

    [Fact]
    public async Task Delete_ChannelWithoutConfig_Succeeds()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var ct = TestContext.Current.CancellationToken;

        var channelService = user.Use<IChannelConfigService>();
        var id = await channelService.SaveChannelAsync(
            new SaveChannelRequest(null, "ch-usage-clean", "wx-usage-clean", "无引用通道", IsEnabled: false), ct);

        Assert.True(await channelService.DeleteChannelAsync(id, ct));
        Assert.Null(await channelService.GetByChannelIdAsync("ch-usage-clean", ct));
    }
}
