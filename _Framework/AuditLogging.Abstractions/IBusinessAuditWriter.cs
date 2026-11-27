using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 业务事实审计写入门面（<b>接线型</b>，非 <c>IDomainService</c>）。
    /// <para><b>注册</b>：<c>TryAddScoped</c>（ctor <c>IServiceProvider</c>+<c>ILogger</c>+<c>IOptions</c>，懒解析 DataService）。</para>
    /// <para><b>消费</b>：域帧 <c>User.GetService&lt;IBusinessAuditWriter&gt;()</c>（<c>Use&lt;T&gt;()</c> 约束 <c>: IDomainService</c>，本门面为接线型不适用）；
    /// 系统帧/后台任务 <c>provider.GetRequiredService&lt;IBusinessAuditWriter&gt;()</c>。</para>
    /// <para>写失败语义：默认 fail-open（不阻断业务）+ 同步 <see cref="IAuditFailureHook"/> 必触发（不静默）。</para>
    /// </summary>
    public interface IBusinessAuditWriter
    {
        /// <summary>追加写入一条业务审计事实。</summary>
        Task SaveAsync(BusinessAuditEntry entry, CancellationToken ct = default);

        /// <summary>批量追加写入。</summary>
        Task SaveBatchAsync(IReadOnlyList<BusinessAuditEntry> entries, CancellationToken ct = default);

        /// <summary>
        /// 严格追加写入——写失败<b>必定抛出</b>（不受 <c>FailOpen</c> 影响），用于特权删除等"留痕失败即中止"的场景。
        /// </summary>
        Task SaveStrictAsync(BusinessAuditEntry entry, CancellationToken ct = default);
    }
}
