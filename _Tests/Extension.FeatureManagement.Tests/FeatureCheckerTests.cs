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
        using var restore = SetAmbientUser(null); // 显式匿名

        var result = await host.Checker.IsEnabledAsync(Checkout, CancellationToken.None);

        Assert.False(result); // 无存储值 → FeatureDefinition.DefaultValue "false"
    }

    [Fact]
    public async Task IsEnabledAsync_DefinedBoolean_GlobalTrue_ReturnsTrue()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Checkout, "true", FeatureProviders.Global, null, CancellationToken.None);
        using var restore = SetAmbientUser(null);

        var result = await host.Checker.IsEnabledAsync(Checkout, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsEnabledAsync_DefinedBoolean_GlobalFalse_ReturnsFalse()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Checkout, "false", FeatureProviders.Global, null, CancellationToken.None);
        using var restore = SetAmbientUser(null);

        var result = await host.Checker.IsEnabledAsync(Checkout, CancellationToken.None);

        Assert.False(result);
    }

    [Fact]
    public async Task IsEnabledAsync_UndefinedFeature_ReturnsFalse()
    {
        // D18：未定义 Feature → IsEnabled 恒 false（fail-closed，框架契约——无 FailClosedOnUndefined 开关）
        using var host = FeatureManagementTestHost.Create();
        using var restore = SetAmbientUser(null);

        Assert.False(await host.Checker.IsEnabledAsync("App.NotDefined", CancellationToken.None));
    }

    [Fact]
    public async Task IsEnabledAsync_EmptyName_ReturnsFalse()
    {
        using var host = FeatureManagementTestHost.Create();
        using var restore = SetAmbientUser(null);

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
        using var restore = SetAmbientUser("u-1");

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
        using var restore = SetAmbientUser("u-1");

        var result = await host.Checker.IsEnabledAsync(Checkout, CancellationToken.None);

        Assert.True(result);
    }

    [Fact]
    public async Task IsEnabledAsync_AmbientUser_UserLayerAbsent_FallsBackToGlobal()
    {
        using var host = FeatureManagementTestHost.Create();
        await host.Manager.SetValueAsync(Checkout, "true", FeatureProviders.Global, null, CancellationToken.None);
        using var restore = SetAmbientUser("u-1"); // 认证用户但 User 层无值

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

    // ── D9 AddFeatureCheck 过滤器注册 ──

    [Fact]
    public void ConfigureFilters_AddFeatureCheck_RegistersFeatureFilter()
    {
        var builder = new FilterBuilder<FeatureManagementUserInfo>();
        new FeatureManagementExtensionInitializer<FeatureManagementUserInfo>().ConfigureFilters(builder);

        Assert.Contains(builder.Filters, f => f is FeatureFilterAttribute<FeatureManagementUserInfo>);
    }

    // ── ambient 用户辅助（对齐 PermissionCheckerTests 模式） ──

    private static IDisposable SetAmbientUser(string? userId, string? role = null)
    {
        var previous = DomainUserContext.CurrentAopUser;
        if (userId == null)
        {
            DomainUserContext.CurrentAopUser = null;
            return new RestoreAmbientUser(previous);
        }

        // 用 TestDomainUser（可控 TenantId/IsAuthenticated）而非裸 DomainUser<T>——
        // 裸 DomainUser 无 Host 关联时 TenantId 访问抛 InvalidOperationException（测试环境无 DomainHost）
        var userInfo = new TestUserInfo(userId, $"用户-{userId}", role != null ? [role] : []);
        DomainUserContext.CurrentAopUser = new TestDomainUser
        {
            IsAuthenticated = true,
            UserId = userId,          // 独立属性（不从 UserInfo.UserIdString 映射）——User 层 key 依赖它
            UserInfo = userInfo
        };
        return new RestoreAmbientUser(previous);
    }

    /// <summary>测试后恢复原 ambient 用户（AsyncLocal 线程流，xunit.v3 每测试独立上下文但仍显式清理）。</summary>
    private sealed class RestoreAmbientUser(object? previous) : IDisposable
    {
        public void Dispose() => DomainUserContext.CurrentAopUser = previous;
    }
}
