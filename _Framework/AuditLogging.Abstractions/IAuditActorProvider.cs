namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 可选 Actor 自动派生扩展点。
    /// <para>默认实现返回 <c>null</c> → 强制调用方显式传入 <see cref="AuditActor"/>（P5 显式优先，
    /// 不依赖 <c>IDomainUser</c>/框架内部环境访问器）。</para>
    /// </summary>
    public interface IAuditActorProvider
    {
        /// <summary>尝试派生当前操作者；返回 <c>null</c> 表示无法派生（调用方必须显式提供）。</summary>
        AuditActor? ResolveActor();
    }
}
