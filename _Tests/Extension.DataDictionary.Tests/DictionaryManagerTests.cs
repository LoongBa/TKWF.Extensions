using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// DictionaryManager 测试——按编码聚合查询、项排序、Upsert 委托、异常静默。
/// </summary>
public class DictionaryManagerTests
{
    private static IFreeSql CreateFreeSql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<DictionaryDefinitionEntity>();
        fsql.CodeFirst.SyncStructure<DictionaryItemEntity>();
        return fsql;
    }

    private static DictionaryManager CreateManager(IFreeSql fsql)
    {
        var store = new FreeSqlDictionaryStore(fsql, NullLogger<FreeSqlDictionaryStore>.Instance);
        return new DictionaryManager(store, NullLogger<DictionaryManager>.Instance);
    }

    private static async Task SeedGender(IFreeSql fsql)
    {
        var store = new FreeSqlDictionaryStore(fsql, NullLogger<FreeSqlDictionaryStore>.Instance);
        var def = new DictionaryDefinitionEntity { Code = "Gender", DisplayName = "性别" };
        await store.UpsertDefinitionAsync(def, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity { DefinitionId = def.Id, Code = "Female", DisplayName = "女", Order = 2 }, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity { DefinitionId = def.Id, Code = "Male", DisplayName = "男", Order = 1 }, CancellationToken.None);
    }

    [Fact]
    public async Task GetDefinitionByCode_ReturnsDefinition()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        await SeedGender(fsql);

        var def = await manager.GetDefinitionByCodeAsync("Gender", CancellationToken.None);

        Assert.NotNull(def);
        Assert.Equal("Gender", def!.Code);
    }

    [Fact]
    public async Task GetItems_ByCode_ReturnsSortedItems()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        await SeedGender(fsql);

        var items = await manager.GetItemsAsync("Gender", CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Equal(new[] { "Male", "Female" }, items.Select(i => i.Code).ToArray()); // 按 Order
    }

    [Fact]
    public async Task GetItems_UnknownCode_ReturnsEmpty()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);

        var items = await manager.GetItemsAsync("NOBODY", CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public async Task GetDefinitionWithItems_ReturnsAggregate()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        await SeedGender(fsql);

        var result = await manager.GetDefinitionWithItemsAsync("Gender", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Gender", result!.Definition.Code);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetDefinitionWithItems_UnknownCode_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);

        var result = await manager.GetDefinitionWithItemsAsync("NOBODY", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertDefinition_DelegatesToStore()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        var def = new DictionaryDefinitionEntity { Code = "Color", DisplayName = "颜色" };

        await manager.UpsertDefinitionAsync(def, CancellationToken.None);

        Assert.True(def.Id > 0);
        Assert.Equal(1, fsql.Select<DictionaryDefinitionEntity>().Count());
    }

    [Fact]
    public async Task UpsertItem_DelegatesToStore()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        await SeedGender(fsql);
        var def = fsql.Select<DictionaryDefinitionEntity>().First();
        var item = new DictionaryItemEntity { DefinitionId = def.Id, Code = "Other", DisplayName = "其他", Order = 3 };

        await manager.UpsertItemAsync(item, CancellationToken.None);

        Assert.True(item.Id > 0);
        Assert.Equal(3, fsql.Select<DictionaryItemEntity>().Count());
    }

    [Fact]
    public async Task Upsert_DelegatesFailSilently_OnNull()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);

        await manager.UpsertDefinitionAsync(null!, CancellationToken.None);
        await manager.UpsertItemAsync(null!, CancellationToken.None);

        Assert.Equal(0, fsql.Select<DictionaryDefinitionEntity>().Count());
        Assert.Equal(0, fsql.Select<DictionaryItemEntity>().Count());
    }
}