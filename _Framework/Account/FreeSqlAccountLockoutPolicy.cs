using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.AuthController;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 账户锁定策略默认实现——<see cref="IAccountLockoutPolicy"/>（主框架 V4.9.45 扩展点）。
    /// <para>基于 <see cref="IAccountLockoutStore"/>（FreeSql）持久化失败计数与锁定截止时间；
    /// 框架 AuthController<typeparamref name="TUserInfo"/>.LoginByContext 已注册时自动调用。</para>
    /// </summary>
    internal sealed class FreeSqlAccountLockoutPolicy : IAccountLockoutPolicy
    {
        private readonly IAccountLockoutStore _store;
        private readonly AccountOptions _options;
        private readonly ILogger<FreeSqlAccountLockoutPolicy> _logger;

        public FreeSqlAccountLockoutPolicy(
            IAccountLockoutStore store,
            IOptions<AccountOptions> options,
            ILogger<FreeSqlAccountLockoutPolicy> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _options = options?.Value ?? new AccountOptions();
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// 检查账户是否锁定（LockoutEnd &gt; Now）。
        /// <para>注：FreeSql SQLite 将 DateTime 按本地时间存储（读回 Kind=Unspecified），
        /// 统一用本地时间语义比较。</para>
        /// </summary>
        public async Task<bool> IsLockedAsync(string userName, CancellationToken ct = default)
        {
            try
            {
                var record = await _store.GetAsync(userName, ct);
                return record?.LockoutEnd is { } end && end > DateTime.Now;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "锁定检查失败: UserName={UserName}", userName);
                return false; // 静默回退：检查失败视为未锁定（不阻塞登录）
            }
        }

        /// <summary>登录失败：递增失败计数，超过阈值设置锁定截止时间（默认锁 15 分钟）。</summary>
        public async Task OnFailedLoginAsync(string userName, CancellationToken ct = default)
        {
            try
            {
                var record = await _store.GetAsync(userName, ct)
                    ?? new AccountLockoutEntity { UserName = userName };

                record.FailedCount = record.FailedCount + 1;
                record.LastFailedTime = DateTime.Now;

                if (record.FailedCount >= _options.MaxFailedAttempts)
                {
                    record.LockoutEnd = DateTime.Now.AddMinutes(_options.DefaultLockoutMinutes);
                    _logger.LogWarning("账户已锁定: UserName={UserName}, 失败次数={Count}, 解锁时间={End}",
                        userName, record.FailedCount, record.LockoutEnd);
                }

                await _store.SaveAsync(record, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "失败计数更新失败: UserName={UserName}", userName);
            }
        }

        /// <summary>登录成功：清除失败计数与锁定状态。</summary>
        public async Task OnSuccessfulLoginAsync(string userName, CancellationToken ct = default)
        {
            try
            {
                await _store.DeleteAsync(userName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "成功登录状态清理失败: UserName={UserName}", userName);
            }
        }

        /// <summary>管理员手动解锁：删除锁定记录。</summary>
        public async Task UnlockAsync(string userName, CancellationToken ct = default)
        {
            try
            {
                await _store.DeleteAsync(userName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "账户解锁失败: UserName={UserName}", userName);
            }
        }
    }
}