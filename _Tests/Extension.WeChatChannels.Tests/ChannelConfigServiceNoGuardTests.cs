using System.Threading.Tasks;
using TKWF.Ext.WeChatChannels;

namespace TKWF.Ext.WeChatChannels.Tests;

/// <summary>
/// 零守卫（平台独用）放行测试——宿主未注册任何 <see cref="IChannelUsageGuard"/>（仅 WeChatChannels 装配，未接 WeChatOperations），
/// <see cref="IChannelConfigService.DeleteChannelAsync"/> 应正常删除（守卫零实现 = 放行，非 fail-closed）。
/// </summary>
public class ChannelConfigServiceNoGuardTests : WeChatChannelsTestHost
{
    [Fact]
    public async Task Delete_Channel_Succeeds_WithNoGuardRegistered()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        var ct = TestContext.Current.CancellationToken;

        var id = await service.SaveChannelAsync(new SaveChannelRequest(null, "ch-no-guard", "wx-no-guard", "无守卫通道", IsEnabled: false), ct);

        Assert.True(await service.DeleteChannelAsync(id, ct));
        Assert.Null(await service.GetByChannelIdAsync("ch-no-guard", ct));
    }
}
