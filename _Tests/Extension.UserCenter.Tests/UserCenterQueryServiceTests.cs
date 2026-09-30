using System;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using TKWF.Ext.UserCenter;

namespace TKWF.Ext.UserCenter.Tests;

/// <summary>
/// 门面测试——UC-1~UC-6：降级矩阵 + 脱敏矩阵 + 档案缺失。
/// </summary>
public class UserCenterQueryServiceTests
{
    // ── UC-1：公共档案——门面输出 Phone 强制脱敏（实现方未自行脱敏仍正确） ──

    [Fact]
    public async Task GetProfile_MasksPhone_EvenWhenSourceReturnsRaw()
    {
        var sp = UserCenterTestInfra.BuildServices(
            profile: new MockProfileSource(UserCenterTestInfra.RawProfile()));

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetProfileAsync("u-1001", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("138****5678", result!.Phone);          // 原始 13812345678 → 脱敏
        Assert.Equal("u-1001", result.UserId);
        Assert.True(result.IsWechatBound);
        Assert.True(result.IsTeacherVerified);
        Assert.Equal(3, result.AuthLevel);
    }

    [Fact]
    public async Task GetProfile_NullPhone_ReturnsNull()   // 无手机号 → Mask null
    {
        var profile = UserCenterTestInfra.RawProfile() with { Phone = null };
        var sp = UserCenterTestInfra.BuildServices(profile: new MockProfileSource(profile));

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetProfileAsync("u-1001", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Null(result!.Phone);
    }

    [Fact]
    public async Task GetProfile_ShortPhone_MasksAll()     // 短号 ≤7 → 全 *
    {
        var profile = UserCenterTestInfra.RawProfile() with { Phone = "12345" };
        var sp = UserCenterTestInfra.BuildServices(profile: new MockProfileSource(profile));

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetProfileAsync("u-1001", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("*****", result!.Phone);
    }

    // ── UC-6：档案不存在（Source 返回 null）→ 门面返回 null ──

    [Fact]
    public async Task GetProfile_SourceReturnsNull_ReturnsNull()
    {
        var sp = UserCenterTestInfra.BuildServices(profile: new MockProfileSource((UserProfileDto?)null));

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetProfileAsync("u-1001", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    // ── 降级矩阵：Source 未注册 → null / 空列表 ──

    [Fact]
    public async Task GetProfile_NoSource_DegradesToNull()      // IUserProfileSource 缺失 → null
    {
        var sp = UserCenterTestInfra.BuildServices();   // 三 Source 全不注册

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetProfileAsync("u-1001", TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRedemptions_NoSource_ReturnsEmpty()    // IRedemptionHistorySource 缺失 → 空列表
    {
        var sp = UserCenterTestInfra.BuildServices();

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetRedemptionsAsync("u-1001", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetApps_NoSource_ReturnsEmpty()           // IUserAppsSource 缺失 → 空列表
    {
        var sp = UserCenterTestInfra.BuildServices();

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetAppsAsync("u-1001", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Empty(result);
    }

    // ── UC-2/UC-3：Source 注册 → 返回列表（code 已脱敏透传 / 应用列表） ──

    [Fact]
    public async Task GetRedemptions_WithSource_ReturnsList()
    {
        var item = UserCenterTestInfra.RedemptionItem();
        var sp = UserCenterTestInfra.BuildServices(redemptions: new MockRedemptionSource([item]));

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetRedemptionsAsync("u-1001", TestContext.Current.CancellationToken);

        var single = Assert.Single(result);
        Assert.Equal("CODE****1234", single.CodeMasked);    // 已脱敏透传
        Assert.Equal("app-math", single.TargetAppId);
        Assert.Equal("redeemed", single.Status);
    }

    [Fact]
    public async Task GetApps_WithSource_ReturnsList()
    {
        var item = UserCenterTestInfra.AppItem();
        var sp = UserCenterTestInfra.BuildServices(apps: new MockAppsSource([item]));

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetAppsAsync("u-1001", TestContext.Current.CancellationToken);

        var single = Assert.Single(result);
        Assert.Equal("app-math", single.AppId);
        Assert.True(single.IsAuthorized);
        Assert.Contains("2 次/周", single.UsageSummary);     // 不含学习明细（契约约束）
    }

    // ── UC-4：Source 抛异常 → 降级（null/空列表），不冒泡 ──

    [Fact]
    public async Task GetProfile_SourceThrows_DegradesToNull()
    {
        var sp = UserCenterTestInfra.BuildServices(
            profile: new MockProfileSource(new InvalidOperationException("db down")));

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetProfileAsync("u-1001", TestContext.Current.CancellationToken);   // 不应抛

        Assert.Null(result);
    }

    [Fact]
    public async Task GetRedemptions_SourceThrows_ReturnsEmpty()
    {
        var sp = UserCenterTestInfra.BuildServices(
            redemptions: new MockRedemptionSource(new InvalidOperationException("db down")));

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetRedemptionsAsync("u-1001", TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetApps_SourceThrows_ReturnsEmpty()
    {
        var sp = UserCenterTestInfra.BuildServices(
            apps: new MockAppsSource(new InvalidOperationException("db down")));

        var service = sp.GetRequiredService<IUserCenterQueryService>();
        var result = await service.GetAppsAsync("u-1001", TestContext.Current.CancellationToken);

        Assert.Empty(result);
    }
}

/// <summary>
/// 脱敏单元测试——UC-5：PhoneMasker 脱敏矩阵（标准 11 位 / 短号 / 空 / null / 带空格）。
/// </summary>
public class PhoneMaskerTests
{
    [Theory]
    [InlineData("13812345678", "138****5678")]    // 标准 11 位
    [InlineData("1234567", "*******")]             // 7 位 → 全 *
    [InlineData("12345", "*****")]                 // 5 位 → 全 *
    [InlineData("", null)]                         // 空串 → null
    [InlineData("   ", null)]                      // 空白 → null
    [InlineData(null, null)]                       // null → null
    [InlineData(" 13812345678 ", "138****5678")]   // 带空格先 Trim
    public void Mask_Matrix(string? input, string? expected)
        => Assert.Equal(expected, PhoneMasker.Mask(input));

    [Fact]
    public void Mask_LongNumber_PreservesHeadAndTail()
        => Assert.Equal("861*********7890", PhoneMasker.Mask("8613876543217890"));   // 16 位：前 3 + 9* + 后 4
}