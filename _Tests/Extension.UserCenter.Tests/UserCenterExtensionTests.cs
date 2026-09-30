using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TKWF.Ext.UserCenter;

namespace TKWF.Ext.UserCenter.Tests;

/// <summary>
/// 模板基类装配测试——UC-9：继承 <c>UserProfileSourceBase</c> 最小适配编译运行。
/// <para>对齐 IdentityUserHelperBase 先例：装配实例仅实现查询钩子（几行），映射/降级/日志管线预实现。</para>
/// </summary>
public class SourceBaseTests
{
    // 装配实例最小适配——仅实现查询钩子（模拟认证中心桥接：查 AuthAccountEntity）
    private sealed class TestProfileSource : UserProfileSourceBase
    {
        private readonly UserProfileDto? _return;
        public TestProfileSource(UserProfileDto? returnValue) : base(NullLogger<UserProfileSourceBase>.Instance)
            => _return = returnValue;

        protected override Task<RawProfile?> QueryRawProfileAsync(string userId, CancellationToken ct)
            => Task.FromResult(_return is null
                ? null
                : new RawProfile(_return.Phone, _return.IsWechatBound, _return.Nickname, _return.AvatarUrl, _return.IsTeacherVerified, _return.AuthLevel));
    }

    private sealed class TestRedemptionSource : RedemptionHistorySourceBase
    {
        private readonly Exception? _throw;
        public TestRedemptionSource(Exception? throwException = null) : base(NullLogger<RedemptionHistorySourceBase>.Instance)
            => _throw = throwException;

        protected override Task<IReadOnlyList<RawRedemption>> QueryRawRedemptionsAsync(string userId, CancellationToken ct)
        {
            if (_throw is not null)
                throw _throw;
            return Task.FromResult<IReadOnlyList<RawRedemption>>(
                [new("CODE****1234", "课程", "app-1", new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), "redeemed")]);
        }
    }

    [Fact]
    public async Task ProfileSourceBase_MinimalAdapter_Runs()
    {
        var source = new TestProfileSource(new UserProfileDto(
            "u-1", "13812345678", true, "昵称", null, false, 2));

        var profile = await source.GetProfileAsync("u-1", TestContext.Current.CancellationToken);

        Assert.NotNull(profile);
        Assert.Equal("13812345678", profile!.Phone);   // 基类透传原始值——门面负责脱敏
        Assert.True(profile.IsWechatBound);
    }

    [Fact]
    public async Task ProfileSourceBase_QueryReturnsNull_FacadeNull()   // 档案不存在 → 基类返回 null（门面随之 null）
    {
        var source = new TestProfileSource(null);
        var sp = UserCenterTestInfra.BuildServices(profile: source);

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetProfileAsync("u-1", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task RedemptionSourceBase_Exception_DegradesToEmpty()   // 查询钩子抛异常 → 基类降级空列表
    {
        var source = new TestRedemptionSource(new InvalidOperationException("boom"));
        var sp = UserCenterTestInfra.BuildServices(redemptions: source);

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetRedemptionsAsync("u-1", TestContext.Current.CancellationToken);

        Assert.Empty(result);   // 降级，不冒泡
    }

    [Fact]
    public async Task RedemptionSourceBase_ReturnsItems()
    {
        var sp = UserCenterTestInfra.BuildServices(redemptions: new TestRedemptionSource());

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetRedemptionsAsync("u-1", TestContext.Current.CancellationToken);

        var single = Assert.Single(result);
        Assert.Equal("CODE****1234", single.CodeMasked);
    }
}

/// <summary>
/// Initializer DI 测试——UC-8：白名单装配冒烟——<c>[TKWFEnabledExtension]</c> 声明后
/// 门面 <see cref="IUserCenterQueryService"/> 自动注册；Source 可注入（Mock）。
/// </summary>
public class UserCenterExtensionInitializerTests
{
    private static ServiceProvider BuildHost()
    {
        var services = new ServiceCollection();
        services.AddLogging();

        // 模拟扩展 ConfigureServices 被三钩子接线后的注册
        var initializer = new UserCenterExtensionInitializer<TestUserInfo>();
        initializer.ConfigureServices(services);

        return services.BuildServiceProvider();
    }

    [Fact]
    public void ConfigureServices_RegistersFacade()
    {
        var sp = BuildHost();

        var facade = sp.GetService<IUserCenterQueryService>();
        Assert.NotNull(facade);
        Assert.IsType<UserCenterQueryService>(facade);
    }

    [Fact]
    public void ConfigureServices_DoesNotRegisterSources()   // Source 接口不注册默认实现——由属主扩展/装配实例提供
    {
        var sp = BuildHost();

        Assert.Null(sp.GetService<IUserProfileSource>());
        Assert.Null(sp.GetService<IRedemptionHistorySource>());
        Assert.Null(sp.GetService<IUserAppsSource>());
    }

    [Fact]
    public async Task ConsumerHost_Facade_WorksWithMockSource()   // 冒烟：注入 Mock Source → 门面可用
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<UserCenterOptions>();

        var initializer = new UserCenterExtensionInitializer<TestUserInfo>();
        initializer.ConfigureServices(services);

        services.TryAddScoped<IUserProfileSource>(_ => new MockProfileSource(UserCenterTestInfra.RawProfile()));
        services.TryAddScoped<IRedemptionHistorySource>(_ => new MockRedemptionSource([UserCenterTestInfra.RedemptionItem()]));
        services.TryAddScoped<IUserAppsSource>(_ => new MockAppsSource([UserCenterTestInfra.AppItem()]));

        await using var sp = services.BuildServiceProvider();
        var facade = sp.GetRequiredService<IUserCenterQueryService>();

        var profile = await facade.GetProfileAsync("u-1001", TestContext.Current.CancellationToken);
        Assert.NotNull(profile);
        Assert.Equal("138****5678", profile!.Phone);

        var redemptions = await facade.GetRedemptionsAsync("u-1001", TestContext.Current.CancellationToken);
        Assert.Single(redemptions);
    }
}