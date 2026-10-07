using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.FreeSql;
using TKWF.Ext.UserCenter;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>N4/N5：认证中心 × UserCenter 装配冒烟——门面降级（零阻塞）+ 终态全链路（真实档案 + 门面强制脱敏）。
/// <para>V4.10.53（领域自治根治后重写）：接线型 AuthAccountUserProfileSource 直构
/// <c>new AuthAccountUserProfileSource(sp)</c>（sp 经 CreateStub 注册 IAuthAccountQueryService——C1 延迟解析）。</para></summary>
public class UserCenterIntegrationTests
{
    /// <summary>N4：IUserProfileSource 未注册 → 门面降级 null（零阻塞——契约未实现方装配时用户中心页面仍可渲染）。</summary>
    [Fact]
    public async Task ProfileSourceNotRegistered_GateDegradesToNull()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddScoped<IUserCenterQueryService, UserCenterQueryService>();

        await using var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<IUserCenterQueryService>();

        Assert.Null(await gate.GetProfileAsync("u-x"));
    }

    /// <summary>N5：终态装配冒烟——真实 SQLite 内存库 → DataService → AuthAccountQueryService → AuthAccountUserProfileSource
    /// → UserCenter 门面；门面强制脱敏（Phone → "138****5678"），档案字段全对；未知 userId → null。</summary>
    [Fact]
    public async Task FullChain_ProfileSourceRegistered_GateReturnsMaskedRealProfile()
    {
        // 真实数据链：种子账号经 DataService 落库（红线合规路径——无裸 ORM）
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql, register: (services, user) =>
        {
            // 查询契约（接口）→ CreateStub 注册单例，供 AuthAccountUserProfileSource（接线型 C1 延迟解析）
            services.AddSingleton<IAuthAccountQueryService>(new AuthAccountQueryService(user));
        });
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity
        {
            UId = "u-1001",
            Phone = "13812345678",
            AuthLevel = (int)AuthLevel.Federated,
        });

        // V0.9.0（T3/A.1 凭据/档案分离）：档案（Nickname/Avatar）迁 UserProfile 1:1 表；微信绑定经
        // PlatformAccountMap 通道行（IsWechatBoundAsync）——经 ISsoChannelMapService 映射种入 wechat_mp 通道
        await stub.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-1001",
            Nickname = "测试用户",
            Avatar = "https://cdn.example.com/a.png",
            Email = "test@example.com",
        });
        await stub.Use<PlatformAccountMapEntityDataService>().CreateAsync(new PlatformAccountMapEntity
        {
            PlatformAccountId = "u-1001",
            BusinessAppId = "wechat_mp",
            BusinessLocalId = "mp-1",
            ChannelId = "wechat_mp",
            ExternalUserId = "mp-1",
            CreateTime = System.DateTime.UtcNow,
            UpdateTime = System.DateTime.UtcNow,
        });

        // 档案源实现（接线型：ctor(IServiceProvider) + C1 延迟解析 IAuthAccountQueryService）
        var profileSource = new AuthAccountUserProfileSource(stub.ServiceProvider!);

        var services = new ServiceCollection();
        services.AddLogging();
        services.TryAddScoped<IUserCenterQueryService, UserCenterQueryService>();
        services.AddScoped<IUserProfileSource>(_ => profileSource);

        await using var provider = services.BuildServiceProvider();
        var gate = provider.GetRequiredService<IUserCenterQueryService>();

        // 终态零桥接：消费方白名单声明认证中心 + UserCenter 后，门面自动获得真实档案（Phone 被门面强制脱敏）
        var profile = await gate.GetProfileAsync("u-1001");
        Assert.NotNull(profile);
        Assert.Equal("u-1001", profile!.UserId);
        Assert.Equal("138****5678", profile.Phone);   // PhoneMasker head3/tail4——证明终态装配零桥接 + 门面强制脱敏
        Assert.True(profile.IsWechatBound);           // PlatformAccountMap wechat_mp 通道行（A.8 联邦归一化）
        Assert.Equal("测试用户", profile.Nickname);    // UserProfile 1:1 档案
        Assert.Equal("https://cdn.example.com/a.png", profile.AvatarUrl);
        Assert.False(profile.IsTeacherVerified);      // V0.9.0（A.4）：TeacherVerified 迁出——档案面恒 false
        Assert.Equal((int)AuthLevel.Federated, profile.AuthLevel);   // AuthLevel 泛化（2=联邦快捷）

        // 未知 userId → null（档案不存在降级）
        Assert.Null(await gate.GetProfileAsync("u-unknown"));
    }
}
