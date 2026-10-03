using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.BackgroundJobs;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 业务结果记录器（V0.1.0）——<see cref="IJobResultRecorder"/> 实现，
/// JobId 从 <see cref="BackgroundJobContext.Current"/> 读取，委托 <see cref="JobResultEntityDataService"/> 落库。
/// <para>异常静默 + ILogger.Warning——作业内记录失败不阻断作业执行。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI——旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败）；DataService 经
/// <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；注册形态改 <c>AddConstructibleService</c>（接口可构造守卫工厂 +
/// 实现类 throw-factory，消费方统一 <c>User.Use&lt;IJobResultRecorder&gt;()</c> 解析）。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class JobResultRecorder : DomainServiceBase, IJobResultRecorder
{
    private JobResultEntityDataService? _dataService;
    private readonly ILogger<JobResultRecorder> _logger;

    private JobResultEntityDataService DataService => _dataService ??= User.Use<JobResultEntityDataService>();

    public JobResultRecorder(IDomainUser user, ILogger<JobResultRecorder> logger)
        : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>记录业务产出——JobId 从 BackgroundJobContext.Current 读取；无上下文抛 InvalidOperationException。</summary>
    public async Task<long> RecordAsync(string resultType, string resultJson, string? summary = null, CancellationToken ct = default)
    {
        var context = BackgroundJobContext.Current
            ?? throw new InvalidOperationException(
                "无法记录业务结果：当前没有 BackgroundJobContext（必须在后台作业执行上下文内调用 IJobResultRecorder.RecordAsync）");

        var entity = new JobResultEntity
        {
            JobId = context.JobId ?? "",
            ResultType = resultType ?? "success",
            ResultJson = resultJson,
            Summary = summary,
            CreateTime = DateTime.UtcNow
        };

        try
        {
            var result = await DataService.EntityCreateAsync(entity, ct);
            return result.Id;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "业务结果落库失败: JobId={JobId}, ResultType={ResultType}", context.JobId, resultType);
            return 0;
        }
    }
}
