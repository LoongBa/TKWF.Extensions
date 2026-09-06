using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// 邮件记录存储实现——经 <see cref="EmailRecordEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class EmailRecordStore : IEmailRecordStore
    {
        private readonly EmailRecordEntityDataService _dataService;
        private readonly ILogger<EmailRecordStore> _logger;

        public EmailRecordStore(EmailRecordEntityDataService dataService, ILogger<EmailRecordStore> logger)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<EmailRecordEntity?> GetAsync(long id, CancellationToken ct = default)
        {
            try
            {
                return await _dataService.GetEntityByIdAsync(id, ct);
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
                return await _dataService.GetListByStatusAsync(status, ct);
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
                await _dataService.UpsertAsync(entity, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "邮件记录保存失败: To={To}, Subject={Subject}", entity.To, entity.Subject);
            }
        }
    }
}
