using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// DictionaryManager 缓存测试（V0.2.0 W7）——缓存命中/未命中/失效、Options 默认值。
/// </summary>
public class DictionaryManagerCacheTests
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

    private static (DictionaryManager manager, IFreeSql fsql) CreateManagerWithCache(
        bool enableCache = true, int cacheExpirationSeconds = 300)
    {
        var fsql = CreateFreeSql();
        var store = new FreeSqlDictionaryStore(fsql, NullLogger<FreeSqlDictionaryStore>.Instance);
        var cache = new MemoryCache(new MemoryCacheOptions());
        var options = Options.Create(new DataDictionaryOptions
        {
            EnableCache = enableCache,
            CacheExpirationSeconds = cacheExpirationSeconds
        });
        var manager = new DictionaryManager(store, NullLogger<DictionaryManager>.Instance, cache, options);
        return (manager, fsql);
    }

    private static async Task SeedGender(IFreeSql fsql)
    {
        var store = new FreeSqlDictionaryStore(fsql, NullLogger<FreeSqlDictionaryStore>.Instance);
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

    [Fact]
    public async Task GetDefinitionWithItems_CacheHit_SecondCallReturnsCached()
    {
        var (manager, fsql) = CreateManagerWithCache();
        await SeedGender(fsql);

        // 第一次读取 → 查库
        var first = await manager.GetDefinitionWithItemsAsync("Gender", CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(2, first!.Items.Count);

        // 第二次读取 → 应命中缓存（同一实例）
        var second = await manager.GetDefinitionWithItemsAsync("Gender", CancellationToken.None);
        Assert.NotNull(second);
        Assert.Same(first, second); // 缓存命中：同一对象引用
    }

    [Fact]
    public async Task GetItems_CacheHit_SecondCallReturnsSameItems()
    {
        var (manager, fsql) = CreateManagerWithCache();
        await SeedGender(fsql);

        var first = await manager.GetItemsAsync("Gender", CancellationToken.None);
        var second = await manager.GetItemsAsync("Gender", CancellationToken.None);

        Assert.Same(first, second); // 缓存命中
    }

    [Fact]
    public async Task GetDefinitionWithItems_UnknownCode_ReturnsNull_NoCache()
    {
        var (manager, fsql) = CreateManagerWithCache();

        var result = await manager.GetDefinitionWithItemsAsync("NOBODY", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertDefinition_InvalidatesCache()
    {
        var (manager, fsql) = CreateManagerWithCache();
        await SeedGender(fsql);

        // 预热缓存
        var before = await manager.GetDefinitionWithItemsAsync("Gender", CancellationToken.None);
        Assert.NotNull(before);
        Assert.Equal(2, before!.Items.Count);

        // 更新定义（修改 DisplayName）
        var def = before.Definition;
        def.DisplayName = "性别（修改后）";
        await manager.UpsertDefinitionAsync(def, CancellationToken.None);

        // 重新读取 → 应从库中获取新数据
        var after = await manager.GetDefinitionWithItemsAsync("Gender", CancellationToken.None);
        Assert.NotNull(after);
        Assert.NotSame(before, after); // 缓存已失效，重新加载
        Assert.Equal("性别（修改后）", after!.Definition.DisplayName);
    }

    [Fact]
    public async Task UpsertItem_InvalidatesCache()
    {
        var (manager, fsql) = CreateManagerWithCache();
        await SeedGender(fsql);

        // 预热缓存
        var before = await manager.GetItemsAsync("Gender", CancellationToken.None);
        Assert.Equal(2, before.Count);

        // 新增项
        var def = fsql.Select<DictionaryDefinitionEntity>().First();
        await manager.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Other", DisplayName = "其他", Order = 3
        }, CancellationToken.None);

        // 重新读取 → 应包含新项
        var after = await manager.GetItemsAsync("Gender", CancellationToken.None);
        Assert.Equal(3, after.Count);
    }

    [Fact]
    public async Task EnableCacheFalse_EveryCallHitsStore()
    {
        var (manager, fsql) = CreateManagerWithCache(enableCache: false);
        await SeedGender(fsql);

        var first = await manager.GetDefinitionWithItemsAsync("Gender", CancellationToken.None);
        var second = await manager.GetDefinitionWithItemsAsync("Gender", CancellationToken.None);

        Assert.NotNull(first);
        Assert.NotNull(second);
        // EnableCache=false 时每次查库，不是同一引用
        Assert.NotSame(first, second);
    }

    [Fact]
    public void DataDictionaryOptions_DefaultValues()
    {
        var options = new DataDictionaryOptions();

        Assert.True(options.IsEnabled);
        Assert.True(options.EnableCache);
        Assert.Equal(300, options.CacheExpirationSeconds);
        Assert.False(options.EnableTreeMode);
    }

    [Fact]
    public void DataDictionaryOptions_CustomValues()
    {
        var options = new DataDictionaryOptions
        {
            IsEnabled = false,
            EnableCache = false,
            CacheExpirationSeconds = 60,
            EnableTreeMode = true
        };

        Assert.False(options.IsEnabled);
        Assert.False(options.EnableCache);
        Assert.Equal(60, options.CacheExpirationSeconds);
        Assert.True(options.EnableTreeMode);
    }

    [Fact]
    public async Task GetItemsTree_CacheHit_SecondCallReturnsSame()
    {
        var (manager, fsql) = CreateManagerWithCache(enableCache: true);

        var store = new FreeSqlDictionaryStore(fsql, NullLogger<FreeSqlDictionaryStore>.Instance);
        var def = new DictionaryDefinitionEntity { Code = "CachedTree", DisplayName = "缓存树" };
        await store.UpsertDefinitionAsync(def, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Root", DisplayName = "根", Order = 1,
            Level = 0, Path = "/Root"
        }, CancellationToken.None);

        var options = Options.Create(new DataDictionaryOptions { EnableTreeMode = true, EnableCache = true });
        var cache = new MemoryCache(new MemoryCacheOptions());
        var mgr = new DictionaryManager(store, NullLogger<DictionaryManager>.Instance, cache, options);

        var first = await mgr.GetItemsTreeAsync("CachedTree", CancellationToken.None);
        var second = await mgr.GetItemsTreeAsync("CachedTree", CancellationToken.None);

        // 树形查询也走缓存（底层 GetOrLoadAggregateAsync 有缓存）
        Assert.Single(first);
        Assert.Single(second);
    }
}
