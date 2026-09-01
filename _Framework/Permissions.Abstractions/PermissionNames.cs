namespace TKWF.Ext.Permissions.Abstractions
{
    /// <summary>
    /// V0.7.0：权限系统内置权限名常量。
    /// <para>系统权限（如 <see cref="AdminAll"/>）是隐式定义——不依赖 <see cref="IPermissionDefinitionContributor"/> 声明，
    /// <see cref="PermissionChecker{TUserInfo}"/> 在权限名校验之前先行判定：拥有者对所有权限放行。</para>
    /// <para>V4.9.85 (ADR48 D7)：迁移至 Abstractions 项目（依赖倒置）。</para>
    /// </summary>
    public static class PermissionNames
    {
        /// <summary>
        /// 系统管理员通配权限：拥有者（用户或任一角色）对所有已定义权限放行。
        /// <para>授予方式：与普通权限相同——<c>("User", userId)</c> 或 <c>("Role", roleName)</c> 授予
        /// <c>Admin.All</c> 即为系统管理员。种子初始化默认预置 admin 角色。</para>
        /// </summary>
        public const string AdminAll = "Admin.All";
    }
}
