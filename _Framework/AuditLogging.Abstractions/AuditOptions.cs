namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计落库失败处置策略。
    /// <para><c>FailOpen=true</c>（默认）：审计写失败不阻断业务调用，但**必须**触发 <see cref="IAuditFailureHook"/>（不静默）。
    /// <c>FailOpen=false</c>：受管场景 fail-closed——审计写失败向上抛出。</para>
    /// </summary>
    public class AuditFailurePolicyOptions
    {
        public bool FailOpen { get; set; } = true;
    }

    /// <summary>
    /// 审计生命周期事件发布开关（默认关）。
    /// <para>开启需宿主同时启用 <c>AddEventDispatch()</c>；未启用派发时事件被静默丢弃（框架既有门控告警）。
    /// 审计级信号另有同步 <see cref="IAuditFailureHook"/> 保底，不依赖事件。</para>
    /// </summary>
    public class AuditEventPublishingOptions
    {
        public bool PublishEvents { get; set; }
    }

    /// <summary>
    /// 审计访问策略配置。
    /// <para><c>AllowWithoutPermissions=false</c>（默认）：Permissions 未启用时敏感操作（QueryAll/Detail/Export/Delete/Purge）一律拒绝（fail-closed）。
    /// 设为 true 则跳过权限校验（仅限无 Permissions 的独立部署，需明确承担风险）。</para>
    /// </summary>
    public class AuditAccessPolicyOptions
    {
        public bool AllowWithoutPermissions { get; set; }
    }
}
