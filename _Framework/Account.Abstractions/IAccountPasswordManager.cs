using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 密码落地抽象——消费方实现（V0.3.0 起 Identity 扩展提供 <c>IdentityPasswordManager</c> 默认实现），
    /// 将重置后的新密码散列（客户端已计算 PBKDF2）写入用户存储。
    /// <para>接口定义于 <c>TKWF.Ext.Account.Abstractions</c>（ADR48 D7 依赖倒置）——命名空间保持
    /// <c>TKWF.Ext.Account</c>（Oracle P2-1c：既有接口迁移不破消费方 using）。
    /// 未注册时 <c>DefaultPasswordResetFlow.InitiateResetAsync</c> 返回 false 并记录 Warning。</para>
    /// </summary>
    public interface IAccountPasswordManager
    {
        /// <summary>用户是否存在（用于防用户枚举）。</summary>
        Task<bool> UserExistsAsync(string userName, CancellationToken ct = default);

        /// <summary>设置用户密码（newClientHash = 客户端计算的 PBKDF2 散列，salt = 盐）。</summary>
        Task<bool> SetPasswordAsync(string userName, string newClientHash, string salt, CancellationToken ct = default);
    }
}
