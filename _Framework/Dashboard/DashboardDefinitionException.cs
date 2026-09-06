using System;

namespace TKWF.Ext.Dashboard
{
    /// <summary>
    /// Dashboard 配置/定义异常——定义文件缺失、JSON 损坏、契约校验失败、Metrics 依赖未接线时抛出。
    /// <para>严格失败优于静默产出错误数据（对齐 Metrics 失败哲学）。</para>
    /// </summary>
    public class DashboardDefinitionException : Exception
    {
        /// <summary>相关 Dashboard 名（dashKey）。</summary>
        public string? DashKey { get; }

        /// <summary>相关 Widget 名。</summary>
        public string? WidgetName { get; }

        /// <summary>原因。</summary>
        public string Reason { get; }

        public DashboardDefinitionException(string reason)
            : base(reason)
        {
            Reason = reason;
        }

        public DashboardDefinitionException(string? dashKey, string? widgetName, string reason)
            : base($"Dashboard 定义错误{(dashKey != null ? $" [{dashKey}]" : "")}{(widgetName != null ? $" Widget '{widgetName}'" : "")}：{reason}")
        {
            DashKey = dashKey;
            WidgetName = widgetName;
            Reason = reason;
        }
    }
}
