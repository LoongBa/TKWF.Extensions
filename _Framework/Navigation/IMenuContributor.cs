namespace TKWF.Ext.Navigation
{
    /// <summary>
    /// V4.9.74 (扩展机制业务模块 W2)：菜单贡献者——业务模块实现此接口贡献菜单项（对齐 D17 §5.2.2）。
    /// <para>实现类用 <see cref="MenuContributorAttribute"/> 标记，SG1 扫描发现并生成注册表；
    /// 扩展初始化器在 ConfigureServices 阶段实例化并调用 <see cref="ConfigureMenu"/> 收集菜单项。</para>
    /// <para><b>同步化决策（Oracle H1，V4.9.74 修正 D17 §5.2.2）</b>：本方法为同步 void——
    /// <c>ExtensionInitializer.ConfigureServices</c> 是同步钩子（DI 构建前），无法 await 异步贡献者；
    /// 菜单贡献者是纯声明式（<c>context.Add(new MenuItemDefinition{...})</c>），无异步诉求，
    /// 对齐 <c>IPermissionDefinitionContributor.Define()</c>（同步）。</para>
    /// <code>
    /// [MenuContributor]
    /// public class MainMenuContributor : IMenuContributor
    /// {
    ///     public void ConfigureMenu(MenuConfigurationContext context)
    ///     {
    ///         context.Add(new MenuItemDefinition { Name = "Orders", DisplayName = "订单", Url = "/orders" });
    ///     }
    /// }
    /// </code>
    /// </summary>
    public interface IMenuContributor
    {
        /// <summary>向上下文声明本模块的菜单项（同步——ConfigureServices 阶段调用）。</summary>
        void ConfigureMenu(MenuConfigurationContext context);
    }
}