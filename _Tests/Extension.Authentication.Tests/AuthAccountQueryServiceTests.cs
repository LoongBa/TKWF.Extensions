using System.Linq;
using System.Threading.Tasks;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>N1：AuthAccountQueryService——4 查询方法委托真实 DataService（SQLite 内存库 + 真实 FreeSql DAC，红线合规委托路径）。</summary>
public class AuthAccountQueryServiceTests
{
    private static (AuthAccountQueryService Service, AuthAccountEntityDataService Ds) CreateService()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var ds = new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)));
        return (new AuthAccountQueryService(ds), ds);
    }

    /// <summary>4 账号：UId/Phone/Mp/Web 四键互不相同——逐方法验证委托正确性（各方法命中各自键对应的账号）。</summary>
    [Fact]
    public async Task QueryMethods_Delegate_ToRealDataService()
    {
        var (service, ds) = CreateService();

        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1001", Phone = "13812345678", WechatMpOpenId = "mp-1", WechatWebOpenId = "web-1" });
        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1002", Phone = "13912345678", WechatMpOpenId = "mp-2" });
        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1003", WechatWebOpenId = "web-2" });
        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1004", Phone = "13712345678", WechatMpOpenId = "mp-3", WechatWebOpenId = "web-3" });

        Assert.Equal("u-1002", (await service.GetByUIdAsync("u-1002"))!.UId);
        Assert.Equal("u-1001", (await service.GetByPhoneAsync("13812345678"))!.UId);
        Assert.Equal("u-1004", (await service.GetByWechatMpOpenIdAsync("mp-3"))!.UId);
        Assert.Equal("u-1003", (await service.GetByWechatWebOpenIdAsync("web-2"))!.UId);
    }

    /// <summary>未知键查询 → null（委托 DataService 无记录语义）。</summary>
    [Fact]
    public async Task QueryMethods_UnknownKey_ReturnsNull()
    {
        var (service, ds) = CreateService();
        await ds.CreateAsync(new AuthAccountEntity { UId = "u-1001", Phone = "13812345678", WechatMpOpenId = "mp-1", WechatWebOpenId = "web-1" });

        Assert.Null(await service.GetByUIdAsync("u-unknown"));
        Assert.Null(await service.GetByPhoneAsync("13900000000"));
        Assert.Null(await service.GetByWechatMpOpenIdAsync("mp-unknown"));
        Assert.Null(await service.GetByWechatWebOpenIdAsync("web-unknown"));
    }
}
