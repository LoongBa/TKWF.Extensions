using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.BackgroundJobs;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 后台任务持久化扩展初始化器（V0.1.0）——经 [TKWFExtension] 被 SG1 编译期发现，三钩子接线：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——DI 构建前：注册 Options + SG1 DataService + 监听器（TryAddEnumerable）+ 查询/记录器（TryAddScoped）</item>
/// <item><see cref="ConfigureFilters"/>——BackgroundJobs 无全局过滤器（空实现）</item>
/// <item><see cref="InitializeAsync"/>——系统就绪后：空实现</item>
/// </list>
/// <para>消费方须 <c>[TKWFEnabledExtension(typeof(BackgroundJobsExtensionInitializer&lt;&gt;))]</c> 白名单声明后三钩子才执行。</para>
/// </summary>
[TKWFExtension("BackgroundJobs")]
public class BackgroundJobsExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称（对齐 [TKWFExtension] Name）。</summary>
    public override string Name => "BackgroundJobs";

    /// <summary>扩展描述。</summary>
    public override string Description => "后台任务持久化增强——执行历史审计 + 业务结果追踪（V0.1.0）";

    public override void ConfigureServices(IServiceCollection services)
    {
        // 1. Options 绑定
        services.AddOptions<BackgroundJobsPersistenceOptions>()
            .BindConfiguration("TKWF:BackgroundJobs");

        // 1.5 SG1 DataService（监听器/查询服务构造依赖；对齐 Permissions 显式注册先例）
        services.TryAddScoped<JobExecutionEntityDataService>();
        services.TryAddScoped<JobResultEntityDataService>();

        // 2. 执行历史监听器（TryAddEnumerable：多监听器可叠加——Oracle C3，与契约一致）
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IBackgroundJobExecutionListener, JobExecutionRecorder>());

        // 3. 业务结果记录器
        services.TryAddScoped<IJobResultRecorder, JobResultRecorder>();

        // 4. 查询服务
        services.TryAddScoped<IJobExecutionQueryService, JobExecutionQueryService>();
        services.TryAddScoped<IJobResultQueryService, JobResultQueryService>();
    }

    /// <summary>BackgroundJobs 无全局过滤器。</summary>
    public override void ConfigureFilters(FilterBuilder<TUserInfo> builder) { /* BackgroundJobs 无全局过滤器 */ }

    /// <summary>系统就绪后初始化（空实现）。</summary>
    public override Task InitializeAsync() => Task.CompletedTask;
}
