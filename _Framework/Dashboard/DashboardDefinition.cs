using System.Collections.Generic;

namespace TKWF.Ext.Dashboard
{
    /// <summary>
    /// Dashboard 定义——名称 + 标题 + 分组 + Widget 列表（JSON 描述符反序列化目标）。
    /// <para>文件组织（Oracle C8）：<c>{TKWF:Dashboard:SpecRoot}/{group}/{dashKey}.json</c>（group 缺省 = 扁平 <c>{SpecRoot}/{dashKey}.json</c>）。</para>
    /// </summary>
    public sealed record DashboardDefinition(
        string Name,
        string Title,
        string? Group = null,
        IReadOnlyList<DashboardWidgetDefinition>? Widgets = null);
}
