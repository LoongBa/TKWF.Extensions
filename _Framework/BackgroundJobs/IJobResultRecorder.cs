using System;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 作业内显式记录业务产出接口（V0.1.0）——JobId 从 <see cref="T:TKW.Framework.Domain.BackgroundJobs.BackgroundJobContext"/> Current 读取；无上下文抛明确异常。
/// <para>v0.1.0 无 executionId 参数——JobExecution 后置写入，执行中不可得（Oracle C4 移除，YAGNI）。</para>
/// </summary>
public interface IJobResultRecorder
{
    /// <summary>记录业务产出（JobId 从 BackgroundJobContext.Current 读取；无上下文抛 InvalidOperationException）。</summary>
    /// <param name="resultType">结果类型（默认 "success"，消费方可自定义分类）。</param>
    /// <param name="resultJson">业务产出 JSON。</param>
    /// <param name="summary">摘要（可选）。</param>
    /// <param name="ct">取消令牌。</param>
    /// <returns>落库记录 Id。</returns>
    Task<long> RecordAsync(string resultType, string resultJson, string? summary = null, CancellationToken ct = default);
}
