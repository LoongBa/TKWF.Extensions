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
/// <item><see cref="ConfigureServices"/>——DI 构建前：注册 Options + 监听器（TryAddEnumerable）+ 查询/记录器 + 历史清理服务（AddConstructibleService）</item>
/// <item><see cref="ConfigureFilters"/>——BackgroundJobs 无全局过滤器（空实现）</item>
/// <item><see cref="InitializeAsync"/>——系统就绪后：空实现</item>
/// </list>
/// <para>V4.10.53（领域自治根治，ADR90）三态注册：</para>
/// <list type="bullet">
/// <item><b>门面（AddConstructibleService）</b>——4 接口 : IDomainService（IJobResultRecorder /
///     IJobExecutionQueryService / IJobResultQueryService / IJobHistoryCleanupService）→ 接口可构造守卫工厂
///     （CurrentAopUser 守卫）+ 实现类 throw-factory；消费方统一经 <c>User.Use&lt;接口&gt;()</c> 解析
///     （AOP 路径先设 CurrentAopUser 再 GetRequiredService）。旧形态 TryAddScoped 构造注入
///     <see cref="IDomainUser"/>（永不注册 DI——D01）生产解析必失败（v0.3.3 同根缺陷）。</item>
/// <item><b>边界保留（TryAddEnumerable）</b>——<see cref="IBackgroundJobExecutionListener"/>（主框架 Core 契约，
///     非 IDomainService 不可修改）+ <see cref="JobExecutionRecorder"/>：多实现集合（按实现类型去重），
///     AddConstructibleService 单实现不适用；JobExecutionRecorder ctor 有 IDomainUser——运行期依赖框架三桥
///     （内置/Hangfire/Quartz）经 GetServices 枚举 + SystemActor 通道供给 Use&lt;T&gt; 懒加载窗口
///     （ADR88 方案 i 已收敛，本批不改代码）。</item>
/// </list>
/// <para>Options：<see cref="BackgroundJobsPersistenceOptions"/> 标 [Options("TKWF:BackgroundJobs")]——SG1 消费方
/// 自动绑定（模式 A 双通道）；此处 <c>AddOptions + BindConfiguration</c> 显式绑定 + 默认值兜底
/// （AddConstructibleService 守卫工厂经 ActivatorUtilities 解析剩余参数需 IOptions 可解析）。</para>
/// <para>零 DataService 手动注册（ADR61/D17 铁律）——JobExecution/JobResult DataService 经 SG1 消费方聚合自动注册
/// （throw-factory），运行期 <c>User.Use&lt;具体类&gt;()</c> NoAop 直建。</para>
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
        // 1. Options 绑定（V0.1.0：显式 BindConfiguration + [Options] 特性 SG 自动绑定——模式 A 双通道，
        //    对齐 SecurityLog 先例；勿重复 AddOptions）
        services.AddOptions<BackgroundJobsPersistenceOptions>()
            .BindConfiguration("TKWF:BackgroundJobs");

        // 1.5 SG1 DataService——v4.10.8 (ADR61) 起经 SG 基类类型判定 + 消费方聚合自动注册（可构造工厂），不再手动 TryAddScoped

        // 2. 执行历史监听器（边界保留）：主框架契约 IBackgroundJobExecutionListener 非 IDomainService——
        //    TryAddEnumerable 多实现集合（Oracle C3 多监听器可叠加），AddConstructibleService 不适用；保持原注册
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IBackgroundJobExecutionListener, JobExecutionRecorder>());

        // 3-5. V4.10.53（领域自治根治，ADR90）：4 门面 TryAddScoped → AddConstructibleService——
        //    接口可构造守卫工厂（CurrentAopUser 守卫）+ 实现类 throw-factory，消费方 User.Use<接口>() 解析
        services.AddConstructibleService<IJobResultRecorder, JobResultRecorder>();
        services.AddConstructibleService<IJobExecutionQueryService, JobExecutionQueryService>();
        services.AddConstructibleService<IJobResultQueryService, JobResultQueryService>();
        services.AddConstructibleService<IJobHistoryCleanupService, JobHistoryCleanupService>();
    }

    /// <summary>BackgroundJobs 无全局过滤器。</summary>
    public override void ConfigureFilters(FilterBuilder<TUserInfo> builder) { /* BackgroundJobs 无全局过滤器 */ }

    /// <summary>系统就绪后初始化（空实现）。</summary>
    public override Task InitializeAsync(System.IServiceProvider sp) => Task.CompletedTask;
}
