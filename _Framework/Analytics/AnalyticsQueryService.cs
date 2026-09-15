#nullable enable
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Utility.Analytics;

namespace TKWF.Ext.Analytics;

/// <summary>
/// 分析查询服务门面实现——组合核心层 <see cref="AnalyticsSpecLoader"/>（加载 + manifest 状态校验）。
/// <para>无状态（Loader 只读文件系统 + 校验）→ Singleton 安全；spec 文件 git-tracked 设计期固化，运行期零 LLM。</para>
/// </summary>
public sealed class AnalyticsQueryService : IAnalyticsQueryService
{
    private readonly AnalyticsSpecLoader _loader;

    public AnalyticsQueryService(AnalyticsSpecLoader loader)
    {
        _loader = loader;
    }

    /// <inheritdoc />
    public Task<JsonDocument> GetSemanticTypesAsync(string domain, string specKey, CancellationToken ct = default)
        => _loader.LoadSemanticTypesAsync(domain, specKey, ct);

    /// <inheritdoc />
    public Task<JsonDocument> GetChartSpecAsync(string domain, string specKey, string? chartType, CancellationToken ct = default)
        => _loader.LoadChartSpecAsync(domain, specKey, chartType, ct);

    /// <inheritdoc />
    public Task<IReadOnlyList<string>> ListPivotChartTypesAsync(string domain, string specKey, CancellationToken ct = default)
        => _loader.LoadPivotHintAsync(domain, specKey, ct);

    /// <inheritdoc />
    public Task<JsonDocument> GetManifestAsync(string domain, string specKey, CancellationToken ct = default)
        => _loader.LoadManifestAsync(domain, specKey, ct);
}
