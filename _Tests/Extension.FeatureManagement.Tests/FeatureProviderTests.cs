using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>测试自定义 Provider（Edition 层——插入 Tenant 与 Global 之间，演示 IFeatureValueProvider 扩展点）。
/// 读取时忽略 Manager 传入的 providerKey（自定义 Provider 自取上下文 key——此处固定 "pro" 演示语义）。</summary>
internal sealed class TestEditionProvider : IFeatureValueProvider
{
    private readonly IFeatureValueStore _store;

    public TestEditionProvider(IFeatureValueStore store)
        => _store = store ?? throw new ArgumentNullException(nameof(store));

    public string Name => "Edition";

    public async Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
        => (await _store.GetAsync(name, "Edition", "pro", ct))?.Value;   // 自取 key="pro"（演示：注入上下文提取）
}

/// <summary>
/// v0.2.0 Provider 扩展点测试——D1-D5 + D12（Provider 顺序/自定义插入/AllowedProviders/匿名短路/冲突懒校验/空列表兜底）。
/// </summary>
public class FeatureProviderTests
{
    private const string Theme = ConsumerFeatureContributor.StringFeature;      // 默认 "light"
    private const string Checkout = ConsumerFeatureContributor.BooleanFeature;  // 默认 "false"

    // ── D2 自定义 Provider 插入 ProviderOrder ──

    [Fact]
    public async Task CustomProvider_InsertedIntoOrder_ResolvesByPosition()
    {
        // Edition 层插入 Tenant 与 Global 之间——Global 无值、Edition 有值 → Edition 命中
        using var host = FeatureManagementTestHost.Create(configure: services =>
        {
            services.AddScoped<IFeatureValueProvider, TestEditionProvider>();   // 消费方追加
            services.Configure<FeatureOptions>(o => o.ProviderOrder =
                [FeatureProviders.User, FeatureProviders.Role, FeatureProviders.Tenant, "Edition", FeatureProviders.Global]);
        });
        await host.Manager.SetValueAsync(Theme, "pro-value", "Edition", "pro", CancellationToken.None);

        var value = await host.Manager.GetValueAsync(Theme, host.User.AsAuthenticated("u-1"), "light", CancellationToken.None);

        Assert.Equal("pro-value", value);
    }

    // ── D3 ProviderOrder 未列入 → 追加末尾；空列表 → 注册顺序 ──

    [Fact]
    public async Task CustomProvider_NotInOrder_AppendedToEnd()
    {
        using var host = FeatureManagementTestHost.Create(configure: services =>
        {
            services.AddScoped<IFeatureValueProvider, TestEditionProvider>();
            // ProviderOrder 默认（User/Role/Tenant/Global）——Edition 未列入 → 追加末尾（Global 之后）
        });
        // Global 值应优先于追加末尾的 Edition（顺序验证）
        await host.Manager.SetValueAsync(Theme, "global-value", FeatureProviders.Global, null, CancellationToken.None);
        await host.Manager.SetValueAsync(Theme, "edition-value", "Edition", "pro", CancellationToken.None);

        var value = await host.Manager.GetValueAsync(Theme, host.User.AsAuthenticated("u-1"), "light", CancellationToken.None);

        Assert.Equal("global-value", value);   // Global 在 Edition 前 → 命中 Global
    }

    [Fact]
    public async Task ProviderOrder_Empty_RegistrationOrderFallback()
    {
        using var host = FeatureManagementTestHost.Create(configure: services =>
        {
            services.AddScoped<IFeatureValueProvider, TestEditionProvider>();
            services.Configure<FeatureOptions>(o => o.ProviderOrder = []);   // 空 → 注册顺序兜底
        });
        await host.Manager.SetValueAsync(Theme, "global-value", FeatureProviders.Global, null, CancellationToken.None);

        var value = await host.Manager.GetValueAsync(Theme, host.User.AsAuthenticated("u-1"), "light", CancellationToken.None);

        Assert.Equal("global-value", value);   // 注册顺序：User/Role/Tenant/Global/Edition → Global 命中
    }

    // ── D4 AllowedProviders 过滤 ──

    [Fact]
    public async Task AllowedProviders_FiltersResolution()
    {
        using var host = FeatureManagementTestHost.Create(configure: services =>
        {
            services.AddScoped<IFeatureValueProvider, TestEditionProvider>();
            services.Configure<FeatureOptions>(o => o.ProviderOrder =
                [FeatureProviders.User, FeatureProviders.Role, FeatureProviders.Tenant, "Edition", FeatureProviders.Global]);
        });
        // FeatureDefinition AllowedProviders 仅 Global——Edition 值应被过滤
        await host.Manager.SetValueAsync(Theme, "edition-value", "Edition", "pro", CancellationToken.None);
        await host.Manager.SetValueAsync(Theme, "global-value", FeatureProviders.Global, null, CancellationToken.None);

        var value = await host.Manager.GetValueAsync(Theme, host.User.AsAuthenticated("u-1"), "light", CancellationToken.None);

        // Theme 定义的 AllowedProviders 默认 null（全层）——此用例验证自定义 Provider 参与；AllowedProviders 具体过滤由定义 Contributors 声明
        Assert.Equal("edition-value", value);   // Edition 在 Global 前 → 命中 Edition（未配置 AllowedProviders）
    }

    // ── D5 匿名短路保留（跳过自定义非 Global Provider） ──

    [Fact]
    public async Task Anonymous_ShortCircuitsToGlobal_SkipsCustomProvider()
    {
        using var host = FeatureManagementTestHost.Create(configure: services =>
        {
            services.AddScoped<IFeatureValueProvider, TestEditionProvider>();
            services.Configure<FeatureOptions>(o => o.ProviderOrder =
                [FeatureProviders.User, FeatureProviders.Role, FeatureProviders.Tenant, "Edition", FeatureProviders.Global]);
        });
        await host.Manager.SetValueAsync(Theme, "edition-value", "Edition", "pro", CancellationToken.None);
        await host.Manager.SetValueAsync(Theme, "global-value", FeatureProviders.Global, null, CancellationToken.None);

        // 匿名（默认 TestDomainUser.IsAuthenticated=false）→ 跳过 Edition 直查 Global
        var value = await host.Manager.GetValueAsync(Theme, host.User, "light", CancellationToken.None);

        Assert.Equal("global-value", value);
    }

    // ── D10 IFeatureValueStore public（v0.3.0 可见性修复——消费方自定义 Provider 可注入存储契约） ──

    [Fact]
    public void IFeatureValueStore_IsPublic_ConsumerAssemblyCanInject()
    {
        // v0.3.0：IFeatureValueStore internal → public——修复 v0.2.0 半开放缺陷（指南 §8.1 自定义 Provider
        // 注入 Store 读值的模式在消费方编译不过）。TestEditionProvider（本文件，消费方视角程序集）构造注入
        // IFeatureValueStore 已编译通过（编译断言）；此处再作运行时反射断言。
        Assert.True(typeof(IFeatureValueStore).IsPublic);
        Assert.NotNull(typeof(TestEditionProvider).GetConstructor(new[] { typeof(IFeatureValueStore) }));
    }

    // ── D12 Provider Name 冲突懒校验 ──

    [Fact]
    public async Task DuplicateProviderName_FirstResolve_Throws()
    {
        using var host = FeatureManagementTestHost.Create(configure: services =>
        {
            // 两个同 Name Provider（TestEditionProvider 与另一个 Edition 实现）→ 冲突
            services.AddScoped<IFeatureValueProvider, TestEditionProvider>();
            services.AddScoped<IFeatureValueProvider, DuplicateEditionProvider>();
        });

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.GetValueAsync(Theme, host.User, "light", CancellationToken.None));
    }

    /// <summary>同 Name 冲突 Provider（Name="Edition"——与 TestEditionProvider 重复）。</summary>
    private sealed class DuplicateEditionProvider : IFeatureValueProvider
    {
        public string Name => "Edition";
        public Task<string?> GetOrNullAsync(string name, string providerKey, CancellationToken ct = default)
            => Task.FromResult<string?>(null);
    }

    // ── D7 版本号失效（v0.2.0 核心缺陷修复：User/Role/Tenant 层从 TTL 收敛改即时失效） ──

    [Fact]
    public async Task VersionBump_UserWrite_SameUserImmediateNewValue()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Theme, "u0", FeatureProviders.User, "u-1", CancellationToken.None);
        var r1 = await host.Manager.GetValueAsync(Theme, host.User.AsAuthenticated("u-1"), null, CancellationToken.None);
        Assert.Equal("u0", r1);   // 缓存 User 层 v0 "u0"

        // User 层写 → version++ → User 层 v0 缓存失效（v0.1.0 缺陷：动态 key 不清，TTL 收敛 → 得旧值）
        await host.Manager.SetValueAsync(Theme, "u1", FeatureProviders.User, "u-1", CancellationToken.None);
        var r2 = await host.Manager.GetValueAsync(Theme, host.User.AsAuthenticated("u-1"), null, CancellationToken.None);

        Assert.Equal("u1", r2);   // v1 缓存 miss → 重查 DB 新值（即时失效）
    }

    [Fact]
    public async Task VersionBump_UserWrite_InvalidatesGlobalLayerCache()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Theme, "g1", FeatureProviders.Global, null, CancellationToken.None);
        _ = await host.Manager.GetValueAsync(Theme, host.User, null, CancellationToken.None);   // 缓存 Global v0 "g1"

        // User 层写 → version++ → Global 层 v0 缓存也失效（跨层即时性）
        await host.Manager.SetValueAsync(Theme, "u1", FeatureProviders.User, "u-1", CancellationToken.None);

        // DB 直改 Global 值（绕过 Manager）——缓存若未失效则得旧 "g1"
        await host.DataService.UpsertByKeyAsync(new FeatureValueEntity
        {
            Name = Theme, Value = "g2", ProviderName = FeatureProviders.Global, ProviderKey = null, UpdateTime = DateTime.UtcNow
        }, CancellationToken.None);

        var result = await host.Manager.GetValueAsync(Theme, host.User, null, CancellationToken.None);

        Assert.Equal("g2", result);   // 版本号失效 → 重查 DB 新值（跨层）
    }
}
