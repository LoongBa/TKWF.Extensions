using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// DictionaryTreeNode 树形组装测试（V0.2.0 W7）——正常层级、空父节点归根、平级无 Children、降级行为。
/// </summary>
public class DictionaryTreeTests
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

    private static DictionaryManager CreateManager(IFreeSql fsql, bool enableTreeMode = true)
    {
        var store = new FreeSqlDictionaryStore(fsql, NullLogger<FreeSqlDictionaryStore>.Instance);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new DataDictionaryOptions { EnableTreeMode = enableTreeMode });
        return new DictionaryManager(store, NullLogger<DictionaryManager>.Instance, cache, options);
    }

    /// <summary>种子：省市区三级树形数据。</summary>
    private static async Task SeedRegionTree(IFreeSql fsql)
    {
        var store = new FreeSqlDictionaryStore(fsql, NullLogger<FreeSqlDictionaryStore>.Instance);
        var def = new DictionaryDefinitionEntity { Code = "Region", DisplayName = "地区" };
        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        // 省
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Guangdong", DisplayName = "广东", Order = 1,
            Level = 0, Path = "/Guangdong"
        }, CancellationToken.None);

        // 市（广东下）
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Shenzhen", DisplayName = "深圳", Order = 1,
            ParentCode = "Guangdong", Level = 1, Path = "/Guangdong/Shenzhen"
        }, CancellationToken.None);

        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Guangzhou", DisplayName = "广州", Order = 2,
            ParentCode = "Guangdong", Level = 1, Path = "/Guangdong/Guangzhou"
        }, CancellationToken.None);

        // 区（深圳下）
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Nanshan", DisplayName = "南山", Order = 1,
            ParentCode = "Shenzhen", Level = 2, Path = "/Guangdong/Shenzhen/Nanshan"
        }, CancellationToken.None);

        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Futian", DisplayName = "福田", Order = 2,
            ParentCode = "Shenzhen", Level = 2, Path = "/Guangdong/Shenzhen/Futian"
        }, CancellationToken.None);
    }

    [Fact]
    public async Task GetItemsTree_ThreeLevelHierarchy_ReturnsNestedTree()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);
        await SeedRegionTree(fsql);

        var tree = await manager.GetItemsTreeAsync("Region", CancellationToken.None);

        Assert.Single(tree); // 只有一个根节点：广东
        var guangdong = tree[0];
        Assert.Equal("Guangdong", guangdong.Code);
        Assert.Equal("广东", guangdong.DisplayName);
        Assert.Equal(2, guangdong.Children.Count); // 深圳、广州

        var shenzhen = guangdong.Children.First(c => c.Code == "Shenzhen");
        Assert.Equal(2, shenzhen.Children.Count); // 南山、福田
        Assert.Equal("Nanshan", shenzhen.Children[0].Code);
        Assert.Equal("Futian", shenzhen.Children[1].Code);

        var guangzhou = guangdong.Children.First(c => c.Code == "Guangzhou");
        Assert.Empty(guangzhou.Children); // 广州无子节点
    }

    [Fact]
    public async Task GetItemsTree_NullParentCode_GoesToRoot()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);

        var store = new FreeSqlDictionaryStore(fsql, NullLogger<FreeSqlDictionaryStore>.Instance);
        var def = new DictionaryDefinitionEntity { Code = "Flat", DisplayName = "平级" };
        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        // 三个平级项，ParentCode 都为 null
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "A", DisplayName = "A", Order = 1
        }, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "B", DisplayName = "B", Order = 2
        }, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "C", DisplayName = "C", Order = 3
        }, CancellationToken.None);

        var tree = await manager.GetItemsTreeAsync("Flat", CancellationToken.None);

        Assert.Equal(3, tree.Count);
        Assert.All(tree, node => Assert.Empty(node.Children));
        Assert.Equal(new[] { "A", "B", "C" }, tree.Select(n => n.Code).ToArray());
    }

    [Fact]
    public async Task GetItemsTree_SiblingItems_NoChildren()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);

        var store = new FreeSqlDictionaryStore(fsql, NullLogger<FreeSqlDictionaryStore>.Instance);
        var def = new DictionaryDefinitionEntity { Code = "Siblings", DisplayName = "兄弟" };
        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        // Parent 相同的两个兄弟节点
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Parent", DisplayName = "父", Order = 1,
            Level = 0, Path = "/Parent"
        }, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Child1", DisplayName = "子1", Order = 1,
            ParentCode = "Parent", Level = 1, Path = "/Parent/Child1"
        }, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Child2", DisplayName = "子2", Order = 2,
            ParentCode = "Parent", Level = 1, Path = "/Parent/Child2"
        }, CancellationToken.None);

        var tree = await manager.GetItemsTreeAsync("Siblings", CancellationToken.None);

        Assert.Single(tree);
        Assert.Equal(2, tree[0].Children.Count);
        Assert.All(tree[0].Children, c => Assert.Empty(c.Children));
    }

    [Fact]
    public async Task GetItemsTree_UnknownCode_ReturnsEmpty()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);

        var tree = await manager.GetItemsTreeAsync("NOBODY", CancellationToken.None);

        Assert.Empty(tree);
    }

    [Fact]
    public async Task GetItemsTree_EnableTreeModeFalse_ReturnsFlatList()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql, enableTreeMode: false);
        await SeedRegionTree(fsql);

        var tree = await manager.GetItemsTreeAsync("Region", CancellationToken.None);

        // D7: 降级为平铺列表，所有 Children 为空
        Assert.Equal(5, tree.Count); // 5 个项全部平铺
        Assert.All(tree, node => Assert.Empty(node.Children));
    }

    [Fact]
    public async Task GetItemsTree_EmptyDictionary_ReturnsEmpty()
    {
        var fsql = CreateFreeSql();
        var manager = CreateManager(fsql);

        var store = new FreeSqlDictionaryStore(fsql, NullLogger<FreeSqlDictionaryStore>.Instance);
        await store.UpsertDefinitionAsync(new DictionaryDefinitionEntity
        {
            Code = "Empty", DisplayName = "空"
        }, CancellationToken.None);

        var tree = await manager.GetItemsTreeAsync("Empty", CancellationToken.None);

        Assert.Empty(tree);
    }
}
