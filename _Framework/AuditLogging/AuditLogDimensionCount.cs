namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 调用次数聚合统计（V0.3.0 统计聚合）——<see cref="IAuditLogAnalyticsService"/> 输出项。
    /// <para>由 <see cref="AuditLogEntityDataService.CountByServiceAsync"/>（<paramref name="Dimension"/> = ServiceName）
    /// / <see cref="AuditLogEntityDataService.CountByUserAsync"/>（<paramref name="Dimension"/> = UserName）聚合产出——
    /// 按 Count 降序。</para>
    /// </summary>
    /// <param name="Dimension">聚合维度值（服务名 / 用户名）。</param>
    /// <param name="Count">窗口内调用次数。</param>
    public sealed record AuditLogDimensionCount(string Dimension, long Count);
}