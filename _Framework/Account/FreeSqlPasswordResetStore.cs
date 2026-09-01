using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// FreeSql 密码重置码存储实现——将 <see cref="PasswordResetCodeEntity"/> 持久化到数据库。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// </summary>
    internal sealed class FreeSqlPasswordResetStore : IPasswordResetStore
    {
        private readonly IFreeSql _freeSql;
        private readonly ILogger<FreeSqlPasswordResetStore> _logger;

        public FreeSqlPasswordResetStore(IFreeSql freeSql, ILogger<FreeSqlPasswordResetStore> logger)
        {
            _freeSql = freeSql ?? throw new ArgumentNullException(nameof(freeSql));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<PasswordResetCodeEntity?> GetAsync(string userName, string resetCode, CancellationToken ct = default)
        {
            try
            {
                return await _freeSql.Select<PasswordResetCodeEntity>()
                    .Where(p => p.UserName == userName && p.ResetCode == resetCode)
                    .FirstAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "重置码读取失败: UserName={UserName}", userName);
                return null;
            }
        }

        public async Task SaveAsync(PasswordResetCodeEntity record, CancellationToken ct = default)
        {
            if (record == null) return;

            try
            {
                var newId = await _freeSql.Insert(record).ExecuteIdentityAsync(ct);
                record.Id = newId;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "重置码保存失败: UserName={UserName}", record.UserName);
            }
        }

        public async Task MarkUsedAsync(long id, CancellationToken ct = default)
        {
            try
            {
                // 单字段更新（Id 保持不变；SQLite 下单字段 Set+Where 可靠，先删后插会改变自增 Id 不可用）
                await _freeSql.Update<PasswordResetCodeEntity>()
                    .Set(p => p.IsUsed, true)
                    .Where(p => p.Id == id)
                    .ExecuteAffrowsAsync(ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "重置码标记使用失败: Id={Id}", id);
            }
        }
    }
}