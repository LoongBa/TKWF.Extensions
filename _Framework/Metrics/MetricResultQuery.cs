using System;

namespace TKWF.Ext.Metrics
{
    /// <summary>
    /// 指标结果查询条件 DTO——消费方 Store 将其映射到自己的 DataService 过滤谓词。
    /// </summary>
    /// <param name="SpecKey">按规格键过滤（精确匹配；null = 不限）。</param>
    /// <param name="Name">按指标名过滤（精确匹配；null = 不限）。</param>
    /// <param name="FromUtc">计算时间下界（闭区间：CalculatedAtUtc &gt;= FromUtc；null = 不限）。</param>
    /// <param name="ToUtc">计算时间上界（闭区间：CalculatedAtUtc &lt;= ToUtc；null = 不限）。</param>
    /// <param name="DimensionFilter">
    /// JSON 维度过滤（可选，如 {"bucket":"2026-08"}）——消费方 Store 按需实现（SQL JSON 函数或内存过滤，
    /// 对齐 Notifications <c>UserNotificationViewDataService</c> 模式）；<b>未实现时返回未按维度过滤的结果</b>
    /// （兼容降级 P1——维度过滤为增强能力，非契约强需求）。
    /// </param>
    /// <param name="Skip">跳过的行数（分页偏移，默认 0）。</param>
    /// <param name="Take">
    /// 返回行数上限（默认 50）。
    /// <para><b>消费方 Store 实现 MUST 钳制到 [1,200]</b>——超界静默钳制为 200、Take&lt;0 视为 0（返回空集）
    /// （分页先例 P4，对齐 BackgroundJobs/AuditLogging 分页语义）；Take=0 由消费方 Store 自行决定（如视为默认值）。
    /// 契约不强制，钳制责任落于消费方 Store 侧。</para>
    /// </param>
    public sealed record MetricResultQuery(
        string? SpecKey = null,
        string? Name = null,
        DateTime? FromUtc = null,
        DateTime? ToUtc = null,
        string? DimensionFilter = null,
        int Skip = 0,
        int Take = 50);
}