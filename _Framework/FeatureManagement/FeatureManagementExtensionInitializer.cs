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
/// <item><see cref="ConfigureServices"/>——Options 绑定 + IMemoryCache + Feature 贡献者收集 + Store/Manager/Checker 注册（TryAddScoped）</item>
/// <item><see cref="ConfigureFilters"/>——<c>builder.AddFeatureCheck()</c>（框架方法，接入 [RequireFeature] 过滤器——不自建）</item>
/// <item><see cref="InitializeAsync"/>——空实现（无种子）</item>
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
        var context = new FeatureDefinitionContext();
        var repository = new InMemoryFeatureDefinitionRepository();
        foreach (var contributorData in (ProjectMetaContextBase.Instance as ProjectMetaContextBase)?.FeatureContributors
                 ?? Array.Empty<PermissionContributorData>())
        {
            if (Activator.CreateInstance(contributorData.ContributorType) is IFeatureDefinitionContributor contributor)
            {
                try
                {
                    contributor.Define(context);
                }
                catch (Exception ex)
                {
                    // 单个贡献者失败不阻塞整体——记录警告（对齐扩展机制容错语义）
                    Microsoft.Extensions.Logging.LoggerExtensions.LogWarning(
                        services.BuildServiceProvider().GetService<ILoggerFactory>()?.CreateLogger("FeatureManagement") ?? null!,
                        ex, "Feature 贡献者 {Contributor} Define 失败", contributorData.Name);
                }
            }
        }
        repository.AddRange(context.Definitions);

        services.TryAddScoped<IFeatureDefinitionRepository>(_ => repository);
        services.TryAddScoped<IFeatureValueStore, FeatureValueStore>();

        // v0.2.0 Provider 扩展点——内置四层（TryAddEnumerable：多实现遍历，C2 评审修正——TryAddScoped 只注册第一个）
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureValueProvider, UserFeatureValueProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureValueProvider, RoleFeatureValueProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureValueProvider, TenantFeatureValueProvider>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IFeatureValueProvider, GlobalFeatureValueProvider>());

        services.TryAddScoped<IFeatureManager>(sp => new FeatureManager(
            sp.GetRequiredService<IFeatureValueStore>(),
            sp.GetRequiredService<IFeatureDefinitionRepository>(),
            sp.GetServices<IFeatureValueProvider>(),
            sp.GetRequiredService<FeatureCacheVersionRegistry>(),
            sp.GetRequiredService<IDomainUser>(),
            sp.GetRequiredService<IMemoryCache>(),
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<FeatureOptions>>(),
            sp.GetRequiredService<ITransactionManager>(),
            sp.GetRequiredService<ILocalEventBus>(),
            sp.GetRequiredService<ILogger<FeatureManager>>()));
        services.TryAddScoped<IFeatureChecker, FeatureChecker<TUserInfo>>();
        // 管理 API 服务（[GenerateController]——写路径委托 Manager：缓存失效 + Global 唯一性，C3 裁定）
        services.TryAddScoped<FeatureManagementApiService>();
    }

    /// <summary>接入框架 [RequireFeature] 过滤器（不自建——V4.9.50 ADR19 P4 基座）。</summary>
    public override void ConfigureFilters(FilterBuilder<TUserInfo> builder)
        => builder.AddFeatureCheck();

    /// <summary>系统就绪后初始化（空实现——无种子数据）。</summary>
    public override Task InitializeAsync() => Task.CompletedTask;
}
