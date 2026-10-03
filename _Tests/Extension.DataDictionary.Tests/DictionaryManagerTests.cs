using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// DictionaryManager 测试（V0.3.0 领域自治根治重写——走生产路径）——
/// 按编码聚合查询、项排序、Upsert 委托、异常静默。
/// <para>生产路径：真实 DI（同步建宿主——BindScope 绑定当前测试上下文）+ <see cref="DomainUser{TUserInfo}"/>
/// → <c>User.Use&lt;IDictionaryManager&gt;()</c> AOP 路径解析（AddConstructibleService 接口守卫工厂 →
/// DictionaryManager → 懒加载 IDictionaryStore → DataService 全链真实 DI）。</para>
/// </summary>
public class DictionaryManagerTests
{
    /// <summary>同步建宿主 + AOP 解析 Manager（BindScope 必须发生在测试自身上下文，禁 async helper）。</summary>
    private static (IDictionaryManager Manager, DomainUser<TestUserInfo> User) CreateManager()
    {
        var (provider, user) = DataDictionaryTestHost.CreateProductionHost();
        return (user.Use<IDictionaryManager>(), user);
    }

    private static async Task SeedGender(IDictionaryManager manager)
    {
        var def = new DictionaryDefinitionEntity { Code = "Gender", DisplayName = "性别" };
        await manager.UpsertDefinitionAsync(def, CancellationToken.None);
        await manager.UpsertItemAsync(new DictionaryItemEntity { DefinitionId = def.Id, Code = "Female", DisplayName = "女", Order = 2 }, CancellationToken.None);
        await manager.UpsertItemAsync(new DictionaryItemEntity { DefinitionId = def.Id, Code = "Male", DisplayName = "男", Order = 1 }, CancellationToken.None);
    }

    [Fact]
    public async Task GetDefinitionByCode_ReturnsDefinition()
    {
        var (manager, user) = CreateManager();
        await SeedGender(manager);

        var def = await manager.GetDefinitionByCodeAsync("Gender", CancellationToken.None);

        Assert.NotNull(def);
        Assert.Equal("Gender", def!.Code);
    }

    [Fact]
    public async Task GetItems_ByCode_ReturnsSortedItems()
    {
        var (manager, user) = CreateManager();
        await SeedGender(manager);

        var items = await manager.GetItemsAsync("Gender", CancellationToken.None);

        Assert.Equal(2, items.Count);
        Assert.Equal(new[] { "Male", "Female" }, items.Select(i => i.Code).ToArray()); // 按 Order
    }

    [Fact]
    public async Task GetItems_UnknownCode_ReturnsEmpty()
    {
        var (manager, user) = CreateManager();

        var items = await manager.GetItemsAsync("NOBODY", CancellationToken.None);

        Assert.Empty(items);
    }

    [Fact]
    public async Task GetDefinitionWithItems_ReturnsAggregate()
    {
        var (manager, user) = CreateManager();
        await SeedGender(manager);

        var result = await manager.GetDefinitionWithItemsAsync("Gender", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal("Gender", result!.Definition.Code);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetDefinitionWithItems_UnknownCode_ReturnsNull()
    {
        var (manager, user) = CreateManager();

        var result = await manager.GetDefinitionWithItemsAsync("NOBODY", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task UpsertDefinition_DelegatesToStore()
    {
        var (manager, user) = CreateManager();
        var def = new DictionaryDefinitionEntity { Code = "Color", DisplayName = "颜色" };

        await manager.UpsertDefinitionAsync(def, CancellationToken.None);

        Assert.True(def.Id > 0);
        var saved = await manager.GetDefinitionByCodeAsync("Color", CancellationToken.None);
        Assert.NotNull(saved);
    }

    [Fact]
    public async Task UpsertItem_DelegatesToStore()
    {
        var (manager, user) = CreateManager();
        await SeedGender(manager);
        var def = await manager.GetDefinitionByCodeAsync("Gender", CancellationToken.None);
        Assert.NotNull(def); // 播种定义经 manager 业务方法读取
        var item = new DictionaryItemEntity { DefinitionId = def!.Id, Code = "Other", DisplayName = "其他", Order = 3 };

        await manager.UpsertItemAsync(item, CancellationToken.None);

        Assert.True(item.Id > 0);
        var items = await manager.GetItemsAsync("Gender", CancellationToken.None);
        Assert.Equal(3, items.Count);
    }

    [Fact]
    public async Task Upsert_DelegatesFailSilently_OnNull()
    {
        var (manager, user) = CreateManager();

        await manager.UpsertDefinitionAsync(null!, CancellationToken.None);
        await manager.UpsertItemAsync(null!, CancellationToken.None);

        Assert.Null(await manager.GetDefinitionByCodeAsync("Color", CancellationToken.None));
        Assert.Empty(await manager.GetItemsAsync("Color", CancellationToken.None));
    }
}
