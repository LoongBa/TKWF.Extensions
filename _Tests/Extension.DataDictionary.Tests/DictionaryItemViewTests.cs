using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// vw_DictionaryItemView 视图 JOIN 测试（V0.2.0 N4）——DefinitionCode 单查询返回项 + 定义列（两步骤一）。
/// </summary>
public class DictionaryItemViewTests
{
    private static IFreeSql CreateFreeSql()
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<DictionaryDefinitionEntity>();
        fsql.CodeFirst.SyncStructure<DictionaryItemEntity>();
        return fsql;
    }

    private static async Task SeedGender(IFreeSql fsql)
    {
        var store = DataDictionaryTestHost.CreateStore(fsql);
        var def = new DictionaryDefinitionEntity { Code = "Gender", DisplayName = "性别" };
        await store.UpsertDefinitionAsync(def, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Male", DisplayName = "男", Order = 1
        }, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Female", DisplayName = "女", Order = 2
        }, CancellationToken.None);
    }

    /// <summary>N4：JOIN 查询按 DefinitionCode 返回项（含定义字段 DefinitionDisplayName）——两步骤一。</summary>
    [Fact]
    public async Task GetItemsByDefinitionCode_ReturnsItemsWithDefinitionFields()
    {
        var fsql = CreateFreeSql();
        var store = DataDictionaryTestHost.CreateStore(fsql);
        await SeedGender(fsql);

        var items = await store.GetItemsByDefinitionCodeAsync("Gender", CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Equal(new[] { "Male", "Female" }, items.Select(i => i.Code).ToArray()); // Order 升序
        Assert.All(items, i => Assert.Equal("Gender", i.DefinitionCode));
        Assert.All(items, i => Assert.Equal("性别", i.DefinitionDisplayName)); // JOIN 携带定义列
        Assert.All(items, i => Assert.True(i.IsEnabled));
    }

    /// <summary>N4：定义存在但无项 → 视图返回空列表（非 null）——"空字典"语义保留（oracle3 C-1/H1 方案 b）。</summary>
    [Fact]
    public async Task GetItemsByDefinitionCode_DefinitionExistsNoItems_ReturnsEmpty()
    {
        var fsql = CreateFreeSql();
        var store = DataDictionaryTestHost.CreateStore(fsql);
        await store.UpsertDefinitionAsync(new DictionaryDefinitionEntity
        {
            Code = "Empty", DisplayName = "空"
        }, CancellationToken.None);

        var items = await store.GetItemsByDefinitionCodeAsync("Empty", CancellationToken.None);

        Assert.NotNull(items);
        Assert.Empty(items);
    }

    /// <summary>N4：禁用项不返回——保持"聚合仅含启用项"既有可观测行为（对齐 GetItemsByDefinitionIdAsync）。</summary>
    [Fact]
    public async Task GetItemsByDefinitionCode_ExcludesDisabledItems()
    {
        var fsql = CreateFreeSql();
        var store = DataDictionaryTestHost.CreateStore(fsql);

        var def = new DictionaryDefinitionEntity { Code = "Status", DisplayName = "状态" };
        await store.UpsertDefinitionAsync(def, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Active", DisplayName = "启用", Order = 1
        }, CancellationToken.None);
        var disabled = new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Frozen", DisplayName = "冻结", Order = 2
        };
        disabled.IsEnabled = false;
        await store.UpsertItemAsync(disabled, CancellationToken.None);

        var items = await store.GetItemsByDefinitionCodeAsync("Status", CancellationToken.None);

        Assert.Single(items);
        Assert.Equal("Active", items[0].Code);
    }
}
