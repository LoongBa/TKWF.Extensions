using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.WeChatOperations.DTOs;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="IMenuService"/>（MenuService 门面）测试——菜单两阶段底层能力（设计文档 §3.3.2）：
/// PublishAsync 全链（CreateMenuAsync 调微信 → 拉取确认 → 同一事务：MenuSnapshot 更新 + MenuVersion 追加 +
/// MenuPublishLog Create 审计）/ RollbackAsync（加载旧 AfterJson 重新发布 → 追加新版本）/ GetCurrentAsync
/// （拉微信 + 快照 + PublishLog Read）/ DeleteAsync（menu/delete + 快照置空 + PublishLog Delete）。
/// <para>生产路径：<c>user.Use&lt;IMenuService&gt;()</c> AOP（AddConstructibleService 守卫工厂）；
/// 出站经 <see cref="TKWF.Federation.WeChat.WeChatApiClient"/> typed client（Stub HttpClientFactory 桩——
/// 不真打微信，menu/create 请求 body 记录供断言）。</para>
/// </summary>
public class MenuServiceTests : WeChatOperationsTestHost
{
    private const string MenuV1 = "{\"button\":[{\"type\":\"click\",\"name\":\"菜单一\",\"key\":\"V1001\"}]}";
    private const string MenuV2 = "{\"button\":[{\"type\":\"click\",\"name\":\"菜单二\",\"key\":\"V1002\"}]}";

    /// <summary>PublishAsync 全链——menu/create 调微信（body 透传）+ 拉取确认 + 快照/版本/审计同落库。</summary>
    [Fact]
    public async Task PublishAsync_FullChain_SnapshotVersionLog()
    {
        using var _ = this;
        var menuCreateBodies = new List<string>();
        Handler = WeChatOperationsTestSupport.CreateStubHandler(menuCreateBodies: menuCreateBodies);
        var (provider, user) = BindScope();
        await SeedConfigAsync(user);
        var service = user.Use<IMenuService>();

        var confirmed = await RunInFrameAsync(provider, user,
            () => service.PublishAsync("ch-menu", new PublishMenuRequest(MenuV1), ct: TestContext.Current.CancellationToken));

        Assert.NotNull(confirmed);   // 拉取确认（发布成功语义）
        // ① menu/create 收到 raw JSON 透传
        Assert.Single(menuCreateBodies);
        Assert.Equal(MenuV1, menuCreateBodies[0]);

        var fsql = provider.GetRequiredService<IFreeSql>();
        // ② MenuSnapshot 更新（本地权威基线）
        var snapshot = await fsql.Select<MenuSnapshotEntity>().Where(s => s.ChannelId == "ch-menu")
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(snapshot);
        Assert.False(string.IsNullOrWhiteSpace(snapshot!.MenuJson));
        // 拉取确认序列化——STJ 默认中文转 \uXXXX，断言反序列化后结构（含"测试菜单"）
        var snapMenu = System.Text.Json.JsonSerializer.Deserialize<TKWF.Federation.WeChat.WeChatSelfMenuInfoResult>(snapshot.MenuJson);
        Assert.NotNull(snapMenu);
        Assert.NotNull(snapMenu!.SelfMenuInfo);
        Assert.Equal("测试菜单", snapMenu.SelfMenuInfo!.Button[0].Name);

        // ③ MenuVersion 追加（v1——AfterJson=menuJson 透传原样，BeforeJson=null 首次）
        var versions = await fsql.Select<MenuVersionEntity>().Where(v => v.ChannelId == "ch-menu")
            .OrderBy(v => v.VersionNo).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Single(versions);
        Assert.Equal(1, versions[0].VersionNo);
        Assert.Equal(MenuV1, versions[0].AfterJson);
        Assert.Null(versions[0].BeforeJson);
        Assert.Equal(1, versions[0].Result);   // Success

        // ④ MenuPublishLog Create 审计（Action=2）
        var log = await fsql.Select<MenuPublishLogEntity>().Where(l => l.ChannelId == "ch-menu")
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);
        Assert.Equal(2, log!.Action);   // Create（发布）
    }

    /// <summary>二次发布——v2 BeforeJson = v1 快照备份（回滚数据源链）。</summary>
    [Fact]
    public async Task PublishAsync_SecondVersion_BeforeJsonIsFirstSnapshot()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        await SeedConfigAsync(user);
        var service = user.Use<IMenuService>();

        await RunInFrameAsync(provider, user, () => service.PublishAsync("ch-menu", new PublishMenuRequest(MenuV1), ct: TestContext.Current.CancellationToken));
        await RunInFrameAsync(provider, user, () => service.PublishAsync("ch-menu", new PublishMenuRequest(MenuV2), ct: TestContext.Current.CancellationToken));

        var fsql = provider.GetRequiredService<IFreeSql>();
        var versions = await fsql.Select<MenuVersionEntity>().Where(v => v.ChannelId == "ch-menu")
            .OrderBy(v => v.VersionNo).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(2, versions.Count);
        Assert.Equal(2, versions[1].VersionNo);
        Assert.Equal(MenuV2, versions[1].AfterJson);
        Assert.NotNull(versions[1].BeforeJson);   // v1 快照备份
    }

    /// <summary>RollbackAsync——加载旧版本 AfterJson 重新发布（menu/create body = 旧菜单 JSON）+ 追加新版本。</summary>
    [Fact]
    public async Task RollbackAsync_RepublishesOldVersion_AppendsNewVersion()
    {
        using var _ = this;
        var menuCreateBodies = new List<string>();
        Handler = WeChatOperationsTestSupport.CreateStubHandler(menuCreateBodies: menuCreateBodies);
        var (provider, user) = BindScope();
        await SeedConfigAsync(user);
        var service = user.Use<IMenuService>();

        await RunInFrameAsync(provider, user, () => service.PublishAsync("ch-menu", new PublishMenuRequest(MenuV1), ct: TestContext.Current.CancellationToken));
        await RunInFrameAsync(provider, user, () => service.PublishAsync("ch-menu", new PublishMenuRequest(MenuV2), ct: TestContext.Current.CancellationToken));
        var v1 = (await service.ListVersionsAsync("ch-menu", TestContext.Current.CancellationToken))[0];

        var confirmed = await RunInFrameAsync(provider, user,
            () => service.RollbackAsync("ch-menu", new RollbackMenuRequest(v1.Id), ct: TestContext.Current.CancellationToken));

        Assert.NotNull(confirmed);
        // menu/create 第三次收到 v1 的 AfterJson
        Assert.Equal(3, menuCreateBodies.Count);
        Assert.Equal(MenuV1, menuCreateBodies[2]);   // 回滚 = 重新发布 v1 内容

        var fsql = provider.GetRequiredService<IFreeSql>();
        var versions = await fsql.Select<MenuVersionEntity>().Where(v => v.ChannelId == "ch-menu")
            .OrderBy(v => v.VersionNo).ToListAsync(TestContext.Current.CancellationToken);
        Assert.Equal(3, versions.Count);
        Assert.Equal(3, versions[2].VersionNo);
        Assert.Equal(MenuV1, versions[2].AfterJson);   // 回滚后新版本内容 = v1
    }

    /// <summary>GetCurrentAsync——拉微信当前菜单 → 本地快照更新 + PublishLog Read 审计（Action=1）。</summary>
    [Fact]
    public async Task GetCurrentAsync_PullsWeChat_UpdatesSnapshot_LogsRead()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        await SeedConfigAsync(user);
        var service = user.Use<IMenuService>();

        var current = await RunInFrameAsync(provider, user,
            () => service.GetCurrentAsync("ch-menu", ct: TestContext.Current.CancellationToken));

        Assert.NotNull(current);
        Assert.Equal(1, current!.IsMenuOpen);
        Assert.NotNull(current.SelfMenuInfo);

        var fsql = provider.GetRequiredService<IFreeSql>();
        var snapshot = await fsql.Select<MenuSnapshotEntity>().Where(s => s.ChannelId == "ch-menu")
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(snapshot);
        // STJ 默认中文转 \uXXXX——断言反序列化后结构（含"测试菜单"）
        var snapMenu = System.Text.Json.JsonSerializer.Deserialize<TKWF.Federation.WeChat.WeChatSelfMenuInfoResult>(snapshot!.MenuJson);
        Assert.NotNull(snapMenu);
        Assert.NotNull(snapMenu!.SelfMenuInfo);
        Assert.Equal("测试菜单", snapMenu.SelfMenuInfo!.Button[0].Name);

        var log = await fsql.Select<MenuPublishLogEntity>().Where(l => l.ChannelId == "ch-menu")
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);
        Assert.Equal(1, log!.Action);   // Read
    }

    /// <summary>DeleteAsync——menu/delete 调微信 + 本地快照置空 + PublishLog Delete 审计（Action=3）。</summary>
    [Fact]
    public async Task DeleteAsync_ClearsSnapshot_LogsDelete()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        await SeedConfigAsync(user);
        var service = user.Use<IMenuService>();
        await RunInFrameAsync(provider, user,
            () => service.PublishAsync("ch-menu", new PublishMenuRequest(MenuV1), ct: TestContext.Current.CancellationToken));

        await RunInFrameAsync(provider, user, async () =>
        {
            await service.DeleteAsync("ch-menu", ct: TestContext.Current.CancellationToken);
            return true;
        });

        var fsql = provider.GetRequiredService<IFreeSql>();
        var snapshot = await fsql.Select<MenuSnapshotEntity>().Where(s => s.ChannelId == "ch-menu")
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(snapshot);
        Assert.Equal("", snapshot!.MenuJson);   // 快照置空（本地基准同步）

        var log = await fsql.Select<MenuPublishLogEntity>().Where(l => l.ChannelId == "ch-menu" && l.Action == 3)
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(log);   // Delete 审计
    }

    /// <summary>RollbackAsync 版本不存在 → InvalidOperationException。</summary>
    [Fact]
    public async Task RollbackAsync_UnknownVersion_Throws()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IMenuService>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.RollbackAsync("ch-menu", new RollbackMenuRequest(99999), TestContext.Current.CancellationToken));
        Assert.Contains("不存在", ex.Message);
    }

    /// <summary>PublishAsync 配置缺失 → InvalidOperationException（须先经 IChannelConfigService 配置）。</summary>
    [Fact]
    public async Task PublishAsync_NoConfig_Throws()
    {
        using var _ = this;
        var (_, user) = BindScope();
        var service = user.Use<IMenuService>();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.PublishAsync("ch-unknown", new PublishMenuRequest(MenuV1), TestContext.Current.CancellationToken));
        Assert.Contains("配置不存在", ex.Message);
    }

    /// <summary>种子公众号配置（经 IChannelConfigService 生产路径写入）。</summary>
    private static async Task SeedConfigAsync(DomainUser<TestUserInfo> user)
    {
        var service = user.Use<IChannelConfigService>();
        await service.SaveAsync(new SaveChannelRequest(
            Id: null, ChannelId: "ch-menu", AppId: "wx-menu-1", DisplayName: "菜单号", IsEnabled: true,
            ChannelType: "wechat_event", AppSecret: "menu-secret"), ct: TestContext.Current.CancellationToken);
    }
}
