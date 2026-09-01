using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 账户锁定状态存储抽象——锁定状态的读取、保存与删除。
    /// <para>由扩展默认 FreeSql 实现（<see cref="FreeSqlAccountLockoutStore"/>），消费方可自定义（TryAdd 语义）。</para>
    /// </summary>
    public interface IAccountLockoutStore
    {
        /// <summary>读取指定用户的锁定记录（不存在返回 null）。</summary>
        Task<AccountLockoutEntity?> GetAsync(string userName, CancellationToken ct = default);

        /// <summary>保存锁定记录（新增或更新）。</summary>
        Task SaveAsync(AccountLockoutEntity record, CancellationToken ct = default);

        /// <summary>删除指定用户的锁定记录。</summary>
        Task DeleteAsync(string userName, CancellationToken ct = default);
    }
}