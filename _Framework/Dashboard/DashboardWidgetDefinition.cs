namespace TKWF.Ext.Dashboard
{
    /// <summary>
    /// Widget 定义——布局（row/order/width）+ 数据源（dataSource 必选）+ 计算引用（metricRef 可选）。
    /// <para>契约（Oracle C5/C2）：<c>dataSource</c> 必选（消费方 <see cref="IDashboardDataProvider"/> 取数源）；
    /// <c>metricRef</c> 可选——指定则对 dataSource 返回的数据行做 Metrics 计算（"取数 → 计算"链式，非互斥二选一）。</para>
    /// </summary>
    /// <param name="Name">Widget 名（Dashboard 内唯一）。</param>
    /// <param name="Type">Widget 类型。</param>
    /// <param name="Row">行号（布局）。</param>
    /// <param name="Order">列内顺序（布局）。</param>
    /// <param name="Width">宽度（布局，1-6）。</param>
    /// <param name="DataSource">消费方 <see cref="IDashboardDataProvider"/> 名（必选）。</param>
    /// <param name="MetricRef">Metrics 指标引用——格式 <c>"domain/specKey:metricName"</c> 或 <c>"domain/specKey"</c>（全量，Oracle C1 路径含 domain）。</param>
    public sealed record DashboardWidgetDefinition(
        string Name,
        DashboardWidgetType Type,
        int Row,
        int Order,
        int Width,
        string DataSource,
        string? MetricRef = null);
}
