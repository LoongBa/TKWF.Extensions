using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 特权删除（append-only 的唯一例外）——<b>能力注册即存在</b>（C3：无 <c>AllowPrivilegedDelete</c> 开关）。
    /// <para>消费方显式 <c>services.AddAuditPrivilegedDelete()</c> 才注册；调用须 <see cref="AuditOperation.Delete"/> 放行，
    /// 且先写业务审计留痕（含脱敏快照）再删除。</para>
    /// <para>安全日志面（<see cref="AuditFacet.Security"/>）永不支持删除。</para>
    /// </summary>
    public interface IAuditPrivilegedDeleteService : IDomainService
    {
        /// <summary>删除单条审计记录（先留痕后删）。</summary>
        Task<bool> DeleteAsync(AuditFacet facet, long id, string reason, CancellationToken ct = default);
    }
}
