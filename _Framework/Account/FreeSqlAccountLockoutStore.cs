using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// FreeSql 账户锁定存储实现——将 <see cref="AccountLockoutEntity"/> 持久化到数据库。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class FreeSqlAccountLockoutStore : IAccountLockoutStore
    {
        private readonly IFreeSql _freeSql;
        private readonly ILogger<FreeSqlAccountLockoutStore> _logger;

        public FreeSqlAccountLockoutStore(IFreeSql freeSql, ILogger<FreeSqlAccountLockoutStore> logger)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AccountLockoutEntity?> GetAsync(string userName, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<AccountLockoutEntity>()
                    .Where(a => a.UserName == userName)
                    .FirstAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "锁定记录读取失败: UserName={UserName}", userName);
                return null;
            }
        }

        public async Task SaveAsync(AccountLockoutEntity record, CancellationToken ct = default)
        {
            if (record == null) return;

            try
            {
                if (record.Id > 0)
                {
                    // 更新：先删后插（确保 SQLite/PostgreSQL 等全兼容——与 FreeSqlBlobRecordStore 同模式）
                    await _freeSql.Delete<AccountLockoutEntity>()
                        .Where(a => a.Id == record.Id)
                        .ExecuteAffrowsAsync(ct);

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
                _logger.LogWarning(ex, "锁定记录保存失败: UserName={UserName}", record.UserName);
            }
        }

        public async Task DeleteAsync(string userName, CancellationToken ct = default)
        {
            try
            {
                await _freeSql.Delete<AccountLockoutEntity>()
                    .Where(a => a.UserName == userName)
                    .ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "锁定记录删除失败: UserName={UserName}", userName);
            }
        }
    }
}