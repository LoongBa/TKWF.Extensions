using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 账户锁定存储实现——经 <see cref="AccountLockoutEntityDataService"/>（SG1/xCodeGen 生成的 DataService）
    /// 委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class AccountLockoutStore : IAccountLockoutStore
    {
        private readonly AccountLockoutEntityDataService _dataService;
        private readonly ILogger<AccountLockoutStore> _logger;

        public AccountLockoutStore(AccountLockoutEntityDataService dataService, ILogger<AccountLockoutStore> logger)
        {
            _dataService = dataService ?? throw new ArgumentNullException(nameof(dataService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AccountLockoutEntity?> GetAsync(string userName, CancellationToken ct = default)
        {
            try
            {
                return await _dataService.GetByUserNameAsync(userName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "账户锁定记录读取失败: UserName={UserName}", userName);
                return null;
            }
        }

        public async Task SaveAsync(AccountLockoutEntity record, CancellationToken ct = default)
        {
            if (record == null) return;

            try
            {
                await _dataService.UpsertAsync(record, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "账户锁定记录保存失败: UserName={UserName}", record.UserName);
            }
        }

        public async Task DeleteAsync(string userName, CancellationToken ct = default)
        {
            try
            {
                var existing = await _dataService.GetByUserNameAsync(userName, ct);
                if (existing != null)
                    await _dataService.DeleteAsync(existing.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "账户锁定记录删除失败: UserName={UserName}", userName);
            }
        }
    }
}
