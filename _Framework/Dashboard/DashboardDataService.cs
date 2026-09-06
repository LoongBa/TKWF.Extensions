using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Utility.Metrics;

namespace TKWF.Ext.Dashboard
{
    /// <summary>
    /// 仪表盘门面实现——定义加载 → 数据源取数 →（可选）Metrics 计算 → 组装 <see cref="WidgetDataResult"/>。
    /// <para>Scoped 生命周期（按请求）。Metrics 消费走 <see cref="IMetricCalculator"/> 直连（Oracle C2：
    /// 绕过 <c>CalculateAsync&lt;T&gt;</c> 泛型摩擦，<see cref="MetricRow"/> 由本类构造并注入数据源 accessor）。</para>
    /// </summary>
    internal sealed class DashboardDataService : IDashboardDataService
    {
        private readonly DashboardSpecFileProvider _specProvider;
        private readonly IEnumerable<IDashboardDataProvider> _providers;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DashboardDataService> _logger;

        public DashboardDataService(
            DashboardSpecFileProvider specProvider,
            IEnumerable<IDashboardDataProvider> providers,
            IServiceProvider serviceProvider,
            ILogger<DashboardDataService> logger)
        {
            _specProvider = specProvider ?? throw new ArgumentNullException(nameof(specProvider));
            _providers = providers ?? throw new ArgumentNullException(nameof(providers));
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public Task<DashboardDefinition> GetDashboardDefinitionAsync(string dashKey, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            return Task.FromResult(_specProvider.LoadDashboard(dashKey));
        }

        /// <inheritdoc />
        public async Task<WidgetDataResult> GetWidgetDataAsync(
            string dashKey,
            string widgetName,
            IReadOnlyDictionary<string, object?>? filters = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var definition = _specProvider.LoadDashboard(dashKey);
            var widget = definition.Widgets?.FirstOrDefault(w => w.Name == widgetName)
                ?? throw new DashboardDefinitionException(dashKey, widgetName, "Widget 未找到");

            // 1. 数据源取数（dataSource 必选）
            var provider = _providers.FirstOrDefault(p => p.Name == widget.DataSource)
                ?? throw new DashboardDefinitionException(dashKey, widgetName, $"数据源 '{widget.DataSource}' 未注册（消费方须实现 IDashboardDataProvider 并注入）");

            var (rows, accessor) = await provider.GetDataAsync(widgetName, filters, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();

            // 2. metricRef 可选：对数据行做 Metrics 计算
            if (string.IsNullOrWhiteSpace(widget.MetricRef))
            {
                // dataSource 直连输出（list 明细）
                return new WidgetDataResult(widget.Name, widget.Type, null, null, null, null, null, rows);
            }

            var results = CalculateMetrics(widget.MetricRef, rows, accessor, ct);
            return AssembleMetricResult(widget, results);
        }

        /// <summary>
        /// 解析 metricRef（"domain/specKey:metricName" 或 "domain/specKey"）→ 加载规格 → 筛选 → 逐指标计算。
        /// </summary>
        private IReadOnlyList<MetricResult> CalculateMetrics(
            string metricRef,
            IReadOnlyList<object> rows,
            Func<object, string, object?> accessor,
            CancellationToken ct)
        {
            var (domain, specKey, metricName) = ParseMetricRef(metricRef);

            // Oracle C9：Metrics 扩展未启用（IMetricCalculatorFactory 未注册）→ 显式失败
            var factory = _serviceProvider.GetService<IMetricCalculatorFactory>();
            if (factory == null)
                throw new DashboardDefinitionException(null, null,
                    $"metricRef '{metricRef}' 需要 Metrics 扩展——消费方须同时 [TKWFEnabledExtension(typeof(TKWF.Ext.Metrics.MetricsExtensionInitializer<>))] 启用");

            var definitions = _specProvider.LoadMetricsSpec(domain, specKey);
            var selected = metricName != null
                ? definitions.Where(d => d.Name == metricName).ToArray()
                : definitions.ToArray();

            if (metricName != null && selected.Length == 0)
                throw new DashboardDefinitionException(null, null, $"指标 '{metricName}' 未在规格 '{domain}/{specKey}' 中定义");

            // 构造 MetricRow（注入数据源 accessor——Oracle C2）
            var metricRows = rows.Select(r => new MetricRow(r, accessor)).ToArray();

            var results = new List<MetricResult>(selected.Length);
            foreach (var definition in selected)
            {
                ct.ThrowIfCancellationRequested();
                var calculator = factory.TryCreate(definition.Calculator)
                    ?? throw new DashboardDefinitionException(null, definition.Name,
                        $"计算器 '{definition.Calculator}' 未注册（Metrics 内置计算器或消费方自定义）");
                results.Add(calculator.Calculate(metricRows, definition));
            }
            return results;
        }

        /// <summary>
        /// 组装 WidgetDataResult——单指标单值 → Value；单指标多切片（Cohort/TimeBucket/Funnel）→ Slices；全量多指标 → Slices。
        /// </summary>
        private static WidgetDataResult AssembleMetricResult(
            DashboardWidgetDefinition widget,
            IReadOnlyList<MetricResult> results)
        {
            var widgetResult = new WidgetDataResult(widget.Name, widget.Type, null, null, null, null, null, null);

            if (results.Count == 1)
            {
                var single = results[0];
                if (single.Value is IReadOnlyList<MetricSlice> slices && slices.Count > 0)
                {
                    // 多切片计算器 → Slices 展开
                    var sliceResults = slices.Select(s => new WidgetDataResult(
                        widget.Name, widget.Type, single.Name, s.Value, single.Unit, s.Dimensions, null, null)).ToArray();
                    return widgetResult with { MetricName = single.Name, Slices = sliceResults };
                }

                // 单值指标（numberContainer / chart 单值）
                return widgetResult with
                {
                    MetricName = single.Name,
                    Value = single.Value,
                    Unit = single.Unit,
                    Dimensions = single.Dimensions
                };
            }

            // 全量模式（多个指标）→ 每个指标一个 Slices 元素（chart 多指标展示）
            var multiResults = results.Select(r => new WidgetDataResult(
                widget.Name, widget.Type, r.Name, r.Value, r.Unit, r.Dimensions, null, null)).ToArray();
            return widgetResult with { MetricName = results[0].Name, Slices = multiResults };
        }

        /// <summary>解析 metricRef："domain/specKey:metricName" 或 "domain/specKey"（全量）。</summary>
        private static (string Domain, string SpecKey, string? MetricName) ParseMetricRef(string metricRef)
        {
            if (string.IsNullOrWhiteSpace(metricRef))
                throw new DashboardDefinitionException(null, null, "metricRef 不能为空");

            var metricName = (string?)null;
            var refBody = metricRef;
            var colonIndex = metricRef.LastIndexOf(':');
            if (colonIndex > 0)
            {
                metricName = metricRef[(colonIndex + 1)..];
                refBody = metricRef[..colonIndex];
            }

            var slashIndex = refBody.IndexOf('/');
            if (slashIndex <= 0 || slashIndex == refBody.Length - 1)
                throw new DashboardDefinitionException(null, null,
                    $"metricRef 格式非法（须 'domain/specKey:metricName' 或 'domain/specKey'）：{metricRef}");

            var domain = refBody[..slashIndex];
            var specKey = refBody[(slashIndex + 1)..];
            return (domain, specKey, metricName);
        }
    }
}
