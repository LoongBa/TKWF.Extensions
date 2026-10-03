using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// FreeSqlDictionaryStore 测试（V0.3.0 领域自治根治重写——走生产路径）——
/// 定义/项 CRUD、按编码查询、Upsert 幂等、异常静默。
/// <para>生产路径：真实 DI（扩展 ConfigureServices + FreeSql 基础设施）+ <see cref="DomainUser{TUserInfo}"/> BindScope
/// → <c>User.Use&lt;IDictionaryStore&gt;()</c> AOP 路径解析（AddConstructibleService 接口守卫工厂）。</para>
/// </summary>
public class FreeSqlDictionaryStoreTests
{
    /// <summary>同步建宿主 + AOP 解析 Store（BindScope 必须发生在测试自身上下文，禁 async helper）。</summary>
    private static IDictionaryStore CreateStore()
    {
        var (provider, user) = DataDictionaryTestHost.CreateProductionHost();
        return user.Use<IDictionaryStore>();
    }

    private static DictionaryDefinitionEntity NewDefinition(string code = "Gender")
        => new() { Code = code, DisplayName = code };

    private static DictionaryItemEntity NewItem(long definitionId, string code, int order = 1)
        => new() { DefinitionId = definitionId, Code = code, DisplayName = code, Order = order };

    [Fact]
    public async Task UpsertDefinition_New_SetsId()
    {
        var store = CreateStore();
        var def = NewDefinition();

        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        Assert.True(def.Id > 0);
        var all = await store.GetDefinitionsAsync(0, 100, CancellationToken.None);
        Assert.Single(all);
    }

    [Fact]
    public async Task UpsertDefinition_Existing_Updates()
    {
        var store = CreateStore();
        var def = NewDefinition();
        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        var updated = NewDefinition();
        updated.DisplayName = "性别（更新）";
        await store.UpsertDefinitionAsync(updated, CancellationToken.None);

        var all = await store.GetDefinitionsAsync(0, 100, CancellationToken.None);
        Assert.Single(all);
        var saved = await store.GetDefinitionByCodeAsync("Gender", CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("性别（更新）", saved!.DisplayName);
    }

    [Fact]
    public async Task GetDefinitionByCode_ReturnsDefinition()
    {
        var store = CreateStore();
        var def = NewDefinition("OrderStatus");
        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        var result = await store.GetDefinitionByCodeAsync("OrderStatus", CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(def.Id, result!.Id);
    }

    [Fact]
    public async Task GetDefinitionByCode_NotExists_ReturnsNull()
    {
        var store = CreateStore();

        var result = await store.GetDefinitionByCodeAsync("NOBODY", CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetDefinitions_ReturnsPaged()
    {
        var store = CreateStore();
        for (var i = 0; i < 3; i++)
            await store.UpsertDefinitionAsync(NewDefinition($"Def{i}"), CancellationToken.None);

        var list = await store.GetDefinitionsAsync(0, 2, CancellationToken.None);

        Assert.Equal(2, list.Count);
        var all = await store.GetDefinitionsAsync(0, 100, CancellationToken.None);
        Assert.Equal(3, all.Count);
    }

    [Fact]
    public async Task UpsertItem_NewAndUpdate_ByIdentity()
    {
        var store = CreateStore();
        var def = NewDefinition();
        await store.UpsertDefinitionAsync(def, CancellationToken.None);

        var item = NewItem(def.Id, "Male", 1);
        await store.UpsertItemAsync(item, CancellationToken.None);
        Assert.True(item.Id > 0);

        var item2 = NewItem(def.Id, "Male", 2);
        item2.DisplayName = "男（更新）";
        await store.UpsertItemAsync(item2, CancellationToken.None);

        var items = await store.GetItemsAsync(def.Id, CancellationToken.None);
        Assert.Single(items);
        var saved = items.Single(i => i.Code == "Male");
        Assert.Equal("男（更新）", saved.DisplayName);
        Assert.Equal(2, saved.Order); // 更新生效
    }

    [Fact]
    public async Task GetItems_SortedByOrder_ExcludesDisabled()
    {
        var store = CreateStore();
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
        var store = CreateStore();
        var def = NewDefinition();
        await store.UpsertDefinitionAsync(def, CancellationToken.None);
        await store.UpsertItemAsync(NewItem(def.Id, "Male", 1), CancellationToken.None);

        await store.DeleteDefinitionAsync(def.Id, CancellationToken.None);

        var defs = await store.GetDefinitionsAsync(0, 100, CancellationToken.None);
        Assert.Empty(defs);
        var items = await store.GetItemsAsync(def.Id, CancellationToken.None);
        Assert.Empty(items);
    }

    [Fact]
    public async Task DeleteItem_Removes()
    {
        var store = CreateStore();
        var def = NewDefinition();
        await store.UpsertDefinitionAsync(def, CancellationToken.None);
        var item = NewItem(def.Id, "Male", 1);
        await store.UpsertItemAsync(item, CancellationToken.None);

        await store.DeleteItemAsync(item.Id, CancellationToken.None);

        var items = await store.GetItemsAsync(def.Id, CancellationToken.None);
        Assert.Empty(items);
    }

    [Fact]
    public async Task Operations_FailSilently_OnNullEntity()
    {
        var store = CreateStore();

        await store.UpsertDefinitionAsync(null!, CancellationToken.None);
        await store.UpsertItemAsync(null!, CancellationToken.None);

        var defs = await store.GetDefinitionsAsync(0, 100, CancellationToken.None);
        Assert.Empty(defs);
        // 无定义时按任意 Id 查项为空（业务方法表达"全表无项"）
        var items = await store.GetItemsAsync(1, CancellationToken.None);
        Assert.Empty(items);
    }
}

