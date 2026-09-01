using System.Threading.Tasks;

namespace TKWF.Ext.Permissions.Abstractions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W2)：权限存储——持久化角色/用户权限值（对齐 D17 §5.1.2）。
    /// <para>V4.9.72 仅定义接口 + 默认 NoOp 实现；真实持久化（数据库/权限分配管理）留后续迭代。</para>
    /// <para>V4.9.85 (ADR48 D7)：迁移至 Abstractions 项目（依赖倒置）。</para>
    /// </summary>
    public interface IPermissionStore
    {
        /// <summary>读取权限授予结果（按提供者：角色/用户等）。</summary>
        Task<PermissionGrantResult> GetAsync(string permissionName, string providerName, string providerKey);

        /// <summary>写入权限授予值。</summary>
        Task SetAsync(string permissionName, string providerName, string providerKey, bool isGranted);
    }
}
