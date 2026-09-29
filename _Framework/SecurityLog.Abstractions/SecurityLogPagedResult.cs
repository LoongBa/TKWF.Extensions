using System.Collections.Generic;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志分页查询结果——包含总数与当前页 DTO 列表。
    /// </summary>
    /// <param name="Total">符合条件的总记录数（用于前端分页控件）。</param>
    /// <param name="Items">当前页 DTO 列表（不含 Detail 全文）。</param>
    public record SecurityLogPagedResult(long Total, IReadOnlyList<SecurityLogListItemDto> Items);
}
