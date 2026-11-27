namespace TKWF.Ext.AuditLogging
{
    /// <summary>审计操作者类型。</summary>
    public enum AuditActorType
    {
        /// <summary>已认证用户。</summary>
        User = 0,

        /// <summary>系统（框架系统作用域）。</summary>
        System = 1,

        /// <summary>后台作业/定时任务。</summary>
        Job = 2,

        /// <summary>服务间调用。</summary>
        Service = 3,

        /// <summary>匿名调用。</summary>
        Anonymous = 4,
    }

    /// <summary>
    /// 审计操作者（一等公民模型）。<b>Actor 必填显式传入</b>（P5 显式优于自动）；
    /// 系统帧用 <see cref="System(string?, string?)"/>，后台作业用 <see cref="Job(string, string?)"/>。
    /// <para>可选 <see cref="IAuditActorProvider"/> 由消费方注册做自动派生；默认不派生 → 强制显式。</para>
    /// </summary>
    public sealed record AuditActor(AuditActorType Type, string? Id, string? DisplayName = null)
    {
        /// <summary>系统操作者（Id 缺省 "system"）。</summary>
        public static AuditActor System(string? id = null, string? displayName = null)
            => new(AuditActorType.System, id ?? "system", displayName ?? "system");

        /// <summary>后台作业操作者（Id = "job:{name}"）。</summary>
        public static AuditActor Job(string name, string? displayName = null)
            => new(AuditActorType.Job, "job:" + name, displayName ?? name);

        /// <summary>用户操作者。</summary>
        public static AuditActor User(string id, string? displayName = null)
            => new(AuditActorType.User, id, displayName);

        /// <summary>服务操作者。</summary>
        public static AuditActor Service(string id, string? displayName = null)
            => new(AuditActorType.Service, id, displayName);

        /// <summary>匿名操作者。</summary>
        public static AuditActor Anonymous() => new(AuditActorType.Anonymous, null, null);
    }
}
