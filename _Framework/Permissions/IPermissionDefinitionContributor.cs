namespace TKWF.Ext.Permissions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W2)：权限定义贡献者——业务模块实现此接口声明权限（对齐 D17 §5.1.2）。
    /// <para>实现类用 <see cref="PermissionContributorAttribute"/> 标记，SG1 扫描发现并生成注册表；
    /// 扩展初始化器在 ConfigureServices 阶段实例化并调用 <see cref="Define"/> 收集权限定义。</para>
    /// <code>
    /// [PermissionContributor]
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
