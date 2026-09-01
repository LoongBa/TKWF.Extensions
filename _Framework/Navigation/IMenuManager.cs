using System.Threading.Tasks;

namespace TKWF.Ext.Navigation
{
    /// <summary>
    /// V4.9.74 (扩展机制业务模块 W2)：菜单管理器——编排贡献者 + 权限过滤（对齐 D17 §5.2.2）。
    /// <para>方法保持异步（运行时调 <c>IPermissionChecker.IsGrantedAsync</c>）；菜单定义在
    /// ConfigureServices 阶段静态收集，权限过滤每次调用基于 ambient 当前用户。</para>
    /// </summary>
    public interface IMenuManager
    {
        /// <summary>获取指定菜单的树形菜单项（含权限过滤）。</summary>
        Task<MenuItemDefinition[]> GetMenuAsync(string menuName);

        /// <summary>获取主菜单（"Main"）——便捷入口，委托 <see cref="GetMenuAsync"/>。</summary>
        Task<MenuItemDefinition[]> GetMainMenuAsync();
    }
}