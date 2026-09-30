using Microsoft.Extensions.Logging.Abstractions;
using TKWF.Ext.UserCenter;

namespace TKWF.Ext.UserCenter.Tests;

/// <summary>测试 Mock 源——内存桩（UserCenter 零 DB 依赖的接线型测试）。
/// <para>UC-1 关键语义：MockProfileSource 返回<b>原始</b>手机号（`13812345678`），
/// 门面输出前经 PhoneMasker 强制脱敏（实现方不自行脱敏仍正确）。</para></summary>
public sealed class MockProfileSource : IUserProfileSource
{
    private readonly UserProfileDto? _profile;
    private readonly Exception? _throw;

    /// <summary>构造：返回固定档案（Phone 为原始值）。</summary>
    public MockProfileSource(UserProfileDto? profile)
    {
        _profile = profile;
        _throw = null;
    }

    /// <summary>构造：抛异常（UC-4 降级验证——档案读取失败降级 null）。</summary>
    public MockProfileSource(Exception throwException)
    {
        _throw = throwException;
        _profile = null;
    }

    public Task<UserProfileDto?> GetProfileAsync(string userId, CancellationToken ct = default)
    {
        if (_throw is not null)
            throw _throw;
        return Task.FromResult(_profile);
    }
}

/// <summary>测试 Mock 兑换历史源——返回固定列表/空列表/异常。</summary>
public sealed class MockRedemptionSource : IRedemptionHistorySource
{
    private readonly IReadOnlyList<RedemptionRecordDto> _items;
    private readonly Exception? _throw;

    public MockRedemptionSource(IReadOnlyList<RedemptionRecordDto> items)
    {
        _items = items;
        _throw = null;
    }

    public MockRedemptionSource(Exception throwException)
    {
        _throw = throwException;
        _items = [];
    }

    public Task<IReadOnlyList<RedemptionRecordDto>> GetRedemptionsAsync(string userId, CancellationToken ct = default)
    {
        if (_throw is not null)
            throw _throw;
        return Task.FromResult(_items);
    }
}

/// <summary>测试 Mock 我的应用源——返回固定列表/空列表/异常。</summary>
public sealed class MockAppsSource : IUserAppsSource
{
    private readonly IReadOnlyList<UserAppDto> _items;
    private readonly Exception? _throw;

    public MockAppsSource(IReadOnlyList<UserAppDto> items)
    {
        _items = items;
        _throw = null;
    }

    public MockAppsSource(Exception throwException)
    {
        _throw = throwException;
        _items = [];
    }

    public Task<IReadOnlyList<UserAppDto>> GetAppsAsync(string userId, CancellationToken ct = default)
    {
        if (_throw is not null)
            throw _throw;
        return Task.FromResult(_items);
    }
}

/// <summary>
/// 测试基础设施——DI 容器构建（门面 + 可选 Mock Source）。
/// <para>对齐 Dashboard DashboardTestInfra 先例——手动 ServiceCollection 构建（不经 DomainHost 全链路，
/// 快速隔离测试门面领域逻辑）；ConsumerHostInitializer 单独验证白名单装配。</para>
/// </summary>
internal static class UserCenterTestInfra
{
    /// <summary>标准测试原始档案（Phone 为原始值——门面应输出脱敏后的 `138****5678`）。</summary>
    public static UserProfileDto RawProfile() => new(
        UserId: "u-1001",
        Phone: "13812345678",
        IsWechatBound: true,
        Nickname: "测试用户",
        AvatarUrl: "https://cdn.example.com/avatar/u-1001.png",
        IsTeacherVerified: true,
        AuthLevel: 3);

    /// <summary>标准测试兑换历史条目（CodeMasked 已脱敏——门面透传）。</summary>
    public static RedemptionRecordDto RedemptionItem() => new(
        CodeMasked: "CODE****1234",
        ProductName: "精品课程 A",
        TargetAppId: "app-math",
        RedeemedAtUtc: new DateTime(2026, 9, 1, 8, 0, 0, DateTimeKind.Utc),
        Status: "redeemed");

    /// <summary>标准测试应用条目（UsageSummary 不含学习明细——儿童数据红线）。</summary>
    public static UserAppDto AppItem() => new(
        AppId: "app-math",
        AppName: "数学思维",
        IsAuthorized: true,
        ExpiresAtUtc: new DateTime(2027, 9, 1, 0, 0, 0, DateTimeKind.Utc),
        UsageSummary: "近期使用：2 次/周");

    /// <summary>构建门面服务容器（可选注册三 Mock Source；不注册 = 降级验证）。</summary>
    public static IServiceProvider BuildServices(
        IUserProfileSource? profile = null,
        IRedemptionHistorySource? redemptions = null,
        IUserAppsSource? apps = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddOptions<UserCenterOptions>();
        services.TryAddScoped<IUserCenterQueryService, UserCenterQueryService>();

        if (profile is not null)
            services.AddScoped<IUserProfileSource>(_ => profile);
        if (redemptions is not null)
            services.AddScoped<IRedemptionHistorySource>(_ => redemptions);
        if (apps is not null)
            services.AddScoped<IUserAppsSource>(_ => apps);

        return services.BuildServiceProvider();
    }
}