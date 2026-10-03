using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interception.Filters;
using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.FeatureManagement.Tests;

/// <summary>
/// FeatureChecker 测试——D8-D9 检查器/过滤器。
/// <para>D8：IsEnabledAsync 布尔解析（true/false）；未定义 → false（fail-closed，D18）；
/// 命中层值存在但 bool 解析失败（P3）→ false 不跨层回退。</para>
/// <para>D9：FeatureChecker&lt;TUserInfo&gt; 接 ambient 用户（<see cref="DomainUserContext.CurrentAopUser"/>，
/// 对齐 PermissionChecker）+ <see cref="FilterBuilder{TUserInfo}.AddFeatureCheck"/> 注册断言。
/// 完整 [RequireFeature] → FeatureDisabledException 端到端需 DomainContext 构造（超出单测宿主范围），
/// 以"AddFeatureCheck 注册 + checker 接 ambient 用户"两断言覆盖（任务允许的降级路径）。</para>
/// <para>V4.10.53（领域自治根治，接线型）：IFeatureChecker 为主框架 Core 契约（非 IDomainService 不可修改）——
/// 框架 FeatureFilterAttribute 经 <c>context.ServiceProvider.GetService&lt;IFeatureChecker&gt;()</c> 普通 DI 解析（L41）；
/// 实现 ctor(<see cref="IServiceProvider"/>, ILogger) + C1 延迟解析 <see cref="IFeatureManager"/>（GetRequiredService——
/// AddConstructibleService 守卫工厂需 <see cref="DomainUserContext.CurrentAopUser"/> 非空，测试经 host 桩设 ambient）。
/// 旧 ctor(IDomainUser)（永不注册 DI——D01）在框架过滤器 GetService 解析时构造失败 → 特性检查静默失效（真实生产故障，已修复）。</para>
/// <para>注：<see cref="DomainUserContext"/> 为框架 internal——须主框架 TKWF.Domain.csproj 增加
/// <c>&lt;InternalsVisibleTo Include="TKWF.Ext.FeatureManagement.Tests" /&gt;</c>
/// （亦须 <c>TKWF.Ext.FeatureManagement</c>——扩展自身 checker 读 ambient 用户）。</para>
/// </summary>
public class FeatureCheckerTests
{
    private const string Checkout = ConsumerFeatureContributor.BooleanFeature; // 默认 "false"

    // ── D8 IsEnabledAsync 布尔解析 ──

    [Fact]
    public async Task IsEnabledAsync_DefinedBoolean_NoValue_DefaultFalse_ReturnsFalse()
    {
        using var host = FeatureManagementTestHost.Create();
        using var restore = SetAmbientUser(host, null); // 显式匿名（桩 IsAuthenticated=false——守卫工厂需非空 CurrentAopUser）

        var result = await host.Checker.IsEnabledAsync(Checkout, CancellationToken.None);

        Assert.False(result); // 无存储值 → FeatureDefinition.DefaultValue "false"
    }

    [Fact]
    public async Task IsEnabledAsync_DefinedBoolean_GlobalTrue_ReturnsTrue()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Checkout, "true", FeatureProviders.Global, null, CancellationToken.None);
        using var restore = SetAmbientUser(host, null);

        var result = await host.Checker.IsEnabledAsync(Checkout, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsEnabledAsync_DefinedBoolean_GlobalFalse_ReturnsFalse()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Checkout, "false", FeatureProviders.Global, null, CancellationToken.None);
        using var restore = SetAmbientUser(host, null);

        var result = await host.Checker.IsEnabledAsync(Checkout, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsEnabledAsync_UndefinedFeature_ReturnsFalse()
    {
        // D18：未定义 Feature → IsEnabled 恒 false（fail-closed，框架契约——无 FailClosedOnUndefined 开关）
        using var host = FeatureManagementTestHost.Create();
        using var restore = SetAmbientUser(host, null);

        Assert.False(await host.Checker.IsEnabledAsync("App.NotDefined", CancellationToken.None));
    }

    [Fact]
    public async Task IsEnabledAsync_EmptyName_ReturnsFalse()
    {
        using var host = FeatureManagementTestHost.Create();
        using var restore = SetAmbientUser(host, null);

        Assert.False(await host.Checker.IsEnabledAsync("", CancellationToken.None));
    }

    [Fact]
    public async Task IsEnabledAsync_HitLayerBoolParseFailure_ReturnsFalse_NoCrossLayerFallback()
    {
        // P3：命中层（User）值 "abc" bool 解析失败 → false，不跨层回退到 Global "true"
        // 注：v0.3.0 起写时校验拒绝向 Boolean Feature 写 "abc"（Manager.SetValueAsync 抛 ArgumentException）——
        // 为保留 P3 读侧语义（命中层存在非法布尔值时的解析行为），此处经 DataService 直写原始坏值（绕过校验，
        // 等价于存量库/外部直写遗留数据场景）。
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Checkout, "true", FeatureProviders.Global, null, CancellationToken.None);
        await host.DataService.UpsertByKeyAsync(new FeatureValueEntity
        {
            Name = Checkout,
            Value = "abc",
            ProviderName = FeatureProviders.User,
            ProviderKey = "u-1",
            UpdateTime = DateTime.UtcNow
        }, CancellationToken.None);
        using var restore = SetAmbientUser(host, "u-1");

        var result = await host.Checker.IsEnabledAsync(Checkout, CancellationToken.None);

        Assert.False(result);
    }

    // ── D9 Checker 接 ambient 用户 ──

    [Fact]
    public async Task IsEnabledAsync_AmbientUser_UserLayerValue_Resolved()
    {
        // checker 从 DomainUserContext.CurrentAopUser 解析 ambient 用户 → 命中 User 层
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Checkout, "true", FeatureProviders.User, "u-1", CancellationToken.None);
        await host.Manager.SetValueAsync(Checkout, "false", FeatureProviders.Global, null, CancellationToken.None);
        using var restore = SetAmbientUser(host, "u-1");

        var result = await host.Checker.IsEnabledAsync(Checkout, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsEnabledAsync_AmbientUser_UserLayerAbsent_FallsBackToGlobal()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Checkout, "true", FeatureProviders.Global, null, CancellationToken.None);
        using var restore = SetAmbientUser(host, "u-1"); // 认证用户但 User 层无值

        var result = await host.Checker.IsEnabledAsync(Checkout, CancellationToken.None);

        Assert.True(result); // 回退 Global "true"
    }

    [Fact]
    public void Checker_ImplementsFrameworkIFeatureChecker()
    {
        using var host = FeatureManagementTestHost.Create();

        Assert.NotNull(host.Checker);
        Assert.IsAssignableFrom<IFeatureChecker>(host.Checker);
    }

    // ── 接线型：普通 DI 直构/GetService 可解析（修复生产故障哨兵） ──

    [Fact]
    public void Checker_PlainDi_Constructible_NoIdomainUser()
    {
        // 生产故障修复哨兵：旧 ctor(IDomainUser)（永不注册 DI——D01）在框架过滤器 GetService 解析时构造失败
        // （特性检查静默失效）；改 ctor(IServiceProvider, ILogger) 后普通 DI GetRequiredService 可构造。
        using var host = FeatureManagementTestHost.Create();

        var checker = host.Checker;
        Assert.NotNull(checker);
        Assert.IsType<FeatureChecker<FeatureManagementUserInfo>>(checker);
    }

    [Fact]
    public async Task Checker_PlainDi_ResolvesManager_ThroughAmbientScope()
    {
        // 接线型内部 C1 延迟解析：Manager 经 serviceProvider.GetRequiredService<IFeatureManager>()（守卫工厂）——
        // 测试经 ambient 桩设 CurrentAopUser（生产 AOP 窗口内同路径）→ 解析成功并命中值
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Checkout, "true", FeatureProviders.Global, null, CancellationToken.None);
        using var restore = SetAmbientUser(host, null);

        Assert.True(await host.Checker.IsEnabledAsync(Checkout, CancellationToken.None));
    }

    // ── D9 AddFeatureCheck 过滤器注册 ──

    [Fact]
    public void ConfigureFilters_AddFeatureCheck_RegistersFeatureFilter()
    {
        var builder = new FilterBuilder<FeatureManagementUserInfo>();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureFilters(builder);

        Assert.Contains(builder.Filters, f => f is FeatureFilterAttribute<FeatureManagementUserInfo>);
    }

    // ── ambient 用户辅助（对齐 PermissionCheckerTests 模式） ──

    /// <summary>
    /// 设置 CurrentAopUser 为宿主桩（Provider 已注入——FeatureManager 经守卫工厂解析后内部 Use&lt;T&gt; 可用）。
    /// <para>V4.10.53（接线型）：checker 内部 GetRequiredService&lt;IFeatureManager&gt;() 经 AddConstructibleService 守卫工厂
    /// 需 CurrentAopUser 非空——匿名用例亦设匿名桩（IsAuthenticated=false）而非 null（语义：匿名 → 直查 Global）。</para>
    /// </summary>
    private static IDisposable SetAmbientUser(FeatureManagementTestHost host, string? userId, string? role = null)
    {
        var previous = DomainUserContext.CurrentAopUser;
        if (userId == null)
        {
            // 匿名桩（非 null——守卫工厂需非空；IsAuthenticated=false → 匿名短路直查 Global）
            host.User.IsAuthenticated = false;
            host.User.UserId = null;
            host.User.UserInfo = null;
            host.User.TenantId = null;
            DomainUserContext.CurrentAopUser = host.User;
            return new RestoreAmbientUser(previous);
        }

        // 认证桩（可控 TenantId/IsAuthenticated/角色——Role 层遍历序测试用）
        host.User.AsAuthenticated(userId, tenantId: null, role != null ? [role] : []);
        DomainUserContext.CurrentAopUser = host.User;
        return new RestoreAmbientUser(previous);
    }

    /// <summary>测试后恢复原 ambient 用户（AsyncLocal 线程流，xunit.v3 每测试独立上下文但仍显式清理）。</summary>
    private sealed class RestoreAmbientUser(object? previous) : IDisposable
    {
        public void Dispose() => DomainUserContext.CurrentAopUser = previous;
    }
}
