namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计权限键常量（对齐 Permissions 扩展）。Permissions 未启用时由 <see cref="IAuditAccessPolicy"/> fail-closed 兜底。
    /// </summary>
    public static class AuditPermissionNames
    {
        /// <summary>基础查询（本域）。</summary>
        public const string Query = "AuditLogging.Query";

        /// <summary>全量查询（跨域）。</summary>
        public const string QueryAll = "AuditLogging.QueryAll";

        /// <summary>读取详情（含敏感字段）。</summary>
        public const string Detail = "AuditLogging.Detail";

        /// <summary>导出。</summary>
        public const string Export = "AuditLogging.Export";

        /// <summary>特权删除。</summary>
        public const string Delete = "AuditLogging.Delete";

        /// <summary>保留期清理。</summary>
        public const string Purge = "AuditLogging.Purge";
    }
}
