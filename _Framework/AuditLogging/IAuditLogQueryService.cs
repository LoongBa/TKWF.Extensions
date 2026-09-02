using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志查询服务接口——提供按条件分页查询审计日志的能力。
    /// <para>V0.2.0 新增。查询接口由扩展侧定义（不修改主框架 <see cref="TKW.Framework.Domain.Interception.Auditing.IAuditLogStore"/>），
    /// 返回 DTO 列表（不暴露实体）。</para>
    /// </summary>
    public interface IAuditLogQueryService
    {
        /// <summary>
        /// 按条件分页查询审计日志列表。
        /// </summary>
        /// <param name="query">查询条件（所有字段可选，空条件 = 全量分页）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>分页结果（含总数 + 当前页 DTO 列表）。</returns>
        Task<AuditLogPagedResult> GetListAsync(AuditLogQueryInput query, CancellationToken ct = default);

        /// <summary>
        /// 按条件统计审计日志总数。
        /// </summary>
        /// <param name="query">查询条件（所有字段可选，空条件 = 全量统计）。</param>
        /// <param name="ct">取消令牌。</param>
        /// <returns>符合条件的总记录数。</returns>
        Task<long> CountAsync(AuditLogQueryInput query, CancellationToken ct = default);
    }
}
