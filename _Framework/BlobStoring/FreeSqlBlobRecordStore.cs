using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// FreeSql Blob 记录存储实现——将 <see cref="BlobRecordEntity"/> 持久化到数据库。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。
    /// 与 FreeSqlSettingStore / FreeSqlEmailRecordStore 模式一致。</para>
    /// </summary>
    internal sealed class FreeSqlBlobRecordStore : IBlobRecordStore
    {
        private readonly IFreeSql _freeSql;
        private readonly ILogger<FreeSqlBlobRecordStore> _logger;

        public FreeSqlBlobRecordStore(IFreeSql freeSql, ILogger<FreeSqlBlobRecordStore> logger)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<BlobRecordEntity?> GetAsync(long id, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<BlobRecordEntity>()
                    .Where(r => r.Id == id)
                    .FirstAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 记录读取失败: Id={Id}", id);
                return null;
            }
        }

        public async Task<BlobRecordEntity?> GetByNameAsync(string name, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<BlobRecordEntity>()
                    .Where(r => r.Name == name)
                    .FirstAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 记录按名称读取失败: Name={Name}", name);
                return null;
            }
        }

        public async Task<IReadOnlyList<BlobRecordEntity>> GetListAsync(
            string? contentType = null,
            int skip = 0,
            int take = 20,
            CancellationToken ct = default)
        {
            try
            {
                var query = _freeSql.Select<BlobRecordEntity>();
                if (!string.IsNullOrEmpty(contentType))
                {
                    query = query.Where(r => r.ContentType == contentType);
                }
                return await query.Skip(skip).Take(take).ToListAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 记录列表读取失败: ContentType={ContentType}", contentType);
                return Array.Empty<BlobRecordEntity>();
            }
        }

        public async Task SaveAsync(BlobRecordEntity record, CancellationToken ct = default)
        {
            if (record == null) return;

            try
            {
                if (record.Id > 0)
                {
                    // 更新：先删后插（确保 SQLite/PostgreSQL 等全兼容）
                    await _freeSql.Delete<BlobRecordEntity>()
                        .Where(r => r.Id == record.Id)
                        .ExecuteAffrowsAsync(ct);

                    // 重置 Id 为 0 以便重新插入（自增主键）
                    record.Id = 0;
                    var newId = await _freeSql.Insert(record).ExecuteIdentityAsync(ct);
                    record.Id = newId;
                }
                else
                {
                    var newId = await _freeSql.Insert(record).ExecuteIdentityAsync(ct);
                    record.Id = newId;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 记录保存失败: Name={Name}", record.Name);
            }
        }

        public async Task DeleteAsync(long id, CancellationToken ct = default)
        {
            try
            {
                await _freeSql.Delete<BlobRecordEntity>()
                    .Where(r => r.Id == id)
                    .ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 记录删除失败: Id={Id}", id);
            }
        }
    }
}
