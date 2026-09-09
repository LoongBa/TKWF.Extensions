using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// IFeatureManager 测试——D3-D7 + D13 分层解析 / 缓存 / 匿名短路 / 负缓存 / GetEffectiveValueAsync。
/// <para>分层契约（IDomainUser）：User(user.UserId) → Role(user.UserInfo.Roles 遍历序) → Tenant(仅
/// user.TenantId.HasValue) → Global → defaultValue（参数或 FeatureDefinition.DefaultValue）；
/// 匿名（user==null 或 !user.IsAuthenticated）→ Global → 默认。</para>
/// <para>缓存验证用 DB 直改旁路法（Delete/Insert/Update 绕过 manager）——缓存命中则旁路改动不可见，
/// 无需计数桩（对齐 Settings 计数 Store 语义，DB 级更稳）。</para>
/// </summary>
public class FeatureManagerTests
{
    private const string Theme = ConsumerFeatureContributor.StringFeature;      // 默认 "light"
    private const string Checkout = ConsumerFeatureContributor.BooleanFeature;  // 默认 "false"
    private const string Fallback = "fallback";

    // ── D3 Global 层 ──

    [Fact]
    public async Task GetValueAsync_GlobalValue_ReturnsStoredValue()
    {
        using var host = FeatureManagementTestHost.Create();
        await SetGlobal(host, Theme, "dark");

        var result = await host.Manager.GetValueAsync(Theme, null, Fallback, CancellationToken.None);

        Assert.Equal("dark", result);
    }

    [Fact]
    public async Task GetValueAsync_NoValue_ReturnsFeatureDefinitionDefault()
    {
        using var host = FeatureManagementTestHost.Create();

        // 无任何存储值 → 回退 FeatureDefinition.DefaultValue（"light"）
        var result = await host.Manager.GetValueAsync(Theme, null, null, CancellationToken.None);

        Assert.Equal("light", result);
    }

    [Fact]
    public async Task GetValueAsync_NoValue_ExplicitDefaultParam_OverridesDefinitionDefault()
    {
        using var host = FeatureManagementTestHost.Create();

        var result = await host.Manager.GetValueAsync(Theme, null, "explicit", CancellationToken.None);

        Assert.Equal("explicit", result);
    }

    // ── D4 分层回退（IDomainUser 契约） ──

    [Fact]
    public async Task GetValueAsync_UserLayer_OverridesAllLowerLayers()
    {
        using var host = FeatureManagementTestHost.Create();
        var user = host.User.AsAuthenticated("u-1", tenantId: 100, "role-a");
        await SetGlobal(host, Theme, "global-theme");
        await SetTenant(host, Theme, "tenant-theme", "100");
        await SetRole(host, Theme, "role-theme", "role-a");
        await SetUser(host, Theme, "user-theme", "u-1");

        var result = await host.Manager.GetValueAsync(Theme, user, Fallback, CancellationToken.None);

        Assert.Equal("user-theme", result);
    }

    [Fact]
    public async Task GetValueAsync_NoUserValue_FallsBackToRole()
    {
        using var host = FeatureManagementTestHost.Create();
        var user = host.User.AsAuthenticated("u-1", tenantId: 100, "role-a");
        await SetGlobal(host, Theme, "global-theme");
        await SetTenant(host, Theme, "tenant-theme", "100");
        await SetRole(host, Theme, "role-theme", "role-a");

        var result = await host.Manager.GetValueAsync(Theme, user, Fallback, CancellationToken.None);

        Assert.Equal("role-theme", result);
    }

    [Fact]
    public async Task GetValueAsync_NoUserRole_FallsBackToTenant()
    {
        using var host = FeatureManagementTestHost.Create();
        var user = host.User.AsAuthenticated("u-1", tenantId: 100, "role-a");
        await SetGlobal(host, Theme, "global-theme");
        await SetTenant(host, Theme, "tenant-theme", "100");

        var result = await host.Manager.GetValueAsync(Theme, user, Fallback, CancellationToken.None);

        Assert.Equal("tenant-theme", result);
    }

    [Fact]
    public async Task GetValueAsync_NoUserRoleTenant_FallsBackToGlobal()
    {
        using var host = FeatureManagementTestHost.Create();
        var user = host.User.AsAuthenticated("u-1", tenantId: 100, "role-a");
        await SetGlobal(host, Theme, "global-theme");

        var result = await host.Manager.GetValueAsync(Theme, user, Fallback, CancellationToken.None);

        Assert.Equal("global-theme", result);
    }

    [Fact]
    public async Task GetValueAsync_AllLayersEmpty_ReturnsDefaultParam()
    {
        using var host = FeatureManagementTestHost.Create();
        var user = host.User.AsAuthenticated("u-1", tenantId: 100, "role-a");

        var result = await host.Manager.GetValueAsync(Theme, user, Fallback, CancellationToken.None);

        Assert.Equal(Fallback, result);
    }

    [Fact]
    public async Task GetValueAsync_TenantLayer_WithoutTenantId_Skipped()
    {
        // P5：Tenant 层仅 TenantId.HasValue 时查询——用户无租户则跳过 Tenant 层
        using var host = FeatureManagementTestHost.Create();
        var user = host.User.AsAuthenticated("u-1", tenantId: null, "role-a");
        await SetTenant(host, Theme, "tenant-theme", "100");
        await SetGlobal(host, Theme, "global-theme");

        var result = await host.Manager.GetValueAsync(Theme, user, Fallback, CancellationToken.None);

        Assert.Equal("global-theme", result);
    }

    // ── D5 Role 层多角色（遍历序） ──

    [Fact]
    public async Task GetValueAsync_RoleTraversal_ValueOnlyOnSecondRole_Hit()
    {
        // P4：Roles 遍历序——首个有值角色命中；role-a 无值继续遍历到 role-b
        using var host = FeatureManagementTestHost.Create();
        var user = host.User.AsAuthenticated("u-1", tenantId: null, "role-a", "role-b");
        await SetRole(host, Theme, "role-b-theme", "role-b");

        var result = await host.Manager.GetValueAsync(Theme, user, Fallback, CancellationToken.None);

        Assert.Equal("role-b-theme", result);
    }

    [Fact]
    public async Task GetValueAsync_RoleTraversal_FirstRoleWithValue_Wins()
    {
        using var host = FeatureManagementTestHost.Create();
        var user = host.User.AsAuthenticated("u-1", tenantId: null, "role-a", "role-b");
        await SetRole(host, Theme, "role-a-theme", "role-a");
        await SetRole(host, Theme, "role-b-theme", "role-b");

        var result = await host.Manager.GetValueAsync(Theme, user, Fallback, CancellationToken.None);

        Assert.Equal("role-a-theme", result);
    }

    // ── D6 匿名短路 ──

    [Fact]
    public async Task GetValueAsync_Anonymous_SkipsUserRoleTenant_ReturnsGlobal()
    {
        using var host = FeatureManagementTestHost.Create();
        // 先以认证用户写入各层
        var authUser = host.User.AsAuthenticated("u-1", tenantId: 100, "role-a");
        await SetUser(host, Theme, "user-theme", "u-1");
        await SetRole(host, Theme, "role-theme", "role-a");
        await SetTenant(host, Theme, "tenant-theme", "100");
        await SetGlobal(host, Theme, "global-theme");
        // 切回匿名（同一宿主实例切换认证状态）
        host.User.IsAuthenticated = false;

        var result = await host.Manager.GetValueAsync(Theme, host.User, Fallback, CancellationToken.None);

        Assert.Equal("global-theme", result);
    }

    [Fact]
    public async Task GetValueAsync_NullUser_TreatedAsAnonymous()
    {
        using var host = FeatureManagementTestHost.Create();
        await SetUser(host, Theme, "user-theme", "u-1");
        await SetGlobal(host, Theme, "global-theme");

        var result = await host.Manager.GetValueAsync(Theme, null, Fallback, CancellationToken.None);

        Assert.Equal("global-theme", result);
    }

    [Fact]
    public async Task GetValueAsync_Anonymous_NoGlobal_ReturnsDefault()
    {
        using var host = FeatureManagementTestHost.Create();

        var result = await host.Manager.GetValueAsync(Theme, host.User, Fallback, CancellationToken.None);

        Assert.Equal(Fallback, result);
    }

    // ── D7 缓存（读缓存 / 写后失效 / 负缓存 / TTL） ──

    [Fact]
    public async Task GetValueAsync_CacheHit_SecondReadDoesNotQueryDatabase()
    {
        using var host = FeatureManagementTestHost.Create();
        await SetGlobal(host, Theme, "dark");

        var r1 = await host.Manager.GetValueAsync(Theme, host.User, Fallback, CancellationToken.None);
        Assert.Equal("dark", r1);

        // 绕过 manager 直接删库——若二次读取命中缓存，删库不可见
        await host.Fsql.Delete<FeatureValueEntity>()
            .Where(e => e.Name == Theme && e.ProviderName == FeatureProviders.Global)
            .ExecuteAffrowsAsync(CancellationToken.None);

        var r2 = await host.Manager.GetValueAsync(Theme, host.User, Fallback, CancellationToken.None);

        Assert.Equal("dark", r2); // 缓存命中——未重新查库
    }

    [Fact]
    public async Task SetValueAsync_InvalidatesCache_NewValueImmediatelyVisible()
    {
        using var host = FeatureManagementTestHost.Create();
        await SetGlobal(host, Theme, "dark");
        await host.Manager.GetValueAsync(Theme, host.User, Fallback, CancellationToken.None); // 填充缓存

        await SetGlobal(host, Theme, "midnight"); // 写后缓存失效

        var result = await host.Manager.GetValueAsync(Theme, host.User, Fallback, CancellationToken.None);

        Assert.Equal("midnight", result);
    }

    [Fact]
    public async Task DeleteValueAsync_InvalidatesCache_ReturnsDefault()
    {
        using var host = FeatureManagementTestHost.Create();
        await SetGlobal(host, Theme, "dark");
        await host.Manager.GetValueAsync(Theme, host.User, Fallback, CancellationToken.None); // 填充缓存

        await host.Manager.DeleteValueAsync(Theme, FeatureProviders.Global, null, CancellationToken.None);

        // 无显式参数（null）→ 回退 FeatureDefinition.DefaultValue（"light"）
        var result = await host.Manager.GetValueAsync(Theme, host.User, null, CancellationToken.None);

        Assert.Equal("light", result); // 回退 FeatureDefinition.DefaultValue
    }

    [Fact]
    public async Task GetValueAsync_NegativeCache_UndefinedName_DoesNotReprocessDatabase()
    {
        // 负缓存（NotFoundSentinel）：未定义 name → 二次读取不查库（DB 旁路插入不可见）
        using var host = FeatureManagementTestHost.Create();
        const string undefined = "App.NotDefined";

        var r1 = await host.Manager.GetValueAsync(undefined, host.User, Fallback, CancellationToken.None);
        Assert.Equal(Fallback, r1);

        // 绕过 manager 直接入库——若负缓存生效，旁路插入不可见
        await host.Fsql.Insert(new FeatureValueEntity
        {
            Name = undefined,
            Value = "db-value",
            ProviderName = FeatureProviders.Global,
            ProviderKey = null,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow
        }).ExecuteAffrowsAsync(CancellationToken.None);

        var r2 = await host.Manager.GetValueAsync(undefined, host.User, Fallback, CancellationToken.None);

        Assert.Equal(Fallback, r2); // 负缓存命中——未重新查库
    }

    [Fact]
    public async Task GetValueAsync_CacheExpiration_AfterTtl_RefetchesDatabase()
    {
        using var host = FeatureManagementTestHost.Create(services =>
            services.Configure<FeatureOptions>(o => o.CacheExpirationSeconds = 1));
        await SetGlobal(host, Theme, "dark");

        var r1 = await host.Manager.GetValueAsync(Theme, host.User, Fallback, CancellationToken.None);
        Assert.Equal("dark", r1);

        // 绕过 manager 直接改库
        await host.Fsql.Update<FeatureValueEntity>()
            .Set(e => e.Value, "midnight")
            .Where(e => e.Name == Theme && e.ProviderName == FeatureProviders.Global)
            .ExecuteAffrowsAsync(CancellationToken.None);

        // TTL 内：缓存命中（旁路改动不可见）
        var r2 = await host.Manager.GetValueAsync(Theme, host.User, Fallback, CancellationToken.None);
        Assert.Equal("dark", r2);

        // TTL 过期：重新查库（旁路改动可见）
        await Task.Delay(1100, TestContext.Current.CancellationToken);
        var r3 = await host.Manager.GetValueAsync(Theme, host.User, Fallback, CancellationToken.None);
        Assert.Equal("midnight", r3);
    }

    // ── D13 GetEffectiveValueAsync（命中层返回——管理显示） ──

    [Fact]
    public async Task GetEffectiveValueAsync_GlobalHit_ReturnsValueAndProvider()
    {
        using var host = FeatureManagementTestHost.Create();
        await SetGlobal(host, Theme, "dark");

        var (value, providerName) = await host.Manager.GetEffectiveValueAsync(Theme, host.User, CancellationToken.None);

        Assert.Equal("dark", value);
        Assert.Equal(FeatureProviders.Global, providerName);
    }

    [Fact]
    public async Task GetEffectiveValueAsync_UserHit_ReturnsUserProvider()
    {
        using var host = FeatureManagementTestHost.Create();
        var user = host.User.AsAuthenticated("u-1", tenantId: 100, "role-a");
        await SetUser(host, Theme, "user-theme", "u-1");
        await SetGlobal(host, Theme, "global-theme");

        var (value, providerName) = await host.Manager.GetEffectiveValueAsync(Theme, user, CancellationToken.None);

        Assert.Equal("user-theme", value);
        Assert.Equal(FeatureProviders.User, providerName);
    }

    [Fact]
    public async Task GetEffectiveValueAsync_AfterWrite_ReturnsLatestValue()
    {
        using var host = FeatureManagementTestHost.Create();
        await SetGlobal(host, Theme, "dark");
        _ = await host.Manager.GetEffectiveValueAsync(Theme, host.User, CancellationToken.None); // 填充缓存

        await SetGlobal(host, Theme, "midnight"); // 写后缓存失效

        var (value, providerName) = await host.Manager.GetEffectiveValueAsync(Theme, host.User, CancellationToken.None);

        Assert.Equal("midnight", value);
        Assert.Equal(FeatureProviders.Global, providerName);
    }

    [Fact]
    public async Task GetEffectiveValueAsync_NoValue_ReturnsNullValue()
    {
        using var host = FeatureManagementTestHost.Create();

        var (value, _) = await host.Manager.GetEffectiveValueAsync(Theme, host.User, CancellationToken.None);

        Assert.Null(value);
    }

    // ── 其他门面方法 ──

    [Fact]
    public async Task GetDefinitionsAsync_ReturnsContributorDefinitions()
    {
        using var host = FeatureManagementTestHost.Create();

        var definitions = await host.Manager.GetDefinitionsAsync(CancellationToken.None);

        Assert.Equal(3, definitions.Count);
        Assert.Contains(definitions, d => d.Name == ConsumerFeatureContributor.BooleanFeature);
    }

    // ── 快捷辅助 ──

    private static async Task SetGlobal(FeatureManagementTestHost host, string name, string value)
        => await host.Manager.SetValueAsync(name, value, FeatureProviders.Global, null, CancellationToken.None);

    private static async Task SetUser(FeatureManagementTestHost host, string name, string value, string userId)
        => await host.Manager.SetValueAsync(name, value, FeatureProviders.User, userId, CancellationToken.None);

    private static async Task SetRole(FeatureManagementTestHost host, string name, string value, string role)
        => await host.Manager.SetValueAsync(name, value, FeatureProviders.Role, role, CancellationToken.None);

    private static async Task SetTenant(FeatureManagementTestHost host, string name, string value, string tenantId)
        => await host.Manager.SetValueAsync(name, value, FeatureProviders.Tenant, tenantId, CancellationToken.None);
}
