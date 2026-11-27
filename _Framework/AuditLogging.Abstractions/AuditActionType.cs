namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 常用审计动作常量。<b>动作类型开放</b>：允许自由字符串，约定命名 <c>{domain}.{entity}.{verb}</c>
    /// （避免每加业务动词就改框架枚举，P3 反向）。
    /// </summary>
    public static class AuditActionType
    {
        public const string Created = "Created";
        public const string Updated = "Updated";
        public const string Deleted = "Deleted";
        public const string StatusChanged = "StatusChanged";
        public const string Granted = "Granted";
        public const string Revoked = "Revoked";
        public const string Exported = "Exported";
        public const string Purged = "Purged";
        public const string Bound = "Bound";
        public const string Unbound = "Unbound";
        public const string Login = "Login";
        public const string Logout = "Logout";
        public const string Reset = "Reset";
        public const string Configured = "Configured";
    }
}
