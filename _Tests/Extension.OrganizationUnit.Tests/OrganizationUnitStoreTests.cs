using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.OrganizationUnit;

namespace TKWF.Ext.OrganizationUnit.Tests;

/// <summary>
/// IOrganizationUnitStore 委托正确性测试——经 Store 写入 → 经 DataService 可查（红线合规：Store 仅委托 SG1 DataService，不触碰 ORM/IEntityDAC）。
/// </summary>
public class OrganizationUnitStoreTests
{
    private static OrganizationUnitTestHost NewHost() => OrganizationUnitTestHost.Create();

    /// <summary>经 Store 直接创建根 OU（Store 层委托 DataService 的写路径）。</summary>
    private static async Task<OrganizationUnitEntity> CreateViaStore(IOrganizationUnitStore store, string code, CancellationToken ct = default)
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
        await store.CreateAsync(entity, ct);
        return entity;
    }

    [Fact]
    public async Task StoreCreate_DelegatesToDataService_AndGetByIdReturns()
    {
        using var host = NewHost();

        var created = await CreateViaStore(host.Store, "HQ");

        Assert.True(created.Id > 0);
        // 经 DataService 直接查询能读到（委托转发正确）
        var viaDataService = await host.OuDataService.EntityGetAsync(e => e.Id == created.Id, CancellationToken.None);
        Assert.NotNull(viaDataService);
        Assert.Equal("HQ", viaDataService!.Code);

        // GetById 转发正确
        var byId = await host.Store.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.NotNull(byId);
        Assert.Equal(created.Id, byId!.Id);
    }

    [Fact]
    public async Task GetByCode_ForwardsToDataService()
    {
        using var host = NewHost();
        var created = await CreateViaStore(host.Store, "FIN");

        var byCode = await host.Store.GetByCodeAsync("FIN", CancellationToken.None);

        Assert.NotNull(byCode);
        Assert.Equal(created.Id, byCode!.Id);
        Assert.Equal("FIN", byCode.Code);

        Assert.Null(await host.Store.GetByCodeAsync("NOT_EXISTS", CancellationToken.None));
    }

    [Fact]
    public async Task GetAll_ReturnsAllRows()
    {
        using var host = NewHost();
        await CreateViaStore(host.Store, "A");
        await CreateViaStore(host.Store, "B");
        await CreateViaStore(host.Store, "C");

        var all = await host.Store.GetAllAsync(CancellationToken.None);

        Assert.Equal(3, all.Count);
        Assert.Contains(all, e => e.Code == "A");
        Assert.Contains(all, e => e.Code == "B");
        Assert.Contains(all, e => e.Code == "C");
    }

    [Fact]
    public async Task Update_PersistsChanges()
    {
        using var host = NewHost();
        var created = await CreateViaStore(host.Store, "HQ");

        created.Name = "总部（更新）";
        created.IsEnabled = false;
        await host.Store.UpdateAsync(created, CancellationToken.None);

        var reloaded = await host.Store.GetByIdAsync(created.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal("总部（更新）", reloaded!.Name);
        Assert.False(reloaded.IsEnabled);
    }

    [Fact]
    public async Task Delete_RemovesRow()
    {
        using var host = NewHost();
        var created = await CreateViaStore(host.Store, "HQ");

        await host.Store.DeleteAsync(created.Id, CancellationToken.None);

        Assert.Null(await host.Store.GetByIdAsync(created.Id, CancellationToken.None));
        Assert.Empty(await host.Store.GetAllAsync(CancellationToken.None));
    }

    [Fact]
    public async Task UserAssociation_DelegatesToDataService_AndQueriesCorrectly()
    {
        using var host = NewHost();
        var ou = await CreateViaStore(host.Store, "DE");

        await host.Store.AddUserAsync(new OrganizationUnitUserEntity
        {
            OrganizationUnitId = ou.Id,
            UserId = "u_001"
        }, CancellationToken.None);

        // 经 DataService 直查 junction 能读到（委托转发正确）
        var viaDataService = await host.UserDataService.EntityGetAsync(
            j => j.OrganizationUnitId == ou.Id && j.UserId == "u_001", CancellationToken.None);
        Assert.NotNull(viaDataService);

        // 按 OU 集合查询用户
        var userIds = await host.Store.GetUserIdsByOrganizationUnitIdsAsync(new List<long> { ou.Id }, CancellationToken.None);
        Assert.Equal(new[] { "u_001" }, userIds);

        // 按用户查询 OU
        var ouIds = await host.Store.GetOrganizationUnitIdsForUserAsync("u_001", CancellationToken.None);
        Assert.Equal(new[] { ou.Id }, ouIds);

        // 计数
        Assert.Equal(1, await host.Store.CountUsersByOrganizationUnitIdAsync(ou.Id, CancellationToken.None));

        // RemoveUser 解绑
        await host.Store.RemoveUserAsync(ou.Id, "u_001", CancellationToken.None);
        Assert.Equal(0, await host.Store.CountUsersByOrganizationUnitIdAsync(ou.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteUsersByOrganizationUnitId_ClearsAssociation()
    {
        using var host = NewHost();
        var ou = await CreateViaStore(host.Store, "DE");
        await host.Store.AddUserAsync(new OrganizationUnitUserEntity { OrganizationUnitId = ou.Id, UserId = "u_001" }, CancellationToken.None);
        await host.Store.AddUserAsync(new OrganizationUnitUserEntity { OrganizationUnitId = ou.Id, UserId = "u_002" }, CancellationToken.None);

        await host.Store.DeleteUsersByOrganizationUnitIdsAsync(new List<long> { ou.Id }, CancellationToken.None);

        Assert.Equal(0, await host.Store.CountUsersByOrganizationUnitIdAsync(ou.Id, CancellationToken.None));
        Assert.Empty(await host.Store.GetUserIdsByOrganizationUnitIdsAsync(new List<long> { ou.Id }, CancellationToken.None));
    }

    [Fact]
    public async Task GetByIds_Batch_OrderIndependent()
    {
        using var host = NewHost();
        var a = await CreateViaStore(host.Store, "A");
        var b = await CreateViaStore(host.Store, "B");
        var c = await CreateViaStore(host.Store, "C");

        // 批量按 Id 查询：入参顺序无关，三行全回
        var result = await host.OuDataService.GetByIdsAsync(
            new List<long> { b.Id, a.Id, c.Id }, CancellationToken.None);

        Assert.Equal(3, result.Count);
        Assert.Contains(result, e => e.Id == a.Id);
        Assert.Contains(result, e => e.Id == b.Id);
        Assert.Contains(result, e => e.Id == c.Id);
    }
}