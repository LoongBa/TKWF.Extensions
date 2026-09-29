using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>D11：PlatformAccountMap——Link upsert / 双向查询 / UnionId 串联。</summary>
public class PlatformAccountMapServiceTests
{
    private static (PlatformAccountMapService Service, PlatformAccountMapEntityDataService Ds) CreateService()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var ds = new PlatformAccountMapEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<PlatformAccountMapEntity>(new UnitOfWorkManager(fsql)));
        return (new PlatformAccountMapService(ds, NullLogger<PlatformAccountMapService>.Instance), ds);
    }

    [Fact]
    public async Task Link_CreatesAndUpserts()
    {
        var (service, _) = CreateService();

        var created = await service.LinkAsync("u-100", "app-1", "local-1", null);
        Assert.NotNull(created.Id);

        // upsert：同键存在 → 更新 UnionId
        var updated = await service.LinkAsync("u-100", "app-1", "local-1", "union-xyz");
        Assert.Equal(created.Id, updated.Id);
        Assert.Equal("union-xyz", updated.UnionId);
    }

    [Fact]
    public async Task GetByPlatform_And_GetByBusiness_Bidirectional()
    {
        var (service, _) = CreateService();
        await service.LinkAsync("u-100", "app-1", "local-1", null);

        var byPlatform = await service.GetByPlatformAsync("u-100", "app-1");
        Assert.NotNull(byPlatform);
        Assert.Equal("local-1", byPlatform!.BusinessLocalId);

        var byBusiness = await service.GetByBusinessAsync("app-1", "local-1");
        Assert.NotNull(byBusiness);
        Assert.Equal("u-100", byBusiness!.PlatformAccountId);
    }

    [Fact]
    public async Task GetByUnionId_CrossAppLinks()
    {
        var (service, _) = CreateService();
        await service.LinkAsync("u-100", "app-1", "local-1", "union-xyz");
        await service.LinkAsync("u-100", "app-2", "local-2", "union-xyz");

        var links = await service.GetByUnionIdAsync("union-xyz");
        Assert.Equal(2, links.Count);
    }
}
