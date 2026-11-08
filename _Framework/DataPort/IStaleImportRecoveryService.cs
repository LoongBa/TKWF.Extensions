using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.DataPort
{
    /// <summary>
    /// Processing 超时恢复门面（DP-05 V0.1.5）——重置超时 Processing → Failed（CAS 条件更新，防 scan→mark 间隙覆写终态）。
    /// <para>对齐 <see cref="IStaleImportRecoveryService"/> 消费形态：AddConstructibleService 注册，消费方经 <c>User.Use&lt;IStaleImportRecoveryService&gt;()</c> 解析。</para>
    /// <para>调度形态：扩展不内建调度器——Initializer 启动扫描兜底（进程崩溃重启即治愈）+ 文档给消费方周期调度接线示例
    /// （对齐 BackgroundJobs <c>IJobHistoryCleanupService</c> 先例；<c>ProcessingTimeoutMinutes</c> 置 0 可禁用）。</para>
    /// </summary>
    public interface IStaleImportRecoveryService : IDomainService
    {
        /// <summary>
        /// 重置超时 Processing → Failed（错误摘要标记超时原因）；返回恢复条数。
        /// <para>异常静默（ILogger.Warning），零破坏——单条恢复失败不阻断其余，整体异常返回 0。</para>
        /// </summary>
        Task<int> RecoverStaleImportsAsync(CancellationToken ct = default);
    }
}