using System.Collections.Generic;
using System.Threading.Tasks;

namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W2)：权限检查器——运行时判断用户是否有权限（对齐 D17 §5.1.2）。
    /// <para>由 <see cref="PermissionFilterAttribute{TUserInfo}"/> 在 PreProceed 阶段调用。</para>
    /// </summary>
    public interface IPermissionChecker
    {
        /// <summary>检查当前用户是否拥有指定权限。</summary>
        Task<bool> IsGrantedAsync(string permissionName);

        /// <summary>批量检查权限，返回 权限名 → 是否授予 字典。</summary>
        Task<Dictionary<string, bool>> IsGrantedAsync(params string[] permissionNames);
    }
}
