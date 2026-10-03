using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// LoginHistoryService（V0.3.0）测试——登录历史分页/过滤/投影 + 异常检测 TopN（ByUser/ByIp + 时间窗口）+
/// SecurityLog 未启用的明确异常（C1 模式）。
/// <para>宿主模式 V0.5.x（批次间交互遗留修复，生产路径重写）：真实 DI（两扩展 Initializer ConfigureServices +
/// FreeSql SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> +
/// <c>User.Use&lt;接口&gt;()</c> AOP 路径——ILoginHistoryService 现为门面（AddConstructibleService 守卫工厂），
/// 数据插入/读取经 <c>User.Use&lt;ISecurityLogStore&gt;()</c> / <c>User.Use&lt;ILoginHistoryService&gt;()</c>
/// （SecurityLog V0.4.0 三契约守卫工厂须 AOP 帧；IDomainUser 永不注册 DI）。</para>
/// </summary>
public class LoginHistoryServiceTests
{
    // ── 测试宿主 ──

    /// <summary>创建 SQLite 内存库（独立连接）+ 同步 SecurityLogEntity 表。</summary>
    private static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<SecurityLogEntity>();
        return fsql;
    }

    /// <summary>
    /// 构建消费方生产形态 DI：两扩展 Initializer ConfigureServices（SecurityLog 可关——C1 未启用场景）
    /// + FreeSql 基础设施。DataService 不手动注册——生产经 SG 消费方聚合自动注册（throw-factory）；
    /// 测试经 <c>Use&lt;具体类&gt;()</c> NoAop 路径直建（IEntityDAC 从 DI 解析，红线合规，对齐 AccountProductionPathTests）。
    /// </summary>
    private static ServiceProvider CreateProvider(IFreeSql fsql, bool registerSecurityLog = true)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());   // SecurityLog AddOptions BindConfiguration 需 IConfiguration（空 = 默认值）
        services.AddSingleton(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<SecurityLogEntity>, FreeSqlEntityDAC<SecurityLogEntity>>();

        new AccountExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        if (registerSecurityLog)
            new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        return services.BuildServiceProvider();
    }

    /// <summary>绑定生产作用域 + 创建真实 DomainUser（消费方视角；IDomainUser 永不注册 DI——经基类 User / Use&lt;T&gt;）。</summary>
    private static DomainUser<TestUserInfo> BindUser(ServiceProvider sp)
    {
        DomainUser<TestUserInfo>.BindScope(sp);
        return new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "用户42") };
    }

    /// <summary>构造一条登录安全事件（CreateTime 由 SaveAsync 落库时取 UtcNow）。</summary>
    private static SecurityLogEntry Login(string userName, long? userId, string ip, string result, string? ua = null)
        => new("Login", "Authentication", userName, userId, ip, ua, result, null, null);

    /// <summary>经 ISecurityLogStore（SecurityLog 唯一写路径，AOP 守卫）插入安全事件。</summary>
    private static async Task InsertAsync(DomainUser<TestUserInfo> user, params SecurityLogEntry[] entries)
    {
        var store = user.Use<ISecurityLogStore>();
        foreach (var entry in entries)
            await store.SaveAsync(entry, CancellationToken.None);
    }

    // ── ① 登录历史（GetLoginHistoryAsync）──

    [Fact]
    public async Task GetLoginHistoryAsync_ReturnsOnlyLoginEvents_ProjectionCorrect()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        await InsertAsync(user,
            Login("alice", 1, "10.0.0.1", "Success"),
            Login("alice", 1, "10.0.0.1", "Failed"),
            Login("bob", 2, "10.0.0.2", "Failed"),
            Login("carol", 3, "10.0.0.3", "Success"),
            new SecurityLogEntry("Logout", "Authentication", "bob", 2, "10.0.0.2", null, "Success", null, null),   // 非 Login 事件排除
            new SecurityLogEntry("PasswordChange", "Authentication", "alice", 1, "10.0.0.1", null, "Success", null, null));

        var service = user.Use<ILoginHistoryService>();
        var result = await service.GetLoginHistoryAsync(new LoginHistoryQueryInput());

        Assert.Equal(4, result.Total);              // 仅 4 条 Login 事件（Logout/PasswordChange 被 EventType 过滤）
        Assert.Equal(4, result.Items.Count);
        Assert.Equal("carol", result.Items[0].UserName);   // CreateTime 倒序——最后插入的 Login 排最前
        Assert.Equal("alice", result.Items[^1].UserName);

        // 字段投影校验
        var bob = Assert.Single(result.Items, i => i.UserName == "bob");
        Assert.Equal(2, bob.UserId);
        Assert.Equal("10.0.0.2", bob.IpAddress);
        Assert.Equal("Failed", bob.Result);
        Assert.Null(bob.UserAgent);                 // 列表投影恒 null（SecurityLog 列表 DTO 不含 UA）
        Assert.True(bob.CreateTime > DateTime.MinValue);
        Assert.All(result.Items, i => Assert.True(i.Id > 0));
    }

    [Fact]
    public async Task GetLoginHistoryAsync_FiltersByUserName_Ip_Result()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        await InsertAsync(user,
            Login("alice", 1, "10.0.0.1", "Success"),
            Login("alice", 1, "10.0.0.1", "Failed"),
            Login("bob", 2, "10.0.0.2", "Failed"),
            Login("carol", 3, "10.0.0.3", "Success"));

        var service = user.Use<ILoginHistoryService>();

        // UserName LIKE
        var byUser = await service.GetLoginHistoryAsync(new LoginHistoryQueryInput { UserName = "ali" });
        Assert.Equal(2, byUser.Total);
        Assert.All(byUser.Items, i => Assert.Equal("alice", i.UserName));

        // Result 精确
        var byResult = await service.GetLoginHistoryAsync(new LoginHistoryQueryInput { Result = "Failed" });
        Assert.Equal(2, byResult.Total);
        Assert.All(byResult.Items, i => Assert.Equal("Failed", i.Result));

        // IpAddress LIKE
        var byIp = await service.GetLoginHistoryAsync(new LoginHistoryQueryInput { IpAddress = "10.0.0.2" });
        Assert.Equal(1, byIp.Total);
        Assert.Equal("bob", byIp.Items[0].UserName);

        // 组合过滤（alice + Failed）
        var combined = await service.GetLoginHistoryAsync(new LoginHistoryQueryInput { UserName = "alice", Result = "Failed" });
        Assert.Equal(1, combined.Total);
        Assert.Equal("Failed", combined.Items[0].Result);
    }

    [Fact]
    public async Task GetLoginHistoryAsync_Paging_DefaultTakeAndClamp()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        await InsertAsync(user,
            Login("u1", 1, "10.0.0.1", "Success"),
            Login("u2", 2, "10.0.0.2", "Success"),
            Login("u3", 3, "10.0.0.3", "Success"),
            Login("u4", 4, "10.0.0.4", "Success"),
            Login("u5", 5, "10.0.0.5", "Success"));

        var service = user.Use<ILoginHistoryService>();

        // Take=3 → 3 条，Total 仍为 5
        var paged = await service.GetLoginHistoryAsync(new LoginHistoryQueryInput { Take = 3 });
        Assert.Equal(5, paged.Total);
        Assert.Equal(3, paged.Items.Count);
        Assert.Equal("u5", paged.Items[0].UserName);   // 倒序

        // Skip=2 → 跳过最新 2 条（u5/u4），剩 u3/u2/u1
        var skipped = await service.GetLoginHistoryAsync(new LoginHistoryQueryInput { Skip = 2, Take = 3 });
        Assert.Equal(3, skipped.Items.Count);
        Assert.Equal("u3", skipped.Items[0].UserName);

        // Take=0 → 回退默认 50 → 全量
        var defaultTake = await service.GetLoginHistoryAsync(new LoginHistoryQueryInput { Take = 0 });
        Assert.Equal(5, defaultTake.Items.Count);

        // Take=500 → 钳制上限 200 → 全量（仅 5 条）
        var clamped = await service.GetLoginHistoryAsync(new LoginHistoryQueryInput { Take = 500 });
        Assert.Equal(5, clamped.Items.Count);
    }

    [Fact]
    public async Task GetLoginHistoryAsync_NullInput_ThrowsArgumentNullException()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var service = user.Use<ILoginHistoryService>();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.GetLoginHistoryAsync(null!));
    }

    // ── ② SecurityLog 未启用（C1 明确异常）──

    [Fact]
    public async Task GetLoginHistoryAsync_SecurityLogNotRegistered_ThrowsInvalidOperationException()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql, registerSecurityLog: false);   // 仅 Account 扩展启用
        var user = BindUser(sp);
        var service = user.Use<ILoginHistoryService>();

        // C1 模式：SecurityLog 契约未注册 → 门面内 User.Use<ISecurityLogQueryService>() 经 AOP 路径 GetRequiredService
        // 抛 InvalidOperationException（未注册异常，消息含 SecurityLog 类型名）——保持原有"明确异常"语义
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.GetLoginHistoryAsync(new LoginHistoryQueryInput()));
        Assert.Contains("SecurityLog", ex.Message);                        // 提示须启用 SecurityLog 扩展
    }

    [Fact]
    public async Task GetTopFailed_SecurityLogNotRegistered_ThrowsInvalidOperationException()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql, registerSecurityLog: false);
        var user = BindUser(sp);
        var service = user.Use<ILoginHistoryService>();

        var exUser = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetTopFailedUsersAsync());
        Assert.Contains("SecurityLog", exUser.Message);

        var exIp = await Assert.ThrowsAsync<InvalidOperationException>(() => service.GetTopFailedIpsAsync());
        Assert.Contains("SecurityLog", exIp.Message);
    }

    // ── ③ 异常检测（GetTopFailedUsersAsync / GetTopFailedIpsAsync）──

    [Fact]
    public async Task GetTopFailedUsersAsync_ReturnsTopNByFailedCount()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        await InsertAsync(user,
            Login("alice", 1, "10.0.0.1", "Failed"),
            Login("alice", 1, "10.0.0.1", "Failed"),
            Login("alice", 1, "10.0.0.1", "Failed"),
            Login("alice", 1, "10.0.0.1", "Success"),     // Success 不计入
            Login("bob", 2, "10.0.0.2", "Failed"),
            Login("bob", 2, "10.0.0.2", "Failed"),
            Login("carol", 3, "10.0.0.3", "Failed"),
            Login("carol", 3, "10.0.0.3", "Failed"),
            Login("carol", 3, "10.0.0.3", "Failed"),
            Login("carol", 3, "10.0.0.3", "Failed"));

        var service = user.Use<ILoginHistoryService>();

        var top2 = await service.GetTopFailedUsersAsync(topN: 2);
        Assert.Equal(2, top2.Count);
        Assert.Equal("carol", top2[0].Dimension);
        Assert.Equal(4, top2[0].Count);
        Assert.Equal("alice", top2[1].Dimension);
        Assert.Equal(3, top2[1].Count);                  // Success 不计数

        var top5 = await service.GetTopFailedUsersAsync(topN: 5);
        Assert.Equal(3, top5.Count);                     // 仅 3 个有失败记录的用户
        Assert.Equal("bob", top5[2].Dimension);
        Assert.Equal(2, top5[2].Count);
    }

    [Fact]
    public async Task GetTopFailedIpsAsync_ReturnsTopNByIp_ExcludesNullEmpty()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        await InsertAsync(user,
            Login("alice", 1, "10.0.0.1", "Failed"),
            Login("bob", 2, "10.0.0.1", "Failed"),
            Login("carol", 3, "10.0.0.2", "Failed"),
            Login("dave", 4, null, "Failed"),            // IP null 不计入
            Login("erin", 5, "", "Failed"));             // IP 空串不计入

        var service = user.Use<ILoginHistoryService>();
        var result = await service.GetTopFailedIpsAsync(topN: 10);

        Assert.Equal(2, result.Count);
        Assert.Equal("10.0.0.1", result[0].Dimension);
        Assert.Equal(2, result[0].Count);
        Assert.Equal("10.0.0.2", result[1].Dimension);
        Assert.Equal(1, result[1].Count);
        Assert.All(result, s => Assert.False(string.IsNullOrWhiteSpace(s.Dimension)));
        Assert.Equal(3, result.Sum(s => s.Count));       // null/空 IP 的 2 条被跳过
    }

    [Fact]
    public async Task TopN_DefaultAndWindow()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var now = DateTime.UtcNow;

        // 测试种子直接经 fsql 插入带显式 CreateTime 的历史行（仅测试数据铺设，非生产数据访问）。
        // 时间余量对齐 SecurityLog 自身窗口测试先例（now.AddMinutes(-10) / now.AddHours(-2)）：
        // FreeSql+SQLite 对 DateTime 参数/current_timestamp 的时区处理不一致——同秒边界竞争会误过滤，
        // 窗口测试须留 10 分钟级安全余量，勿用"插入即查询"时刻。
        foreach (var _ in Enumerable.Range(0, 3))
        {
            fsql.Insert(new SecurityLogEntity
            {
                EventType = "Login",
                EventCategory = "Authentication",
                UserName = "alice",
                IpAddress = "10.0.0.1",
                Result = "Failed",
                CreateTime = now.AddMinutes(-10),   // 窗内（30 分钟窗口）
            }).ExecuteAffrows();
        }
        foreach (var _ in Enumerable.Range(0, 2))
        {
            fsql.Insert(new SecurityLogEntity
            {
                EventType = "Login",
                EventCategory = "Authentication",
                UserName = "bob",
                IpAddress = "10.0.0.2",
                Result = "Failed",
                CreateTime = now.AddHours(-2),      // 窗外
            }).ExecuteAffrows();
        }

        var service = user.Use<ILoginHistoryService>();

        // 默认 topN=10（不传 topN）全量 → 两用户都计入
        var all = await service.GetTopFailedUsersAsync();
        Assert.Equal(2, all.Count);
        Assert.Equal(3, all.Single(s => s.Dimension == "alice").Count);
        Assert.Equal(2, all.Single(s => s.Dimension == "bob").Count);

        // 30 分钟窗口 → 仅 alice（bob 在 2 小时前被过滤）
        var windowed = await service.GetTopFailedUsersAsync(window: TimeSpan.FromMinutes(30));
        var alice = Assert.Single(windowed);
        Assert.Equal("alice", alice.Dimension);
        Assert.Equal(3, alice.Count);

        // IP 维度同理
        var ipWindowed = await service.GetTopFailedIpsAsync(window: TimeSpan.FromMinutes(30));
        var ip = Assert.Single(ipWindowed);
        Assert.Equal("10.0.0.1", ip.Dimension);
        Assert.Equal(3, ip.Count);
    }
}