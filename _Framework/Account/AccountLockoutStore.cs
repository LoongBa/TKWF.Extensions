using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 账户锁定存储实现——经 <see cref="AccountLockoutEntityDataService"/>（SG1/xCodeGen 生成的 DataService）
    /// 委托持久化，遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常静默处理：操作失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。</para>
    /// <para>V4.10.53（领域自治根治，正确路线）：继承 <see cref="DomainServiceBase"/>（非泛型主实现）——
    /// 经基类 <c>User</c> 获取用户上下文（IDomainUser 永不注册 DI——D01；旧 TryAddScoped 构造注入 IDomainUser
    /// 生产解析必失败）。DataService 仍经 <c>User.Use&lt;AccountLockoutEntityDataService&gt;()</c> NoAop 懒加载。
    /// 注册形态改 <c>AddConstructibleService&lt;IAccountLockoutStore, AccountLockoutStore&gt;</c>（接口可构造守卫工厂）。</para>
    /// </summary>
    [DiContractIgnore]
    internal sealed class AccountLockoutStore : DomainServiceBase, IAccountLockoutStore
    {
        private readonly ILogger<AccountLockoutStore> _logger;

        private AccountLockoutEntityDataService? _dataService;
        private AccountLockoutEntityDataService DataService => _dataService ??= User.Use<AccountLockoutEntityDataService>();

        public AccountLockoutStore(IDomainUser user, ILogger<AccountLockoutStore> logger)
            : base(user)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<AccountLockoutEntity?> GetAsync(string userName, CancellationToken ct = default)
        {
            try
            {
                return await DataService.GetByUserNameAsync(userName, ct);
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
                await DataService.UpsertAsync(record, ct);
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
                var existing = await DataService.GetByUserNameAsync(userName, ct);
                if (existing != null)
                    await DataService.DeleteAsync(existing.Id, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "账户锁定记录删除失败: UserName={UserName}", userName);
            }
        }
    }
}