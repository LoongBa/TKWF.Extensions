using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志分析服务接口（V0.3.0 统计聚合 + 保留天数清理）——扩展侧自建，不修改主框架。
    /// <para><b>数据访问红线合规</b>：本服务只依赖 <see cref="AuditLogEntityDataService"/>（SG1 DataService）委托——
    /// 绝不注入 IFreeSql / IEntityDAC；异常静默对齐扩展既有模式（失败记 Warning 日志，返回空结果/0，不抛异常）。</para>
    /// <para><b>边界</b>：聚合仅查不改（只读 TopN/统计）；唯一的写路径是保留清理 <see cref="CleanupExpiredAsync"/>
    /// （DataService <c>DeleteExpiredAsync</c> 物理删，限定单点，无管理端点）。</para>
    /// </summary>
    public interface IAuditLogAnalyticsService
    {
        /// <summary>
        /// 窗口内调用次数 TopN（按服务名）。空白 ServiceName 跳过。
        /// 热点服务/性能分析：高频服务 = 潜在性能热点，配合耗时统计定位瓶颈。
        /// </summary>
        /// <param name="topN">返回条数（默认 10，按 Count 降序；&lt;=0 回退默认，上限 100）。</param>
        /// <param name="window">时间窗口（相对 UtcNow 的 [UtcNow - window, UtcNow] 闭区间）；null = 全量（不限制时间）。</param>
        /// <param name="ct">取消令牌。</param>
        Task<IReadOnlyList<AuditLogDimensionCount>> GetTopServicesAsync(
            int topN = 10, TimeSpan? window = null, CancellationToken ct = default);

        /// <summary>
        /// 窗口内调用次数 TopN（按用户名）。UserName 为 null/空白跳过（匿名调用不构成维度）。
        /// 用户行为分析：高频操作用户 = 活跃用户/异常行为排查。
        /// </summary>
        Task<IReadOnlyList<AuditLogDimensionCount>> GetTopUsersAsync(
            int topN = 10, TimeSpan? window = null, CancellationToken ct = default);

        /// <summary>
        /// 窗口内统计（SQL 级聚合）——Total / Succeeded / Failed / AvgDurationMs / MaxDurationMs。
        /// 审计合规/风险分析：失败率（Failed/Total）异常升高 = 潜在攻击或系统故障。
        /// </summary>
        Task<AuditLogStats> GetStatsAsync(TimeSpan? window = null, CancellationToken ct = default);

        /// <summary>
        /// 保留天数清理——删除 <c>ExecutionTime &lt; UtcNow - RetentionDays</c> 的过期记录（分批循环清完）。
        /// 配置见 <see cref="AuditLoggingOptions.RetentionDays"/>（默认 90 天）与
        /// <see cref="AuditLoggingOptions.CleanupBatchSize"/>（默认 500 条/批）。
        /// 返回实际删除总条数。可周期性调用（如每日后台任务）。
        /// </summary>
        Task<int> CleanupExpiredAsync(CancellationToken ct = default);
    }
}