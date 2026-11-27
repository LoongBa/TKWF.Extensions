using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>审计导出格式。</summary>
    public enum AuditExportFormat
    {
        /// <summary>CSV（UTF-8 无 BOM，首行表头）。</summary>
        Csv = 0,

        /// <summary>JSON Lines（每行一记录）。</summary>
        JsonLines = 1,
    }

    /// <summary>
    /// 审计导出（G6）——按面 keyset 流式导出，避免一次性载入内存。
    /// <para>门面无 <c>[GenerateController]</c>（C6）；暴露面由 Web 层受控端点承载。</para>
    /// </summary>
    public interface IAuditExportService : IDomainService
    {
        /// <summary>导出指定面（受 <paramref name="query"/> 过滤）到 <paramref name="output"/>，返回导出条数。</summary>
        Task<long> ExportAsync(
            AuditFacet facet,
            AuditFacetQuery query,
            AuditExportFormat format,
            Stream output,
            CancellationToken ct = default);
    }
}
