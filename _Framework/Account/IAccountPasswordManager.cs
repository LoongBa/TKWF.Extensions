using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 密码落地抽象——消费方实现，将重置后的新密码散列（客户端已计算 PBKDF2）写入用户存储。
    /// <para>扩展不提供默认实现：用户存储属于消费方（可适配 Identity 的 <c>IUserManager</c>）。
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