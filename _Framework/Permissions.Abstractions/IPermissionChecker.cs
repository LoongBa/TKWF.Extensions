using System.Collections.Generic;
using System.Threading.Tasks;

namespace TKWF.Ext.Permissions.Abstractions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W2)：权限检查器——运行时判断用户是否有权限（对齐 D17 §5.1.2）。
    /// <para>由 <see cref="PermissionFilterAttribute{TUserInfo}"/> 在 PreProceed 阶段调用。</para>
    /// <para>V4.9.85 (ADR48 D7)：迁移至 Abstractions 项目（依赖倒置），替代 Navigation→Permissions 实现耦合。</para>
    /// </summary>
    public interface IPermissionChecker
    {
        /// <summary>检查当前用户是否拥有指定权限。</summary>
        Task<bool> IsGrantedAsync(string permissionName);

        /// <summary>批量检查权限，返回 权限名 → 是否授予 字典。</summary>
        Task<Dictionary<string, bool>> IsGrantedAsync(params string[] permissionNames);
    }
}
