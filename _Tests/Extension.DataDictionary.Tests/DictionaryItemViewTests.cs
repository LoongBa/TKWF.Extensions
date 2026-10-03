using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// vw_DictionaryItemView 视图 JOIN 测试（V0.2.0 N4，V0.3.0 领域自治根治重写——走生产路径）——
/// DefinitionCode 单查询返回项 + 定义列（两步骤一）。
/// </summary>
public class DictionaryItemViewTests
{
    /// <summary>同步建宿主 + AOP 解析 Store（BindScope 必须发生在测试自身上下文，禁 async helper）。</summary>
    private static (IDictionaryStore Store, DomainUser<TestUserInfo> User) CreateStore()
    {
        var (provider, user) = DataDictionaryTestHost.CreateProductionHost();
        return (user.Use<IDictionaryStore>(), user);
    }

    private static async Task SeedGender(IDictionaryStore store)
    {
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
        var (store, user) = CreateStore();
        await SeedGender(store);

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
        var (store, user) = CreateStore();
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
        var (store, user) = CreateStore();

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

