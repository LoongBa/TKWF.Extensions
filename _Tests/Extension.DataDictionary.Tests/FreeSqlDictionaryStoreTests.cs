using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// FreeSqlDictionaryStore 测试——定义/项 CRUD、按编码查询、Upsert 幂等、异常静默。
/// </summary>
public class FreeSqlDictionaryStoreTests
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

    private static FreeSqlDictionaryStore CreateStore(IFreeSql fsql)
        => new(fsql, NullLogger<FreeSqlDictionaryStore>.Instance);

    private static DictionaryDefinitionEntity NewDefinition(string code = "Gender")
        => new() { Code = code, DisplayName = code };

    private static DictionaryItemEntity NewItem(long definitionId, string code, int order = 1)
        => new() { DefinitionId = definitionId, Code = code, DisplayName = code, Order = order };

    [Fact]
    public async Task UpsertDefinition_New_SetsId()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var def = NewDefinition();

        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        Assert.True(def.Id > 0);
        Assert.Equal(1, fsql.Select<DictionaryDefinitionEntity>().Count());
    }

    [Fact]
    public async Task UpsertDefinition_Existing_Updates()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var def = NewDefinition();
        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        var updated = NewDefinition();
        updated.DisplayName = "性别（更新）";
        await store.UpsertDefinitionAsync(updated, CancellationToken.None);

        Assert.Equal(1, fsql.Select<DictionaryDefinitionEntity>().Count());
        var saved = fsql.Select<DictionaryDefinitionEntity>().Where(d => d.Code == "Gender").First();
        Assert.Equal("性别（更新）", saved.DisplayName);
    }

    [Fact]
    public async Task GetDefinitionByCode_ReturnsDefinition()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var def = NewDefinition("OrderStatus");
        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        var result = await store.GetDefinitionByCodeAsync("OrderStatus", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(def.Id, result!.Id);
    }

    [Fact]
    public async Task GetDefinitionByCode_NotExists_ReturnsNull()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        var result = await store.GetDefinitionByCodeAsync("NOBODY", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDefinitions_ReturnsPaged()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        for (var i = 0; i < 3; i++)
            await store.UpsertDefinitionAsync(NewDefinition($"Def{i}"), CancellationToken.None);

        var list = await store.GetDefinitionsAsync(0, 2, CancellationToken.None);

        Assert.Equal(2, list.Count);
        Assert.Equal(3, fsql.Select<DictionaryDefinitionEntity>().Count());
    }

    [Fact]
    public async Task UpsertItem_NewAndUpdate_ByIdentity()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var def = NewDefinition();
        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        var item = NewItem(def.Id, "Male", 1);
        await store.UpsertItemAsync(item, CancellationToken.None);
        Assert.True(item.Id > 0);

        var item2 = NewItem(def.Id, "Male", 2);
        item2.DisplayName = "男（更新）";
        await store.UpsertItemAsync(item2, CancellationToken.None);

        Assert.Equal(1, fsql.Select<DictionaryItemEntity>().Count());
        var saved = fsql.Select<DictionaryItemEntity>().Where(i => i.Code == "Male").First();
        Assert.Equal("男（更新）", saved.DisplayName);
        Assert.Equal(2, saved.Order); // 更新生效
    }

    [Fact]
    public async Task GetItems_SortedByOrder_ExcludesDisabled()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var def = NewDefinition();
        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        await store.UpsertItemAsync(NewItem(def.Id, "C", 3), CancellationToken.None);
        await store.UpsertItemAsync(NewItem(def.Id, "A", 1), CancellationToken.None);
        var disabled = NewItem(def.Id, "B", 2);
        disabled.IsEnabled = false;
        await store.UpsertItemAsync(disabled, CancellationToken.None);

        var items = await store.GetItemsAsync(def.Id, CancellationToken.None);

        Assert.Equal(2, items.Count); // B 已禁用被排除
        Assert.Equal(new[] { "A", "C" }, items.Select(i => i.Code).ToArray()); // 按 Order 排序
    }

    [Fact]
    public async Task DeleteDefinition_CascadesItems()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var def = NewDefinition();
        await store.UpsertDefinitionAsync(def, CancellationToken.None);
        await store.UpsertItemAsync(NewItem(def.Id, "Male", 1), CancellationToken.None);

        await store.DeleteDefinitionAsync(def.Id, CancellationToken.None);

        Assert.Equal(0, fsql.Select<DictionaryDefinitionEntity>().Count());
        Assert.Equal(0, fsql.Select<DictionaryItemEntity>().Count());
    }

    [Fact]
    public async Task DeleteItem_Removes()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);
        var def = NewDefinition();
        await store.UpsertDefinitionAsync(def, CancellationToken.None);
        var item = NewItem(def.Id, "Male", 1);
        await store.UpsertItemAsync(item, CancellationToken.None);

        await store.DeleteItemAsync(item.Id, CancellationToken.None);

        Assert.Equal(0, fsql.Select<DictionaryItemEntity>().Count());
    }

    [Fact]
    public async Task Operations_FailSilently_OnNullEntity()
    {
        var fsql = CreateFreeSql();
        var store = CreateStore(fsql);

        await store.UpsertDefinitionAsync(null!, CancellationToken.None);
        await store.UpsertItemAsync(null!, CancellationToken.None);

        Assert.Equal(0, fsql.Select<DictionaryDefinitionEntity>().Count());
        Assert.Equal(0, fsql.Select<DictionaryItemEntity>().Count());
    }
}