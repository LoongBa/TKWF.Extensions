using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>N1：AuthAccountQueryService——查询方法委托真实 DataService（SQLite 内存库 + 真实 FreeSql DAC，红线合规委托路径）。
/// <para>V0.9.0（A.8 联邦归一化）：IAuthAccountQueryService 删 GetByWechatMp/WebOpenIdAsync（联邦 openid 绑定不再是账号列）。</para>
/// <para>V0.11.0（方案 §6.5/T7）：手机号不落账号列——<c>GetByPhoneAsync</c> 经 <c>IAuthFactorService.GetByPhoneAsync</c>
/// 反查 UId 再取账号；<c>IsWechatBoundAsync</c> 经 Federated 因素通道前缀判定（T1 删 PlatformAccountMap 通道列）。
/// 本测试按生产语义绑定 Sms/Federated 因素后断言查询委托。</para>
/// <para>V4.10.53（领域自治根治后重写）：门面继承 DomainServiceBase——StubDomainUser 直构（经基类 User 取上下文），
/// DataService 经 User.Use&lt;具体类&gt;() NoAop 直建（IEntityDAC 从 DI 解析）。</para></summary>
public class AuthAccountQueryServiceTests
{
    private static (AuthAccountQueryService Service, AuthAccountEntityDataService Ds, StubDomainUser Stub) CreateService()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql, register: (services, user) =>
            services.AddSingleton<IAuthFactorService>(new AuthFactorService(
                user, Options.Create(new SecurityLoggingOptions()), NullLogger<AuthFactorService>.Instance)));
        return (new AuthAccountQueryService(stub), stub.Use<AuthAccountEntityDataService>(), stub);
    }

    /// <summary>UId/Phone 查询委托真实 DataService（Phone 经因素行反查——账号列已无手机号）。</summary>
    [Fact]
    public async Task QueryMethods_Delegate_ToRealDataService()
    {
        var (service, ds, stub) = CreateService();

        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1001" });
        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1002" });
        await stub.Use<IAuthFactorService>().BindAsync("u-1001", AuthFactorTypes.Sms, "13812345678");

        Assert.Equal("u-1002", (await service.GetByUIdAsync("u-1002"))!.UId);
        Assert.Equal("u-1001", (await service.GetByPhoneAsync("13812345678"))!.UId);
    }

    /// <summary>未知键查询 → null（无账号 / 无对应因素行）。</summary>
    [Fact]
    public async Task QueryMethods_UnknownKey_ReturnsNull()
    {
        var (service, ds, _) = CreateService();
        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1001" });

        Assert.Null(await service.GetByUIdAsync("u-unknown"));
        Assert.Null(await service.GetByPhoneAsync("13900000000"));
    }

    /// <summary>V0.11.0（T7）：微信绑定判定经 Federated 因素通道前缀（wechat_mp / wechat_web）。</summary>
    [Fact]
    public async Task IsWechatBound_ReadsFederatedFactorChannel()
    {
        var (service, ds, stub) = CreateService();
        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1001" });
        await stub.Use<IAuthFactorService>().BindAsync("u-1001", AuthFactorTypes.Federated, "wechat_mp|openid-1");

        Assert.True(await service.IsWechatBoundAsync("u-1001"));
        Assert.False(await service.IsWechatBoundAsync("u-1002"));
    }

    /// <summary>V0.9.0（T3）：档案查询委托 UserProfile 1:1 表（凭据/档案分离——Nickname/Avatar 迁档案表）。</summary>
    [Fact]
    public async Task GetProfileByUId_ReadsUserProfileTable()
    {
        var (service, ds, stub) = CreateService();
        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1001" });

        // 档案种子（UserProfile 1:1——UId 唯一；经 DataService 落库，红线合规路径）
        await stub.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-1001",
            Nickname = "测试用户",
            Avatar = "https://cdn.example.com/a.png",
            Email = "test@example.com",
        });

        var profile = await service.GetProfileByUIdAsync("u-1001");
        Assert.NotNull(profile);
        Assert.Equal("u-1001", profile!.UId);
        Assert.Equal("测试用户", profile.Nickname);
        Assert.Equal("test@example.com", profile.Email);

        // 无档案 → null
        Assert.Null(await service.GetProfileByUIdAsync("u-unknown"));
    }
}
