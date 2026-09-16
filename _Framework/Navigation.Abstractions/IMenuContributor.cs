namespace TKWF.Ext.Navigation
{
    /// <summary>
    /// V4.9.74 (扩展机制业务模块 W2)：菜单贡献者——业务模块实现此接口贡献菜单项（对齐 D17 §5.2.2）。
    /// <para><b>A+ 贡献者机制（V4.10.30）</b>：实现本接口即被 SG1 接口判定自动发现（无需任何标记特性）——
    /// 接口实现即意图声明，编译器强制实现 <see cref="ConfigureMenu"/>，消除漏标静默漏收窗口。</para>
    /// <para><b>同步化决策（Oracle H1，V4.9.74 修正 D17 §5.2.2）</b>：本方法为同步 void——
    /// <c>ExtensionInitializer.ConfigureServices</c> 是同步钩子（DI 构建前），无法 await 异步贡献者；
    /// 菜单贡献者是纯声明式（<c>context.Add(new MenuItemDefinition{...})</c>），无异步诉求，
    /// 对齐 <c>IPermissionDefinitionContributor.Define()</c>（同步）。</para>
    /// <code>
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
