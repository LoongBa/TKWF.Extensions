using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 业务结果查询服务（V0.1.0）——委托 <see cref="JobResultEntityDataService"/> 实现最新/分页查询。
/// <para>异常静默：查询失败返回空/默认值，不阻断消费方。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI——旧 TryAddScoped 构造注入 IDomainUser 生产解析必失败）；DataService 经
/// <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；注册形态改 <c>AddConstructibleService</c>（接口可构造守卫工厂 +
/// 实现类 throw-factory，消费方统一 <c>User.Use&lt;IJobResultQueryService&gt;()</c> 解析）。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class JobResultQueryService : DomainServiceBase, IJobResultQueryService
{
    private JobResultEntityDataService? _dataService;
    private readonly ILogger<JobResultQueryService> _logger;

    private JobResultEntityDataService DataService => _dataService ??= User.Use<JobResultEntityDataService>();

    public JobResultQueryService(IDomainUser user, ILogger<JobResultQueryService> logger)
        : base(user)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>分页过滤查询（JobId/ResultType/时间范围）——Total 为过滤后总条数（SQL COUNT）。</summary>
    public async Task<JobResultPagedResult> GetListAsync(JobResultQueryInput input, CancellationToken ct = default)
    {
        try
        {
            var skip = Math.Max(0, input.Skip);
            var take = Math.Clamp(input.Take, 1, 200);

            var total = await DataService.CountAsync(
                input.JobId, input.ResultType,
                input.StartFromUtc, input.StartToUtc, ct);

            var entities = await DataService.GetListAsync(
                input.JobId, input.ResultType,
                input.StartFromUtc, input.StartToUtc,
                skip, take, ct);

            var items = entities.Select(e => new JobResultListItemDto
            {
                Id = e.Id,
                JobId = e.JobId,
                ResultType = e.ResultType,
                ResultJson = e.ResultJson,
                Summary = e.Summary,
                CreateTime = e.CreateTime
            }).ToList();

            return new JobResultPagedResult((int)total, skip, take, items);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "业务结果分页查询失败");
            return new JobResultPagedResult(0, input.Skip, input.Take, []);
        }
    }

    /// <summary>按 JobId 查最新一条结果。</summary>
    public async Task<JobResultListItemDto?> GetLatestAsync(string jobId, CancellationToken ct = default)
    {
        try
        {
            var entity = await DataService.GetLatestByJobIdAsync(jobId, ct);
            if (entity == null) return null;

            return new JobResultListItemDto
            {
                Id = entity.Id,
                JobId = entity.JobId,
                ResultType = entity.ResultType,
                ResultJson = entity.ResultJson,
                Summary = entity.Summary,
                CreateTime = entity.CreateTime
            };
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "业务结果最新查询失败: JobId={JobId}", jobId);
            return null;
        }
    }
}
