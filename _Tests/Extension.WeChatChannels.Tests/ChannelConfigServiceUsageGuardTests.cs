using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.Exceptions;
using TKWF.Ext.WeChatChannels;

namespace TKWF.Ext.WeChatChannels.Tests;

/// <summary>
/// 通道在用守卫（2026-11-27）门禁测试——<see cref="IChannelConfigService.DeleteChannelAsync"/> 枚举
/// <see cref="IChannelUsageGuard"/>，任一报告 InUse 即拒删（ErrorCode=CHANNEL_IN_USE）；零实现/未在用则放行。
/// <para>平台独用（无运营数据属主）时 WeChatChannels 不注册守卫——对应用例由无 stub 的宿主形态覆盖；
/// 本文件以 stub 守卫验证门禁接线（运营侧真实实现见 Extension.WeChatOperations.Tests）。</para>
/// </summary>
public class ChannelConfigServiceUsageGuardTests : WeChatChannelsTestHost
{
    private const string InUseChannelId = "ch-guard-inuse";

    /// <summary>stub 守卫：仅对 <see cref="InUseChannelId"/> 报告在用（其余放行）。</summary>
    private sealed class StubUsageGuard : IChannelUsageGuard
    {
        public Task<ChannelUsageCheck> CheckAsync(string channelId, CancellationToken ct = default)
            => Task.FromResult(channelId == InUseChannelId
                ? new ChannelUsageCheck(true, "测试引用")
                : new ChannelUsageCheck(false));
    }

    protected override void ConfigureHost(IServiceCollection services)
    {
        base.ConfigureHost(services);
        services.AddScoped<IChannelUsageGuard>(_ => new StubUsageGuard());
    }

    [Fact]
    public async Task Delete_InUseChannel_ThrowsAndKeepsRow()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        var ct = TestContext.Current.CancellationToken;

        var id = await service.SaveChannelAsync(new SaveChannelRequest(null, InUseChannelId, "wx-guard-inuse", "在用通道", IsEnabled: true), ct);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.DeleteChannelAsync(id, ct));
        Assert.Equal("CHANNEL_IN_USE", ex.ErrorCode);
        Assert.Contains("测试引用", ex.Message);
        Assert.NotNull(await service.GetByChannelIdAsync(InUseChannelId, ct));
    }

    [Fact]
    public async Task Delete_NotInUseChannel_Succeeds()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IChannelConfigService>();
        var ct = TestContext.Current.CancellationToken;

        var id = await service.SaveChannelAsync(new SaveChannelRequest(null, "ch-guard-clean", "wx-guard-clean", "干净通道", IsEnabled: false), ct);

        Assert.True(await service.DeleteChannelAsync(id, ct));
        Assert.Null(await service.GetByChannelIdAsync("ch-guard-clean", ct));
    }
}
