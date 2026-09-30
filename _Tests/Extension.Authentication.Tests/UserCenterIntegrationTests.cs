using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.FreeSql;
using TKWF.Ext.UserCenter;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>N4/N5：认证中心 × UserCenter 装配冒烟——门面降级（零阻塞）+ 终态全链路（真实档案 + 门面强制脱敏）。</summary>
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
        var ds = new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)));
        await ds.CreateAsync(new AuthAccountEntity
        {
            UId = "u-1001",
            Phone = "13812345678",
            Nickname = "测试用户",
            Avatar = "https://cdn.example.com/a.png",
            TeacherVerified = true,
            AuthLevel = 3,
            WechatMpOpenId = "mp-1",
        });

        // 契约链：查询服务 → 档案源实现 → 门面
        var queryService = new AuthAccountQueryService(ds);
        var profileSource = new AuthAccountUserProfileSource(queryService);

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
        Assert.True(profile.IsWechatBound);
        Assert.Equal("测试用户", profile.Nickname);
        Assert.Equal("https://cdn.example.com/a.png", profile.AvatarUrl);
        Assert.True(profile.IsTeacherVerified);
        Assert.Equal(3, profile.AuthLevel);

        // 未知 userId → null（档案不存在降级）
        Assert.Null(await gate.GetProfileAsync("u-unknown"));
    }
}
