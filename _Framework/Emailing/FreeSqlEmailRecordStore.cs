using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// FreeSql 邮件记录存储实现——将 <see cref="EmailRecordEntity"/> 持久化到数据库。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。
    /// 与 FreeSqlSettingStore 模式一致。</para>
    /// </summary>
    internal sealed class FreeSqlEmailRecordStore : IEmailRecordStore
    {
        private readonly IFreeSql _freeSql;
        private readonly ILogger<FreeSqlEmailRecordStore> _logger;

        public FreeSqlEmailRecordStore(IFreeSql freeSql, ILogger<FreeSqlEmailRecordStore> logger)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<EmailRecordEntity?> GetAsync(long id, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<EmailRecordEntity>()
                    .Where(e => e.Id == id)
                    .FirstAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "邮件记录读取失败: Id={Id}", id);
                return null;
            }
        }

        public async Task<IReadOnlyList<EmailRecordEntity>> GetListAsync(string? status = null, CancellationToken ct = default)
        {
            try
            {
                var query = _freeSql.Select<EmailRecordEntity>();
                if (!string.IsNullOrEmpty(status))
                {
                    query = query.Where(e => e.Status == status);
                }
                return await query.ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "邮件记录列表读取失败: Status={Status}", status);
                return Array.Empty<EmailRecordEntity>();
            }
        }

        public async Task SaveAsync(EmailRecordEntity entity, CancellationToken ct = default)
        {
            if (entity == null) return;

            try
            {
                if (entity.Id > 0)
                {
                    // 更新：先删后插（确保 SQLite/PostgreSQL 等全兼容）
                    await _freeSql.Delete<EmailRecordEntity>()
                        .Where(e => e.Id == entity.Id)
                        .ExecuteAffrowsAsync(ct);

                    // 重置 Id 为 0 以便重新插入（自增主键）
                    entity.Id = 0;
                    var newId = await _freeSql.Insert(entity).ExecuteIdentityAsync(ct);
                    entity.Id = newId;
                }
                else
                {
                    var newId = await _freeSql.Insert(entity).ExecuteIdentityAsync(ct);
                    entity.Id = newId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "邮件记录保存失败: To={To}, Subject={Subject}", entity.To, entity.Subject);
            }
        }
    }
}
