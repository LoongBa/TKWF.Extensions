using System.Collections.Generic;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志分页查询结果——包含总数与当前页 DTO 列表。
    /// <para>主框架无分页接口，扩展自建。</para>
    /// </summary>
    /// <param name="Total">符合条件的总记录数（用于前端分页控件）。</param>
    /// <param name="Items">当前页 DTO 列表。</param>
    public record AuditLogPagedResult(long Total, IReadOnlyList<AuditLogListItemDto> Items);
}
