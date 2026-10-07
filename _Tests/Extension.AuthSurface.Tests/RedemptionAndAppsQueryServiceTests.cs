using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuthSurface.Tests;

/// <summary>
/// 查询门面测试——兑换历史 + 我的应用（跨扩展 VEntity 视图 JOIN AuthAccount/AuthGrant/AuthApp）。
/// <para>UC-4/UC-5/UC-6 验收：Tier 1.5 SQLite 真实视图（Oracle4 P1-2 建表序契约——基表先于视图）+ 有效过滤
/// （Oracle4 P2-4：ValidUntil 过滤在 DataService predicate）+ 仅本人（Oracle4 P2-5：两视图 UserId 列映射断言）。</para>
/// </summary>
public class RedemptionAndAppsQueryServiceTests
{
    private static IFreeSql CreateInMemoryFreeSql() => AuthSurfaceTestHost.CreateInMemoryFreeSql();

    private static ServiceProvider CreateProvider(IFreeSql fsql)
        => AuthSurfaceTestHost.CreateProvider(fsql);

    private static DomainUser<TestUserInfo> BindUser(ServiceProvider sp, string userId = "u-1001")
    {
        DomainUser<TestUserInfo>.BindScope(sp);
        return new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo(userId, "测试用户") };
    }

    private static async Task<string> RedeemCodeAsync(IFreeSql fsql, ServiceProvider sp, string userId, string productName)
    {
        var user = BindUser(sp, userId);
        var cmd = user.Use<IRedemptionCommandService>();
        var code = await cmd.CreateCodeAsync(productName, "edu-course", null, CancellationToken.None);
        await cmd.RedeemAsync(userId, code, CancellationToken.None);
        return code;
    }

    // ── UC-4：兑换历史（跨扩展视图 JOIN AuthAccount——UserId 别名映射断言，Oracle4 P2-5） ──

    [Fact]
    public async Task GetRedemptionsAsync_ReturnsHistory_WithJoinedProfile()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        await AuthSurfaceTestHost.SeedAccountAsync(fsql, "u-1001", "13800138000");
        var code = await RedeemCodeAsync(fsql, sp, "u-1001", "精品课程 A");
        await RedeemCodeAsync(fsql, sp, "u-1001", "精品课程 B");

        var user = BindUser(sp, "u-1001");
        var list = await user.Use<IRedemptionQueryService>().GetRedemptionsAsync("u-1001", CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.All(list, r => Assert.Equal("edu-course", r.TargetAppId));
        Assert.Contains(list, r => r.ProductName == "精品课程 A");
        Assert.Contains(list, r => r.ProductName == "精品课程 B");
        Assert.All(list, r => Assert.Contains('*', r.CodeMasked));   // 已脱敏透传
    }

    // ── UC-6：仅本人——查询他人 userId 返回空（predicate 过滤数据层兜底） ──

    [Fact]
    public async Task GetRedemptionsAsync_OnlySelf_NoLeak()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        await RedeemCodeAsync(fsql, sp, "u-1001", "精品课程 A");

        var user = BindUser(sp, "u-1002");
        var list = await user.Use<IRedemptionQueryService>().GetRedemptionsAsync("u-1002", CancellationToken.None);

        Assert.Empty(list);                                        // 不泄露 u-1001 的兑换记录
    }

    // ── UC-4/UC-5：我的应用——跨扩展 JOIN（AuthGrant + AuthAccount + AuthApp） ──

    [Fact]
    public async Task GetAppsAsync_ReturnsActiveApps_WithAppNameMapping()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        await AuthSurfaceTestHost.SeedAccountAsync(fsql, "u-1001", "13800138000");
        await AuthSurfaceTestHost.SeedGrantAsync(fsql, "u-1001", "edu-course", status: 0, validUntil: DateTime.UtcNow.AddDays(365));

        // AuthApp 目录录入——AppName 映射
        var user = BindUser(sp, "u-1001");
        await user.Use<IAuthAppService>().CreateAppAsync("edu-course", "教育课程", "https://cdn.example.com/icon.png", CancellationToken.None);

        var list = await user.Use<IUserAppsQueryService>().GetAppsAsync("u-1001", CancellationToken.None);

        var app = Assert.Single(list);
        Assert.Equal("edu-course", app.AppId);
        Assert.Equal("教育课程", app.AppName);                    // LEFT JOIN AuthApp 映射
        Assert.True(app.IsAuthorized);
        Assert.NotNull(app.ExpiresAtUtc);
        Assert.Null(app.UsageSummary);                            // 儿童数据红线（v0.1.0 恒 null）
    }

    // ── UC-5：过期/吊销授权排除（ValidUntil 过滤在 DataService——Oracle4 P2-4） ──

    [Fact]
    public async Task GetAppsAsync_ExcludesExpiredAndRevoked()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        await AuthSurfaceTestHost.SeedAccountAsync(fsql, "u-1001", "13800138000");
        await AuthSurfaceTestHost.SeedGrantAsync(fsql, "u-1001", "app-expired", status: 0, validUntil: DateTime.UtcNow.AddDays(-1));   // 已过期
        await AuthSurfaceTestHost.SeedGrantAsync(fsql, "u-1001", "app-revoked", status: 1, validUntil: null);                            // 已吊销
        await AuthSurfaceTestHost.SeedGrantAsync(fsql, "u-1001", "app-active", status: 0, validUntil: DateTime.UtcNow.AddDays(30));      // 有效

        var user = BindUser(sp, "u-1001");
        var list = await user.Use<IUserAppsQueryService>().GetAppsAsync("u-1001", CancellationToken.None);

        var app = Assert.Single(list);
        Assert.Equal("app-active", app.AppId);
    }

    // ── UC-5：AuthApp 未录入——AppName null 不丢行（LEFT JOIN） ──

    [Fact]
    public async Task GetAppsAsync_AppNameNull_NotDropped()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        await AuthSurfaceTestHost.SeedAccountAsync(fsql, "u-1001", "13800138000");
        await AuthSurfaceTestHost.SeedGrantAsync(fsql, "u-1001", "app-no-catalog", status: 0, validUntil: null);   // 目录未录入

        var user = BindUser(sp, "u-1001");
        var list = await user.Use<IUserAppsQueryService>().GetAppsAsync("u-1001", CancellationToken.None);

        var app = Assert.Single(list);
        Assert.Equal("app-no-catalog", app.AppId);
        Assert.Null(app.AppName);                                // LEFT JOIN 不丢行
        Assert.True(app.IsAuthorized);
        Assert.Null(app.ExpiresAtUtc);                           // ValidUntil null = 持续授权
    }

    // ── UC-6：我的应用仅本人 ──

    [Fact]
    public async Task GetAppsAsync_OnlySelf_NoLeak()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        await AuthSurfaceTestHost.SeedAccountAsync(fsql, "u-1001", "13800138000");
        await AuthSurfaceTestHost.SeedGrantAsync(fsql, "u-1001", "edu-course", status: 0, validUntil: null);

        var user = BindUser(sp, "u-1002");
        var list = await user.Use<IUserAppsQueryService>().GetAppsAsync("u-1002", CancellationToken.None);

        Assert.Empty(list);                                      // 不泄露 u-1001 的应用授权
    }
}
