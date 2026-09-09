using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// FeatureValueStore 测试——D10-D11 + D14 Store/Global 唯一性/写路径传播。
/// <para>D10：IFeatureValueStore Set/Get/Delete/GetList（internal，经 IVT 访问——扩展 csproj
/// 须含 <c>&lt;InternalsVisibleTo Include="TKWF.Ext.FeatureManagement.Tests" /&gt;</c>）。</para>
/// <para>D11：Global 层唯一性——预检 + 事务包裹 + 事务内二次校验（C4，对齐 FileManagement C2）：
/// 同 Feature 同 Global 顺序写不产生重复行（Upsert 语义）；非 Global 层 DB 唯一约束兜底。</para>
/// <para>D14：写路径异常传播——Set 失败（DB 异常 / 无效输入）→ 传播非静默（读静默、写传播契约）。</para>
/// </summary>
public class FeatureValueStoreTests
{
    private const string Theme = ConsumerFeatureContributor.StringFeature;

    private static FeatureValueEntity NewEntity(string name, string value, string providerName, string? providerKey)
        => new()
        {
            Name = name,
            Value = value,
            ProviderName = providerName,
            ProviderKey = providerKey,
            Description = null,
            IsVisibleToClients = false,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow
        };

    // ── D10 Store Set/Get/Delete/GetList ──

    [Fact]
    public async Task Store_Set_Get_RoundTrip()
    {
        using var host = FeatureManagementTestHost.Create();

        await host.Store.SetAsync(NewEntity("Store.Key", "store-value", FeatureProviders.Global, null), CancellationToken.None);

        var saved = await host.Store.GetAsync("Store.Key", FeatureProviders.Global, null, CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal("Store.Key", saved!.Name);
        Assert.Equal("store-value", saved.Value);
        Assert.Equal(FeatureProviders.Global, saved.ProviderName);
        Assert.Null(saved.ProviderKey);
    }

    [Fact]
    public async Task Store_Set_UpdateExisting_SingleRow()
    {
        using var host = FeatureManagementTestHost.Create();

        await host.Store.SetAsync(NewEntity("Store.Key", "v1", FeatureProviders.Global, null), CancellationToken.None);
        await host.Store.SetAsync(NewEntity("Store.Key", "v2", FeatureProviders.Global, null), CancellationToken.None);

        var list = await host.Store.GetListAsync(FeatureProviders.Global, null, CancellationToken.None);
        Assert.Single(list);
        Assert.Equal("v2", list[0].Value);
    }

    [Fact]
    public async Task Store_Get_NotExists_ReturnsNull()
    {
        using var host = FeatureManagementTestHost.Create();

        var result = await host.Store.GetAsync("Store.NotExist", FeatureProviders.Global, null, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Store_Delete_Removes()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Store.SetAsync(NewEntity("Store.Key", "v", FeatureProviders.Global, null), CancellationToken.None);

        await host.Store.DeleteAsync("Store.Key", FeatureProviders.Global, null, CancellationToken.None);

        Assert.Null(await host.Store.GetAsync("Store.Key", FeatureProviders.Global, null, CancellationToken.None));
    }

    [Fact]
    public async Task Store_GetList_FiltersByProvider()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Store.SetAsync(NewEntity("A", "1", FeatureProviders.Global, null), CancellationToken.None);
        await host.Store.SetAsync(NewEntity("B", "2", FeatureProviders.Global, null), CancellationToken.None);
        await host.Store.SetAsync(NewEntity("C", "3", FeatureProviders.User, "u-1"), CancellationToken.None);

        var globals = await host.Store.GetListAsync(FeatureProviders.Global, null, CancellationToken.None);
        var users = await host.Store.GetListAsync(FeatureProviders.User, "u-1", CancellationToken.None);

        Assert.Equal(2, globals.Count);
        Assert.Single(users);
        Assert.Equal("C", users[0].Name);
    }

    // ── D11 Global 层唯一性（Upsert 语义 + 非 Global 层 DB 约束兜底） ──

    [Fact]
    public async Task Manager_SetValue_Global_Twice_SingleRow()
    {
        using var host = FeatureManagementTestHost.Create();

        await host.Manager.SetValueAsync(Theme, "dark", FeatureProviders.Global, null, CancellationToken.None);
        await host.Manager.SetValueAsync(Theme, "midnight", FeatureProviders.Global, null, CancellationToken.None);

        var count = await host.Fsql.Select<FeatureValueEntity>()
            .Where(e => e.Name == Theme && e.ProviderName == FeatureProviders.Global)
            .CountAsync(CancellationToken.None);
        Assert.Equal(1, count); // 不产生重复行

        var saved = await host.Store.GetAsync(Theme, FeatureProviders.Global, null, CancellationToken.None);
        Assert.Equal("midnight", saved!.Value); // Upsert：命中更新
    }

    [Fact]
    public async Task Manager_SetValue_UserLayer_Twice_SingleRow()
    {
        using var host = FeatureManagementTestHost.Create();

        await host.Manager.SetValueAsync(Theme, "u-v1", FeatureProviders.User, "u-1", CancellationToken.None);
        await host.Manager.SetValueAsync(Theme, "u-v2", FeatureProviders.User, "u-1", CancellationToken.None);

        var count = await host.Fsql.Select<FeatureValueEntity>()
            .Where(e => e.Name == Theme && e.ProviderName == FeatureProviders.User && e.ProviderKey == "u-1")
            .CountAsync(CancellationToken.None);
        Assert.Equal(1, count);
    }

    [Fact]
    public async Task Manager_SetValue_ForcedKeyConflict_NoDuplicateRows()
    {
        // DB 唯一约束兜底（非 Global 层）：预置同键行后写——Upsert 吸收（单行）或业务异常兜底（仍单行）
        using var host = FeatureManagementTestHost.Create();
        await host.Fsql.Insert(NewEntity(Theme, "pre-inserted", FeatureProviders.User, "u-1"))
            .ExecuteAffrowsAsync(CancellationToken.None);

        try
        {
            await host.Manager.SetValueAsync(Theme, "after", FeatureProviders.User, "u-1", CancellationToken.None);
        }
        catch (Exception)
        {
            // 唯一约束冲突转业务异常路径也可接受——DB 不得出现重复行
        }

        var count = await host.Fsql.Select<FeatureValueEntity>()
            .Where(e => e.Name == Theme && e.ProviderName == FeatureProviders.User && e.ProviderKey == "u-1")
            .CountAsync(CancellationToken.None);
        Assert.Equal(1, count);
    }

    // ── D14 写路径异常传播（非静默） ──

    [Fact]
    public async Task Manager_SetValue_EmptyName_Throws_NonSilent()
    {
        using var host = FeatureManagementTestHost.Create();

        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            host.Manager.SetValueAsync("", "v", FeatureProviders.Global, null, CancellationToken.None));
    }

    [Fact]
    public async Task Manager_SetValue_DbFailure_Propagates_NonSilent()
    {
        // 写传播契约：DB 故障（已 Dispose 的 FreeSql 操作必定抛异常）→ 异常传播非静默（读静默写传播）
        using var host = FeatureManagementTestHost.Create();
        host.Fsql.Dispose();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            host.Manager.SetValueAsync(Theme, "v", FeatureProviders.Global, null, CancellationToken.None));
    }

    [Fact]
    public async Task Store_Set_DbFailure_Propagates_NonSilent()
    {
        using var host = FeatureManagementTestHost.Create();
        host.Fsql.Dispose();

        await Assert.ThrowsAnyAsync<Exception>(() =>
            host.Store.SetAsync(NewEntity(Theme, "v", FeatureProviders.Global, null), CancellationToken.None));
    }
}
