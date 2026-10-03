using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// DictionaryManager 缓存测试（V0.2.0 W7，V0.3.0 领域自治根治重写——走生产路径）——
/// 缓存命中/未命中/失效、Options 默认值、树形缓存、VEntity 缓存路径回归（N5/N6）。
/// <para>生产路径：真实 DI + <see cref="DomainUser{TUserInfo}"/> BindScope → <c>User.Use&lt;IDictionaryManager&gt;()</c>；
/// Options 按测试经 <c>Configure&lt;DataDictionaryOptions&gt;</c> 覆盖（AddConstructibleService 守卫工厂
/// ActivatorUtilities 解析 IOptions——AddOptions 默认值兜底）。</para>
/// </summary>
public class DictionaryManagerCacheTests
{
    /// <summary>同步建宿主 + AOP 解析 Manager（BindScope 必须发生在测试自身上下文，禁 async helper）。</summary>
    private static (IDictionaryManager Manager, DomainUser<TestUserInfo> User) CreateManager(
        Action<DataDictionaryOptions>? configure = null)
    {
        var (provider, user) = DataDictionaryTestHost.CreateProductionHost(configure);
        return (user.Use<IDictionaryManager>(), user);
    }

    private static async Task SeedGender(IDictionaryManager manager)
    {
        var def = new DictionaryDefinitionEntity { Code = "Gender", DisplayName = "性别" };
        await manager.UpsertDefinitionAsync(def, CancellationToken.None);
        await manager.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Male", DisplayName = "男", Order = 1
        }, CancellationToken.None);
        await manager.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Female", DisplayName = "女", Order = 2
        }, CancellationToken.None);
    }

    /// <summary>经同一宿主 user 的 IDictionaryStore 生产路径播种（与 manager 共享 fsql——数据落库同源）。</summary>
    private static async Task SeedViaStoreAsync(DomainUser<TestUserInfo> user)
    {
        var store = user.Use<IDictionaryStore>();
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
        var (manager, user) = CreateManager();
        await SeedGender(manager);

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
        var (manager, user) = CreateManager();
        await SeedGender(manager);

        var first = await manager.GetItemsAsync("Gender", CancellationToken.None);
        var second = await manager.GetItemsAsync("Gender", CancellationToken.None);

        Assert.Same(first, second); // 缓存命中
    }

    [Fact]
    public async Task GetDefinitionWithItems_UnknownCode_ReturnsNull_NoCache()
    {
        var (manager, user) = CreateManager();

        var result = await manager.GetDefinitionWithItemsAsync("NOBODY", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertDefinition_InvalidatesCache()
    {
        var (manager, user) = CreateManager();
        await SeedGender(manager);

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
        var (manager, user) = CreateManager();
        await SeedGender(manager);

        // 预热缓存
        var before = await manager.GetItemsAsync("Gender", CancellationToken.None);
        Assert.Equal(2, before.Count);

        // 新增项
        var def = await manager.GetDefinitionByCodeAsync("Gender", CancellationToken.None);
        Assert.NotNull(def); // 播种定义经 manager 业务方法读取
        await manager.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def!.Id, Code = "Other", DisplayName = "其他", Order = 3
        }, CancellationToken.None);

        // 重新读取 → 应包含新项
        var after = await manager.GetItemsAsync("Gender", CancellationToken.None);
        Assert.Equal(3, after.Count);
    }

    [Fact]
    public async Task EnableCacheFalse_EveryCallHitsStore()
    {
        var (manager, user) = CreateManager(o => o.EnableCache = false);
        await SeedGender(manager);

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
        var (provider, user) = DataDictionaryTestHost.CreateProductionHost(o => { o.EnableTreeMode = true; o.EnableCache = true; });
        var store = user.Use<IDictionaryStore>();
        var def = new DictionaryDefinitionEntity { Code = "CachedTree", DisplayName = "缓存树" };
        await store.UpsertDefinitionAsync(def, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Root", DisplayName = "根", Order = 1,
            Level = 0, Path = "/Root"
        }, CancellationToken.None);

        var manager = user.Use<IDictionaryManager>();

        var first = await manager.GetItemsTreeAsync("CachedTree", CancellationToken.None);
        var second = await manager.GetItemsTreeAsync("CachedTree", CancellationToken.None);

        // 树形查询也走缓存（底层 GetOrLoadAggregateAsync 有缓存）
        Assert.Single(first);
        Assert.Single(second);
    }

    /// <summary>
    /// N5：缓存路径回归（V0.2.0 VEntity）——未命中走视图单查询 → 实体映射聚合 → BuildTree → 写缓存 → 二次命中。
    /// </summary>
    [Fact]
    public async Task GetOrLoadAggregate_ViewPath_BuildTreeThenCache_SecondCallReturnsCached()
    {
        var (provider, user) = DataDictionaryTestHost.CreateProductionHost(o => { o.EnableTreeMode = true; o.EnableCache = true; });
        var store = user.Use<IDictionaryStore>();

        var def = new DictionaryDefinitionEntity { Code = "Region", DisplayName = "地区" };
        await store.UpsertDefinitionAsync(def, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Guangdong", DisplayName = "广东", Order = 1,
            Level = 0, Path = "/Guangdong"
        }, CancellationToken.None);
        await store.UpsertItemAsync(new DictionaryItemEntity
        {
            DefinitionId = def.Id, Code = "Shenzhen", DisplayName = "深圳", Order = 1,
            ParentCode = "Guangdong", Level = 1, Path = "/Guangdong/Shenzhen"
        }, CancellationToken.None);

        var manager = user.Use<IDictionaryManager>();

        // 第一次：视图单查询 → 实体映射 → 聚合 → 写缓存
        var first = await manager.GetDefinitionWithItemsAsync("Region", CancellationToken.None);
        Assert.NotNull(first);
        Assert.Equal(2, first!.Items.Count);

        // BuildTree 在视图来源聚合上正确组装
        var tree = await manager.GetItemsTreeAsync("Region", CancellationToken.None);
        Assert.Single(tree);
        Assert.Equal("Guangdong", tree[0].Code);
        Assert.Single(tree[0].Children);
        Assert.Equal("Shenzhen", tree[0].Children[0].Code);

        // 第二次：缓存命中（同一对象引用——缓存的是聚合非查询）
        var second = await manager.GetDefinitionWithItemsAsync("Region", CancellationToken.None);
        Assert.Same(first, second);
    }

    /// <summary>
    /// N6：写路径回归（V0.2.0 VEntity）——DeleteItem 后缓存失效正确（D6：反查 DefinitionId → Code → 失效）。
    /// </summary>
    [Fact]
    public async Task DeleteItem_InvalidatesCache()
    {
        var (provider, user) = DataDictionaryTestHost.CreateProductionHost();
        var manager = user.Use<IDictionaryManager>();
        await SeedViaStoreAsync(user);

        // 预热缓存（视图路径加载）
        var before = await manager.GetItemsAsync("Gender", CancellationToken.None);
        Assert.Equal(2, before.Count);

        // 删除项 → 反查所属定义并失效缓存
        var male = before.First(i => i.Code == "Male");
        await manager.DeleteItemAsync(male.Id, CancellationToken.None);

        // 重新读取 → 缓存已失效，从库（视图路径）重新加载，不含已删项
        var after = await manager.GetItemsAsync("Gender", CancellationToken.None);
        Assert.Single(after);
        Assert.DoesNotContain(after, i => i.Code == "Male");
    }
}

