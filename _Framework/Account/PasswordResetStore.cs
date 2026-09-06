using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 密码重置码存储实现——经 <see cref="PasswordResetCodeEntityDataService"/>（SG1/xCodeGen 生成的 DataService）
    /// 委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class PasswordResetStore : IPasswordResetStore
    {
        private readonly PasswordResetCodeEntityDataService _dataService;
        private readonly ILogger<PasswordResetStore> _logger;

        public PasswordResetStore(PasswordResetCodeEntityDataService dataService, ILogger<PasswordResetStore> logger)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PasswordResetCodeEntity?> GetAsync(string userName, string resetCode, CancellationToken ct = default)
        {
            try
            {
                return await _dataService.GetByUserNameAndCodeAsync(userName, resetCode, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "密码重置码读取失败: UserName={UserName}", userName);
                return null;
            }
        }

        public async Task SaveAsync(PasswordResetCodeEntity record, CancellationToken ct = default)
        {
            if (record == null) return;

            try
            {
                await _dataService.SaveAsync(record, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "密码重置码保存失败: UserName={UserName}", record.UserName);
            }
        }

        public async Task MarkUsedAsync(long id, CancellationToken ct = default)
        {
            try
            {
                await _dataService.MarkUsedAsync(id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "密码重置码标记已用失败: Id={Id}", id);
            }
        }
    }
}
