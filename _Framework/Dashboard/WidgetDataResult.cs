using System.Collections.Generic;

namespace TKWF.Ext.Dashboard
{
    /// <summary>
    /// Widget 数据响应 DTO——统一三类输出形态（Oracle C3）。
    /// <para>填充规则：<c>numberContainer</c> → <see cref="Value"/>（KPI 单值）+ <see cref="Unit"/>；
    /// <c>chart</c> → <see cref="Slices"/>（多切片展开，Cohort/TimeBucket/Funnel）或 <see cref="Value"/>（单值指标）；
    /// <c>list</c> → <see cref="Rows"/>（明细数据行）。消费方按 <see cref="Type"/> 分支解析。</para>
    /// </summary>
    public sealed record WidgetDataResult(
        string WidgetName,
        DashboardWidgetType Type,
        string? MetricName,                                  // metricRef 计算时非空
        object? Value,                                       // numberContainer：单值；chart：单值指标
        string? Unit,                                        // 指标单位
        IReadOnlyDictionary<string, object?>? Dimensions,    // 单切片维度
        IReadOnlyList<WidgetDataResult>? Slices,             // chart：多切片展开
        IReadOnlyList<object>? Rows);                        // list：明细数据行
}
