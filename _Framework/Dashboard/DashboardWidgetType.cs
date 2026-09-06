namespace TKWF.Ext.Dashboard
{
    /// <summary>
    /// Widget 类型——对齐 ABP Low-Code 三类型；数据来源 = Metrics 指标结果或消费方数据源。
    /// </summary>
    public enum DashboardWidgetType
    {
        /// <summary>KPI 单值卡片（如"今日营业额"）——响应填充 <see cref="WidgetDataResult.Value"/>。</summary>
        NumberContainer,

        /// <summary>图表（趋势/分布）——响应填充 <see cref="WidgetDataResult.Slices"/>（多切片展开）或单值。</summary>
        Chart,

        /// <summary>明细列表——响应填充 <see cref="WidgetDataResult.Rows"/>（数据行）。</summary>
        List
    }
}
