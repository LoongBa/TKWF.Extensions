using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.BackgroundJobs;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 作业执行监听器（V0.1.0）——<see cref="IBackgroundJobExecutionListener"/> 实现，
/// Context→实体映射→注入 <see cref="JobExecutionEntityDataService"/> 落库。
/// <para>异常静默 + ILogger.Warning——对齐契约语义：监听器异常绝不遮蔽业务异常、不阻断状态更新。</para>
/// <para>注册方式：<c>TryAddEnumerable(ServiceDescriptor.Scoped&lt;IBackgroundJobExecutionListener, JobExecutionRecorder&gt;())</c>，
/// 多监听器可叠加（Oracle C3，与契约一致）。</para>
/// <para>⚡ ADR88（v4.10.52）构造注入门控：DataService 改 IDomainUser + User.Use&lt;T&gt;() 懒加载——
/// 框架三桥（内置/Hangfire/Quartz）经 GetServices 枚举解析 + BeginSystemScopeAsync（SystemActor 通道），
/// 懒加载在 OnExecutedAsync 调用期内经 Use&lt;T&gt;() 自建 CurrentAopUser 窗口（Oracle Q4 方案 i——扩展侧收敛，不动框架）。</para>
/// </summary>
internal sealed class JobExecutionRecorder : IBackgroundJobExecutionListener
{
    private readonly IDomainUser _user;
    private JobExecutionEntityDataService? _dataService;
    private readonly ILogger<JobExecutionRecorder> _logger;

    private JobExecutionEntityDataService DataService => _dataService ??= _user.Use<JobExecutionEntityDataService>();

    public JobExecutionRecorder(IDomainUser user, ILogger<JobExecutionRecorder> logger)
    {
        _user = user ?? throw new ArgumentNullException(nameof(user));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>执行完成回调——Context→实体映射→DataService 落库（异常静默）。</summary>
    public async Task OnExecutedAsync(BackgroundJobExecutedContext context, CancellationToken ct = default)
    {
        try
        {
            var entity = new JobExecutionEntity
            {
                JobId = context.JobId ?? "",
                JobType = context.JobType ?? "",
                Provider = context.Provider ?? "",
                IsSuccess = context.IsSuccess,
                IsCancelled = context.IsCancelled,
                RetryAttempt = context.RetryAttempt,
                DurationMs = (long)context.Duration.TotalMilliseconds,
                StartedAtUtc = context.StartedAtUtc,
                CompletedAtUtc = context.CompletedAtUtc,
                ErrorText = context.Error,
                TenantId = context.TenantId,
                CreateTime = DateTime.UtcNow
            };

            await DataService.EntityCreateAsync(entity, ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "作业执行历史落库失败: JobId={JobId}, Provider={Provider}", context.JobId, context.Provider);
        }
    }
}
