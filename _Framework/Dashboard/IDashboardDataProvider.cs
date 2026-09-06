using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Dashboard
{
    /// <summary>
    /// 数据源抽象——消费方实现，返回 Widget 数据行 + 字段访问器。
    /// <para>Oracle C2：返回 <c>(rows, accessor)</c> 元组——accessor 随行返回，规避 Metrics 泛型 T 丢失
    /// （Dashboard 直接消费 <see cref="TKW.Framework.Utility.Metrics.IMetricCalculator"/> 时用 accessor 构造
    /// <see cref="TKW.Framework.Utility.Metrics.MetricRow"/>，绕过引擎按 T 编译访问器的限制）。</para>
    /// </summary>
    public interface IDashboardDataProvider
    {
        /// <summary>数据源名（与 <see cref="DashboardWidgetDefinition.DataSource"/> 匹配）。</summary>
        string Name { get; }

        /// <summary>
        /// 取数——返回数据行列表 + 字段访问委托。
        /// </summary>
        /// <param name="widgetName">请求的 Widget 名（数据源可为不同 Widget 返回不同数据）。</param>
        /// <param name="filters">全局过滤（日期范围等，透传给数据源；指标计算作用于过滤后的数据行）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>数据行列表 + 字段访问委托 <c>(source, fieldName) → value</c>（字段缺失返回 null 或抛异常）。</returns>
        Task<(IReadOnlyList<object> Rows, Func<object, string, object?> Accessor)> GetDataAsync(
            string widgetName,
            IReadOnlyDictionary<string, object?>? filters,
            CancellationToken ct = default);
    }
}
