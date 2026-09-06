using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.BlobStoring
{
    /// <summary>
    /// Blob 记录存储实现——经 <see cref="BlobRecordEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class BlobRecordStore : IBlobRecordStore
    {
        private readonly BlobRecordEntityDataService _dataService;
        private readonly ILogger<BlobRecordStore> _logger;

        public BlobRecordStore(BlobRecordEntityDataService dataService, ILogger<BlobRecordStore> logger)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<BlobRecordEntity?> GetAsync(long id, CancellationToken ct = default)
        {
            try
            {
                return await _dataService.GetEntityByIdAsync(id, ct);
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
                return await _dataService.GetByNameAsync(name, ct);
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
                return await _dataService.GetListByContentTypeAsync(contentType, skip, take, ct);
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
                // 直接委托 DataService Upsert——按 Id 查存在：存在则更新（保留自增 Id），不存在则插入
                await _dataService.UpsertAsync(record, ct);
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
                await _dataService.DeleteByIdAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Blob 记录删除失败: Id={Id}", id);
            }
        }
    }
}
