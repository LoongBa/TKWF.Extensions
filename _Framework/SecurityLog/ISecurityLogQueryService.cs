using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志查询服务接口——分页/过滤查询安全事件（扩展侧自建，不修改主框架）。
    /// <para>安全决策：列表 DTO（<see cref="SecurityLogListItemDto"/>）不含 <c>Detail</c> 全文（对齐 AuditLogging
    /// 不含 ArgumentsJson 先例——Detail 含异常消息，列表不拉大字段/敏感信息）；详情经 <see cref="GetDetailAsync"/> 取全量。</para>
    /// </summary>
    public interface ISecurityLogQueryService
    {
        /// <summary>按条件分页查询安全事件列表（Take 默认 50 上限 200；列表 DTO 不含 Detail）。</summary>
        /// <param name="input">查询条件（所有过滤字段可选，空条件 = 全量分页）。</param>
        /// <param name="ct">取消令牌。</param>
        Task<SecurityLogPagedResult> GetListAsync(SecurityLogQueryInput input, CancellationToken ct = default);

        /// <summary>按 Id 查询安全事件详情（含 Detail 全文）；不存在返回 null。</summary>
        Task<SecurityLogDetailDto?> GetDetailAsync(long id, CancellationToken ct = default);

        /// <summary>按条件统计安全事件总数。</summary>
        Task<long> CountAsync(SecurityLogQueryInput input, CancellationToken ct = default);
    }
}
