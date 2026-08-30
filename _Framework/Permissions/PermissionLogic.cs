namespace TKWF.Ext.Permissions
{
    /// <summary>权限检查逻辑（同一 <see cref="RequirePermissionAttribute"/> 内多个权限名的判定方式）。</summary>
    public enum PermissionLogic
    {
        /// <summary>所有权限必须授予（默认）。</summary>
        All,
        /// <summary>任意一个权限授予即可。</summary>
        Any
    }
}
