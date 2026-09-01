namespace TKWF.Ext.Permissions.Abstractions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W2)：权限授予结果——<see cref="IPermissionStore.GetAsync"/> 的返回值。
    /// </summary>
    public sealed class PermissionGrantResult
    {
        private PermissionGrantResult(bool isGranted) { IsGranted = isGranted; }

        /// <summary>是否已授予。</summary>
        public bool IsGranted { get; }

        /// <summary>已授予。</summary>
        public static PermissionGrantResult Granted { get; } = new(true);

        /// <summary>未授予。</summary>
        public static PermissionGrantResult Denied { get; } = new(false);
    }
}
