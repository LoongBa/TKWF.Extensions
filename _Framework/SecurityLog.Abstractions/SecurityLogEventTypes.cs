namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志事件字符串常量——收敛 9 事件类型 / 2 结果 / 2 事件分类字面量，
    /// 供采集过滤器（<c>SecurityLogFilterAttribute&lt;TUserInfo&gt;</c>）、聚合查询（<c>GetTopFailedByUserAsync</c>/
    /// <c>GetTopFailedByIpAsync</c> 的 <c>Result=="Failed"</c> 过滤）、扩展直写（AuthCenter 冻结/解冻——
    /// ADR-AuthCenter-密码策略与口令协议 决策 7）与实体注释引用，避免魔法字符串散落。
    /// </summary>
    public static class SecurityLogEventTypes
    {
        // ── 事件类型（9）──

        /// <summary>登录（LoginByPasswordAsync / LoginByContextAsync）。</summary>
        public const string Login = "Login";

        /// <summary>登出（LogoutAsync）。</summary>
        public const string Logout = "Logout";

        /// <summary>修改密码（ChangePasswordSecureAsync）。</summary>
        public const string PasswordChange = "PasswordChange";

        /// <summary>重置密码（InitiateResetAsync / CompleteResetAsync）。</summary>
        public const string PasswordReset = "PasswordReset";

        /// <summary>账户锁定（LoginByContextAsync 锁定分支——异常消息含"锁定"关键字判定）。</summary>
        public const string Lockout = "Lockout";

        /// <summary>注册（RegisterSecureAsync）。</summary>
        public const string Register = "Register";

        /// <summary>挑战（RequestChallengeAsync）。</summary>
        public const string Challenge = "Challenge";

        /// <summary>账号冻结（V0.1.0+——AuthCenter 冻结门面直写 <c>ISecurityLogStore</c>，ADR-密码策略与口令协议 决策 3/7）。
        /// <para><c>UserName</c> 填被冻结账号（非操作者），操作者身份放 <c>Detail</c>；分类 <see cref="CategoryAuthentication"/>。</para></summary>
        public const string Freeze = "Freeze";

        /// <summary>账号解冻（同上——AuthCenter 解冻门面直写）。</summary>
        public const string Unfreeze = "Unfreeze";

        // ── 结果（2）──

        /// <summary>结果：成功。</summary>
        public const string ResultSuccess = "Success";

        /// <summary>结果：失败。</summary>
        public const string ResultFailed = "Failed";

        // ── 事件分类（2）──

        /// <summary>事件分类：认证（v0.1.0 采集事件均为 Authentication）。</summary>
        public const string CategoryAuthentication = "Authentication";

        /// <summary>事件分类：授权（预留值，v0.1.0 未采集）。</summary>
        public const string CategoryAuthorization = "Authorization";
    }
}