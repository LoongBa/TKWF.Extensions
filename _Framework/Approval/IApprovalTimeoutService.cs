using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批超时处理服务——扫描超期任务并按步骤配置动作自动处理（钉钉 5 动作模型）。
/// <para>不内建调度器——由消费方经框架 IBackgroundJob/ITimerService 周期调用 <see cref="ProcessTimeoutTasksAsync"/>
/// （BackgroundJobs 扩展可观察执行历史）。v0.2.0 扩展不依赖后台线程。</para>
/// </summary>
public interface IApprovalTimeoutService
{
    /// <summary>
    /// 处理全部超期任务（Pending &amp;&amp; TimeoutAt&lt;=now &amp;&amp; !TimeoutProcessed）。
    /// <para>P3 扫描即占位：每任务先条件更新 TimeoutProcessed=true（影响行数=0 跳过——防双实例重复执行），
    /// 再按 TimeoutAction 执行（Remind 事件 / Transfer 转交 / Jump 跳转 / Approve 通过 / Reject 驳回；
    /// 系统身份经 ApprovalManager internal 方法，审计 system:timeout 标识，C1/D15）。</para>
    /// </summary>
    /// <param name="ct">取消令牌。</param>
    /// <returns>本次处理的任务数（占位成功数）。</returns>
    Task<int> ProcessTimeoutTasksAsync(CancellationToken ct = default);
}
