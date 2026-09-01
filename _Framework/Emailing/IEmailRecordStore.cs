using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// 邮件记录存储抽象——定义邮件记录的 CRUD 操作。
    /// <para>V0.1.0 FreeSql 默认实现；后续可扩展 EF Core 等。</para>
    /// </summary>
    public interface IEmailRecordStore
    {
        /// <summary>按 ID 读取单条邮件记录。</summary>
        Task<EmailRecordEntity?> GetAsync(long id, CancellationToken ct = default);

        /// <summary>读取邮件记录列表（按状态过滤，可选）。</summary>
        Task<IReadOnlyList<EmailRecordEntity>> GetListAsync(string? status = null, CancellationToken ct = default);

        /// <summary>保存（插入/更新）邮件记录。</summary>
        Task SaveAsync(EmailRecordEntity entity, CancellationToken ct = default);
    }
}
