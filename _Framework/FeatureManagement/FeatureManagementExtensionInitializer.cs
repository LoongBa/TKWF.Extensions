using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Features;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Events;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// 功能管理扩展初始化器——<c>[TKWFExtension("FeatureManagement")]</c> 被 SG1 发现，三钩子接线：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——Options 绑定 + IMemoryCache + Feature 贡献者收集 + Store/Manager/Checker 注册（V4.10.53 三态）</item>
/// <item><see cref="ConfigureFilters"/>——<c>builder.AddFeatureCheck()</c>（框架方法，接入 [RequireFeature] 过滤器——不自建）</item>
/// <item><see cref="InitializeAsync"/>——空实现（无种子）</item>
/// </list>
/// <para>V4.10.53（领域自治根治，正确路线——ADR90）三态注册：</para>
/// <list type="bullet">
/// <item><b>门面（AddConstructibleService）</b>——<see cref="IFeatureValueStore"/> / <see cref="IFeatureManager"/>
///     （接口 : IDomainService）：接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；
///     消费方经 <c>User.Use&lt;IFeatureValueStore&gt;()</c> / <c>User.Use&lt;IFeatureManager&gt;()</c> 解析。
///     旧形态 TryAddScoped 构造注入 <see cref="IDomainUser"/>（永不注册 DI——D01）生产解析必失败（v0.3.3 同根缺陷）。</item>
/// <item><b>接线型（TryAddScoped 普通 DI）</b>——<see cref="IFeatureChecker"/>（主框架 Core 契约，非 IDomainService
///     不可修改）：框架 <c>FeatureFilterAttribute</c> 经 <c>context.ServiceProvider.GetService&lt;IFeatureChecker&gt;()</c>
///     普通 DI 解析（L41）；实现 ctor(<see cref="IServiceProvider"/>, ILogger) + C1 延迟解析 <see cref="IFeatureManager"/>。</item>
/// <item><b>多 Provider（TryAddEnumerableConstructible）</b>——<see cref="IFeatureValueProvider"/> 内置四层（User/Role/Tenant/Global）：
///     多实现集合（按实现类型去重），AddConstructibleService 单实现不适用；实现继承 DomainServiceBase，内部
///     Use&lt;T&gt; 经基类 User。<b>✅ 已迁移（V4.10.55 ADR92）</b>：集合版守卫工厂
///     <c>TryAddEnumerableConstructible&lt;IFeatureValueProvider, TImpl&gt;()</c>——集合元素以守卫工厂形态注册，
///     帧内（<c>User.Use&lt;T&gt;()</c> 调用链）经 CurrentAopUser 注入构造（ctor <see cref="IDomainUser"/> 由守卫
///     工厂供给）；实现类注册 throw-factory；帧外枚举抛 InvalidOperationException（禁止形态）。原 TryAddEnumerable
///     集合按实现类 ctor 激活、IDomainUser 永不注册 DI → 生产解析失败的框架机制缺口（T3 转达候选）已由框架侧
///     ADR92 收编。</item>
/// </list>
/// <para>贡献者收集（对齐 PermissionExtensionInitializer 链路）：<c>ProjectMetaContextBase.Instance.FeatureContributors</c>
/// （主框架 V4.9.114 SG1 收集）→ Activator.CreateInstance → Define(context) → repository.AddRange。</para>
/// <para>V4.9.85 起发现不自动启用——消费方须 <c>[TKWFEnabledExtension(typeof(FeatureManagementExtensionInitializer&lt;&gt;))]</c> 白名单声明。</para>
/// </summary>
[TKWFExtension("FeatureManagement")]
public class FeatureManagementExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "FeatureManagement";

    /// <summary>扩展描述。</summary>
    public override string Description => "功能管理扩展——编译期 Feature 定义声明 + 分层值（Global/Tenant/Role/User）+ IFeatureChecker 实现（接入框架 [RequireFeature] 过滤器）+ 管理 API";

    /// <summary>注册 Feature 定义收集 + Store/Manager/Checker。</summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        services.AddOptions<FeatureOptions>().BindConfiguration("TKWF:FeatureManagement");
        services.TryAddSingleton<IMemoryCache, MemoryCache>();
        services.TryAddSingleton<FeatureCacheVersionRegistry>();   // v0.2.0 版本号缓存表（Singleton——跨 scope 共享）

        // Feature 贡献者收集（对齐 PermissionExtensionInitializer——ProjectMetaContextBase.Instance 在宿主注册期已设置）
        // V4.10.31 (A+ 阶段 3)：读新桥 Contributors[Feature]（接口判定收集）；V4.10.32 起经 CreateContributorInstances 编译期实例化
        var context = new FeatureDefinitionContext();
        var repository = new InMemoryFeatureDefinitionRepository();
        var ctx3 = ProjectMetaContextBase.Instance as ProjectMetaContextBase;
        var contributors3 = ctx3 != null && ctx3.Contributors.ContainsKey(ContributorTargetKinds.Feature)
            ? ctx3.CreateContributorInstances(ContributorTargetKinds.Feature)
            : Array.Empty<object>();
        foreach (var contributor in contributors3)
        {
            if (contributor is IFeatureDefinitionContributor featureContributor)
            {
                try
                {
                    featureContributor.Define(context);
                }
                catch (Exception ex)
                {
                    // 单个贡献者失败不阻塞整体——记录警告（对齐扩展机制容错语义）
                    Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                        services.BuildServiceProvider().GetService<ILoggerFactory>()?.CreateLogger("FeatureManagement") ?? null!,
                        ex, "Feature 贡献者 Define 失败");
                }
            }
        }
        repository.AddRange(context.Definitions);

        services.TryAddScoped<IFeatureDefinitionRepository>(_ => repository);

        // V4.10.53（领域自治根治）：Store/Manager 注册由 TryAddScoped 改 AddConstructibleService——
        // 接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory；消费方经 User.Use<T>() 解析。
        services.AddConstructibleService<IFeatureValueStore, FeatureValueStore>();
        services.AddConstructibleService<IFeatureManager, FeatureManager>();

        // v0.2.0 Provider 扩展点——内置四层（V4.10.55 ADR92：TryAddEnumerableConstructible 集合版守卫工厂——
        // 多实现集合，帧内 User.Use<T>() 调用链经 CurrentAopUser 注入构造（ctor IDomainUser 由守卫工厂供给），
        // 解决原 TryAddEnumerable 集合按实现类 ctor 激活、IDomainUser 永不注册 DI → 生产解析失败的机制缺口）
        services.TryAddEnumerableConstructible<IFeatureValueProvider, UserFeatureValueProvider>();
        services.TryAddEnumerableConstructible<IFeatureValueProvider, RoleFeatureValueProvider>();
        services.TryAddEnumerableConstructible<IFeatureValueProvider, TenantFeatureValueProvider>();
        services.TryAddEnumerableConstructible<IFeatureValueProvider, GlobalFeatureValueProvider>();

        // V4.10.53（接线型）：IFeatureChecker 主框架契约（非 IDomainService）——保持 TryAddScoped 普通 DI
        //（框架 FeatureFilterAttribute 经 context.ServiceProvider.GetService<IFeatureChecker>() 解析；ctor IServiceProvider + C1 延迟解析 Manager）
        services.TryAddScoped<IFeatureChecker, FeatureChecker<TUserInfo>>();
        // 管理 API 服务（[GenerateController]——写路径委托 Manager：缓存失效 + Global 唯一性，C3 裁定）
        services.TryAddScoped<FeatureManagementApiService>();
    }

    /// <summary>接入框架 [RequireFeature] 过滤器（不自建——V4.9.50 ADR19 P4 基座）。</summary>
    public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
        => builder.AddFeatureCheck();

    /// <summary>系统就绪后初始化（空实现——无种子数据）。</summary>
    public override Task InitializeAsync(System.IServiceProvider sp) => Task.CompletedTask;
}
