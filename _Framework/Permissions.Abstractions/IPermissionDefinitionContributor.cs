namespace TKWF.Ext.Permissions.Abstractions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W2)：权限定义贡献者——业务模块实现此接口声明权限（对齐 D17 §5.1.2）。
    /// <para>V4.10.31 (A+ 阶段 3)：SG1 经接口继承关系发现贡献者（不再用 <c>[PermissionContributor]</c> 特性，
    /// 特性已删除）；扩展初始化器在 ConfigureServices 阶段实例化并调用 <see cref="Define"/> 收集权限定义。</para>
    /// <para>V4.9.85 (ADR48 D7)：迁移至 Abstractions 项目（依赖倒置）。</para>
    /// <code>
    /// public class OrderPermissions : IPermissionDefinitionContributor
    /// {
    ///     public void Define(PermissionDefinitionContext context)
    ///     {
    ///         context.Add(new PermissionDefinition { Name = "Order.Create", DisplayName = "创建订单" });
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface IPermissionDefinitionContributor
    {
        /// <summary>向上下文声明本模块的权限定义。</summary>
        void Define(PermissionDefinitionContext context);
    }
}
