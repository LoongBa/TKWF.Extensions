using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.OrganizationUnit;

namespace TKWF.Ext.OrganizationUnit.Tests;

/// <summary>
/// 组织单元 DataService 直调正确性测试（A5 Store-debt 清理——internal thin Store 伪层已删）。
/// 门面经 User.Use&lt;XxxDataService&gt;() 懒加载；本类直接验证 DataService 委托持久化路径。
/// </summary>
public class OrganizationUnitDataServiceTests
{
    private static OrganizationUnitTestHost NewHost() => OrganizationUnitTestHost.Create();

    private static async Task<OrganizationUnitEntity> CreateOu(OrganizationUnitTestHost host, string code, CancellationToken ct = default)
    {
        var entity = new OrganizationUnitEntity
        {
            Code = code,
            Name = code,
            ParentId = null,
            SortOrder = 0,
            Level = 0,
            Path = $"/{code}/",
            IsEnabled = true
        };
        await host.OuDataService.EntityCreateAsync(entity, ct);
        return entity;
    }

    [Fact]
    public async Task Create_And_GetById_Returns()
    {
        using var host = NewHost();

        var created = await CreateOu(host, "HQ", ct: TestContext.Current.CancellationToken);

        Assert.True(created.Id > 0);
        var byId = await host.OuDataService.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.NotNull(byId);
        Assert.Equal(created.Id, byId!.Id);
    }

    [Fact]
    public async Task GetByCode_ForwardsToDataService()
    {
        using var host = NewHost();
        var created = await CreateOu(host, "FIN", ct: TestContext.Current.CancellationToken);

        var byCode = await host.OuDataService.GetByCodeAsync("FIN", CancellationToken.None);

        Assert.NotNull(byCode);
        Assert.Equal(created.Id, byCode!.Id);
        Assert.Equal("FIN", byCode.Code);

        Assert.Null(await host.OuDataService.GetByCodeAsync("NOT_EXISTS", CancellationToken.None));
    }

    [Fact]
    public async Task GetAll_ReturnsAllRows()
    {
        using var host = NewHost();
        await CreateOu(host, "A", ct: TestContext.Current.CancellationToken);
        await CreateOu(host, "B", ct: TestContext.Current.CancellationToken);
        await CreateOu(host, "C", ct: TestContext.Current.CancellationToken);

        var all = await host.OuDataService.GetAllAsync(CancellationToken.None);

        Assert.Equal(3, all.Count);
        Assert.Contains(all, e => e.Code == "A");
        Assert.Contains(all, e => e.Code == "B");
        Assert.Contains(all, e => e.Code == "C");
    }

    [Fact]
    public async Task Update_PersistsChanges()
    {
        using var host = NewHost();
        var created = await CreateOu(host, "HQ", ct: TestContext.Current.CancellationToken);

        created.Name = "总部（更新）";
        created.IsEnabled = false;
        await host.OuDataService.EntityUpdateAsync(created, CancellationToken.None);

        var reloaded = await host.OuDataService.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal("总部（更新）", reloaded!.Name);
        Assert.False(reloaded.IsEnabled);
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        using var host = NewHost();
        var created = await CreateOu(host, "HQ", ct: TestContext.Current.CancellationToken);

        await host.OuDataService.DeleteEntityAsync(created.Id, CancellationToken.None);

        Assert.Null(await host.OuDataService.GetByIdAsync(created.Id, CancellationToken.None));
        Assert.Empty(await host.OuDataService.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UserAssociation_QueriesCorrectly()
    {
        using var host = NewHost();
        var ou = await CreateOu(host, "DE", ct: TestContext.Current.CancellationToken);

        await host.UserDataService.AddAsync(new OrganizationUnitUserEntity
        {
            OrganizationUnitId = ou.Id,
            UserId = "u_001"
        }, CancellationToken.None);

        var viaDataService = await host.UserDataService.EntityGetAsync(
            j => j.OrganizationUnitId == ou.Id && j.UserId == "u_001", CancellationToken.None);
        Assert.NotNull(viaDataService);

        var userIds = await host.UserDataService.GetUserIdsByOrganizationUnitIdsAsync(
            new List<long> { ou.Id }, CancellationToken.None);
        Assert.Equal(new[] { "u_001" }, userIds);

        var ouIds = await host.UserDataService.GetOrganizationUnitIdsForUserAsync("u_001", CancellationToken.None);
        Assert.Equal(new[] { ou.Id }, ouIds);

        Assert.Equal(1, await host.UserDataService.CountByOrganizationUnitIdAsync(ou.Id, CancellationToken.None));

        await host.UserDataService.RemoveAsync(ou.Id, "u_001", CancellationToken.None);
        Assert.Equal(0, await host.UserDataService.CountByOrganizationUnitIdAsync(ou.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteUsersByOrganizationUnitId_ClearsAssociation()
    {
        using var host = NewHost();
        var ou = await CreateOu(host, "DE", ct: TestContext.Current.CancellationToken);
        await host.UserDataService.AddAsync(new OrganizationUnitUserEntity { OrganizationUnitId = ou.Id, UserId = "u_001" }, CancellationToken.None);
        await host.UserDataService.AddAsync(new OrganizationUnitUserEntity { OrganizationUnitId = ou.Id, UserId = "u_002" }, CancellationToken.None);

        await host.UserDataService.DeleteByOrganizationUnitIdsAsync(new List<long> { ou.Id }, CancellationToken.None);

        Assert.Equal(0, await host.UserDataService.CountByOrganizationUnitIdAsync(ou.Id, CancellationToken.None));
        Assert.Empty(await host.UserDataService.GetUserIdsByOrganizationUnitIdsAsync(new List<long> { ou.Id }, CancellationToken.None));
    }

    [Fact]
    public async Task GetByIds_Batch_OrderIndependent()
    {
        using var host = NewHost();
        var a = await CreateOu(host, "A", ct: TestContext.Current.CancellationToken);
        var b = await CreateOu(host, "B", ct: TestContext.Current.CancellationToken);
        var c = await CreateOu(host, "C", ct: TestContext.Current.CancellationToken);

        var result = await host.OuDataService.GetByIdsAsync(
            new List<long> { b.Id, a.Id, c.Id }, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, e => e.Id == a.Id);
        Assert.Contains(result, e => e.Id == b.Id);
        Assert.Contains(result, e => e.Id == c.Id);
    }
}
