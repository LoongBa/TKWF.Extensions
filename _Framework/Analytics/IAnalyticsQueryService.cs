#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Analytics;

/// <summary>
/// 分析查询服务门面——读取 AnalysisSpec（semantic_types / chart_spec / manifest JSON 原样透传）+ 校验 spec 状态（manifest）。
/// <para>职责边界（D20 §5.1）：**不做 ChartAssemblyInput 组装**（前端直调 flint assemble）；**不查业务数据**（由 tkwf-service 提供）。
/// 组合核心层 <see cref="TKW.Framework.Utility.Analytics.AnalyticsSpecLoader"/> + <see cref="TKW.Framework.Utility.Analytics.AnalyticsSpecValidator"/>。</para>
/// <para>扩展层（非 Utility 核心）：可经 DI 注入 <see cref="Microsoft.Extensions.Options.IOptions{T}"/>（对齐 Metrics 集成层先例）。</para>
/// </summary>
public interface IAnalyticsQueryService
{
    /// <summary>读取 specKey 的 semantic_types（JSON 原样透传，不镜像强类型；stale → SpecStaleException）</summary>
    Task<JsonDocument> GetSemanticTypesAsync(string domain, string specKey, CancellationToken ct = default);

    /// <summary>读取 specKey 指定 chartType 的 chart_spec（chartType 非法/缺失 → 返回主图 primaryChartSpecFile；stale → SpecStaleException）</summary>
    Task<JsonDocument> GetChartSpecAsync(string domain, string specKey, string? chartType, CancellationToken ct = default);

    /// <summary>列出 specKey 下可切换图型（读取 pivot-hint.json allowedTransitions；文件不存在 → 空列表。实际可切换性由 flint 前端按数据形态门控裁决——此列表仅为 UI 提示）</summary>
    Task<IReadOnlyList<string>> ListPivotChartTypesAsync(string domain, string specKey, CancellationToken ct = default);

    /// <summary>读取 specKey 的 manifest（不校验 status——前端读 status 字段展示 stale/deprecated）</summary>
    Task<JsonDocument> GetManifestAsync(string domain, string specKey, CancellationToken ct = default);
}
