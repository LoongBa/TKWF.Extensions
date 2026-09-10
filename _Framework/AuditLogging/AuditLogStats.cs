namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志统计（V0.3.0 统计聚合）——<see cref="IAuditLogAnalyticsService.GetStatsAsync"/> 输出项。
    /// <para>SQL 级聚合产出（对齐 BackgroundJobs <c>GetStatsAsync</c> 范式）：计数 SQL COUNT(*)、耗时 SQL AVG/MAX。</para>
    /// </summary>
    /// <param name="Total">窗口内审计记录总数。</param>
    /// <param name="Succeeded">窗口内成功记录数（<c>Success == true</c>）。</param>
    /// <param name="Failed">窗口内失败记录数（<c>Success == false</c>）。</param>
    /// <param name="AvgDurationMs">窗口内平均执行耗时（毫秒，SQL AVG；空窗口为 0）。</param>
    /// <param name="MaxDurationMs">窗口内最大执行耗时（毫秒，SQL MAX；空窗口为 0）。</param>
    public sealed record AuditLogStats(long Total, long Succeeded, long Failed, double AvgDurationMs, long MaxDurationMs);
}