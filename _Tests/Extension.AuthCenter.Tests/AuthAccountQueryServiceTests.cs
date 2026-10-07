using System.Linq;
using System.Threading.Tasks;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>N1：AuthAccountQueryService——查询方法委托真实 DataService（SQLite 内存库 + 真实 FreeSql DAC，红线合规委托路径）。
/// <para>V0.9.0（A.8 联邦归一化）：IAuthAccountQueryService 删 GetByWechatMp/WebOpenIdAsync（微信 openid 绑定迁 PlatformAccountMap
/// ——WeChat Provider 经 ISsoChannelMapService 查号）；本测试仅验证保留的 UId/Phone 凭据查询委托。</para>
/// <para>V4.10.53（领域自治根治后重写）：门面继承 DomainServiceBase——StubDomainUser 直构（经基类 User 取上下文），
/// DataService 经 User.Use&lt;具体类&gt;() NoAop 直建（IEntityDAC 从 DI 解析）。业务断言语义不变。</para></summary>
public class AuthAccountQueryServiceTests
{
    private static (AuthAccountQueryService Service, AuthAccountEntityDataService Ds) CreateService()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var service = new AuthAccountQueryService(stub);
        return (service, stub.Use<AuthAccountEntityDataService>());
    }

    /// <summary>UId/Phone 查询委托真实 DataService（凭据查询——V0.9.0 保留面）。</summary>
    [Fact]
    public async Task QueryMethods_Delegate_ToRealDataService()
    {
        var (service, ds) = CreateService();

        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1001", Phone = "13812345678" });
        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1002", Phone = "13912345678" });

        Assert.Equal("u-1002", (await service.GetByUIdAsync("u-1002"))!.UId);
        Assert.Equal("u-1001", (await service.GetByPhoneAsync("13812345678"))!.UId);
    }

    /// <summary>未知键查询 → null（委托 DataService 无记录语义）。</summary>
    [Fact]
    public async Task QueryMethods_UnknownKey_ReturnsNull()
    {
        var (service, ds) = CreateService();
        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1001", Phone = "13812345678" });

        Assert.Null(await service.GetByUIdAsync("u-unknown"));
        Assert.Null(await service.GetByPhoneAsync("13900000000"));
    }

    /// <summary>V0.9.0（T3）：档案查询委托 UserProfile 1:1 表（凭据/档案分离——Nickname/Avatar 迁档案表）。</summary>
    [Fact]
    public async Task GetProfileByUId_ReadsUserProfileTable()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var service = new AuthAccountQueryService(stub);
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity { UId = "u-1001", Phone = "13812345678" });

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