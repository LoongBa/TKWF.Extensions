using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 密码重置码存储抽象——重置码的读取、保存与标记已用。
    /// <para>由扩展默认 FreeSql 实现（<see cref="FreeSqlPasswordResetStore"/>），消费方可自定义（TryAdd 语义）。</para>
    /// </summary>
    public interface IPasswordResetStore
    {
        /// <summary>读取指定用户名 + 重置码的记录（不存在返回 null）。</summary>
        Task<PasswordResetCodeEntity?> GetAsync(string userName, string resetCode, CancellationToken ct = default);

        /// <summary>保存重置码记录。</summary>
        Task SaveAsync(PasswordResetCodeEntity record, CancellationToken ct = default);

        /// <summary>标记重置码已使用（幂等）。</summary>
        Task MarkUsedAsync(long id, CancellationToken ct = default);
    }
}