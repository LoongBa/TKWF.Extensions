using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Dashboard
{
    /// <summary>
    /// 仪表盘门面——定义查询 + Widget 数据查询。
    /// <para>数据流：定义加载（JSON 描述符）→ 数据源取数（<see cref="IDashboardDataProvider"/>）→
    /// （可选）Metrics 计算（<c>metricRef</c> → <see cref="TKW.Framework.Utility.Metrics.IMetricCalculator"/>）→
    /// 组装 <see cref="WidgetDataResult"/> 响应。</para>
    /// </summary>
    public interface IDashboardDataService
    {
        /// <summary>按 dashKey 查询 Dashboard 定义（JSON 描述符反序列化）。</summary>
        /// <exception cref="DashboardDefinitionException">定义文件缺失/损坏/契约校验失败。</exception>
        Task<DashboardDefinition> GetDashboardDefinitionAsync(string dashKey, CancellationToken ct = default);

        /// <summary>按 dashKey + widgetName 查询 Widget 数据（数据源取数 + 可选 Metrics 计算）。</summary>
        /// <exception cref="DashboardDefinitionException">Widget 缺失/数据源缺失/规格解析失败。</exception>
        Task<WidgetDataResult> GetWidgetDataAsync(
            string dashKey,
            string widgetName,
            IReadOnlyDictionary<string, object?>? filters = null,
            CancellationToken ct = default);
    }
}
