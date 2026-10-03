using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.AuthController;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 账户锁定策略默认实现——<see cref="IAccountLockoutPolicy"/>（主框架 V4.9.45 扩展点）。
    /// <para>基于 <see cref="IAccountLockoutStore"/>（FreeSql）持久化失败计数与锁定截止时间；
    /// 框架 AuthController&lt;TUserInfo&gt;.LoginByContext 已注册时自动调用。</para>
    /// <para>V4.10.53（领域自治根治，正确路线）：**接线型**（skill §4.2）——显式 userName 参数、无需用户上下文，
    /// 不继承 <see cref="TKW.Framework.Domain.DomainServiceBase"/>、不注入 <see cref="TKW.Framework.Domain.Interfaces.IDomainUser"/>。
    /// 主框架契约 <see cref="IAccountLockoutPolicy"/> 非 IDomainService（不可修改主框架），AuthController 经普通
    /// DI（<c>GetOptionalService</c>）解析——旧 ctor 注入 IDomainUser（永不注册 DI）致 GetService 构造失败、
    /// 锁定检查静默失效（真实生产故障）；改后 ctor 全为 DI 可解析基础设施 → GetService 可构造、锁定检查生效。</para>
    /// <para><see cref="IAccountLockoutStore"/> 经 <see cref="IServiceProvider"/> 延迟解析（C1 模式，对齐
    /// LoginHistoryService/NotificationPublisher）——Store 现经 <c>AddConstructibleService</c> 注册（接口可构造守卫工厂），
    /// Policy 不持用户上下文，改普通 DI 解析 Store（接线型边界不吞守卫语义，读取失败仍异常静默降级）。</para>
    /// </summary>
    internal sealed class FreeSqlAccountLockoutPolicy : IAccountLockoutPolicy
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly AccountOptions _options;
        private readonly ILogger<FreeSqlAccountLockoutPolicy> _logger;

        private IAccountLockoutStore? _store;
        private IAccountLockoutStore Store => _store ??= _serviceProvider.GetRequiredService<IAccountLockoutStore>();

        public FreeSqlAccountLockoutPolicy(
            IServiceProvider serviceProvider,
            IOptions<AccountOptions> options,
            ILogger<FreeSqlAccountLockoutPolicy> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
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
                var record = await Store.GetAsync(userName, ct);
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
                var record = await Store.GetAsync(userName, ct)
                    ?? new AccountLockoutEntity { UserName = userName };

                record.FailedCount = record.FailedCount + 1;
                record.LastFailedTime = DateTime.Now;

                if (record.FailedCount >= _options.MaxFailedAttempts)
                {
                    record.LockoutEnd = DateTime.Now.AddMinutes(_options.DefaultLockoutMinutes);
                    _logger.LogWarning("账户已锁定: UserName={UserName}, 失败次数={Count}, 解锁时间={End}",
                        userName, record.FailedCount, record.LockoutEnd);
                }

                await Store.SaveAsync(record, ct);
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
                await Store.DeleteAsync(userName, ct);
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
                await Store.DeleteAsync(userName, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "账户解锁失败: UserName={UserName}", userName);
            }
        }
    }
}