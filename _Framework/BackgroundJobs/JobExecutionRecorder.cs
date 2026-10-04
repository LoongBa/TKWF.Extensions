using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain;
using TKW.Framework.Domain.BackgroundJobs;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 作业执行监听器（V0.1.0）——<see cref="IBackgroundJobExecutionListener"/> 实现，
/// Context→实体映射→注入 <see cref="JobExecutionEntityDataService"/> 落库。
/// <para>异常静默 + ILogger.Warning——对齐契约语义：监听器异常绝不遮蔽业务异常、不阻断状态更新。</para>
/// <para>V4.10.55（ADR92/T3 特例收编，主框架 Core 契约边界）：<b>ctor 移除 IDomainUser</b>——
/// <c>IBackgroundJobExecutionListener</c> 非 IDomainService（<c>TryAddEnumerableConstructible</c> 约束
/// <c>where TInterface : IDomainService</c> 不满足，无法经守卫工厂供给）且三桥（内置/Hangfire/Quartz）
/// 经 <c>GetServices</c> 裸枚举构造（无 Use 帧）——旧 ctor(IDomainUser)（IDomainUser 永不注册 DI）生产解析必失败。
/// 改为泛型 <c>JobExecutionRecorder&lt;TUserInfo&gt;</c> + 接线型 ctor(<see cref="IDomainUserAccessor{TUserInfo}"/>, ILogger)：
/// DataService 经 <c>IDomainUserAccessor&lt;TUserInfo&gt;.DomainUser</c>（三桥 <c>BeginSystemScopeAsync</c> System 帧内
/// 已设 <see cref="StandaloneDomainUserAccessor{TUserInfo}"/>——QuartzJobBridge L88 实证）NoAop 直建——
/// 审计记录器不依赖业务用户身份（JobExecutionEntity 无执行者列；BackgroundJobExecutedContext 亦无执行者字段——
/// 框架 §6.4.3"取执行者身份"建议因扩展实体模型无此列而不适用，以 SystemUser 落库等价实现）。</para>
/// <para>注册方式：<c>TryAddEnumerable(ServiceDescriptor.Scoped&lt;IBackgroundJobExecutionListener,
/// JobExecutionRecorder&lt;TUserInfo&gt;&gt;())</c>，多监听器可叠加（Oracle C3，与契约一致）；
/// <see cref="IDomainUserAccessor{TUserInfo}"/> 由 Initializer 注册
/// <see cref="StandaloneDomainUserAccessor{TUserInfo}"/> 实现（TryAddSingleton 接线型，三桥 System 帧内可解析）。</para>
/// </summary>
internal sealed class JobExecutionRecorder<TUserInfo> : IBackgroundJobExecutionListener
    where TUserInfo : class, IUserInfo, new()
{
    private readonly IDomainUserAccessor<TUserInfo> _userAccessor;
    private JobExecutionEntityDataService? _dataService;
    private readonly ILogger<JobExecutionRecorder<TUserInfo>> _logger;

    private JobExecutionEntityDataService DataService
    {
        get
        {
            if (_dataService != null) return _dataService;
            // 三桥 System 帧内已设 StandaloneDomainUserAccessor（BeginSystemScopeAsync 双通道绑定）——
            // 取 SystemUser 经 NoAop 直建 DataService（IEntityDAC 由 DI 解析，User.Use 直建不依赖容器注册）
            var systemUser = _userAccessor.DomainUser;
            return _dataService = systemUser.Use<JobExecutionEntityDataService>();
        }
    }

    public JobExecutionRecorder(
        IDomainUserAccessor<TUserInfo> userAccessor,
        ILogger<JobExecutionRecorder<TUserInfo>> logger)
    {
        _userAccessor = userAccessor ?? throw new ArgumentNullException(nameof(userAccessor));
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