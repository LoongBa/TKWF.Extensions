using System.Collections.Generic;

namespace TKWF.Ext.Navigation
{
    /// <summary>
    /// V4.9.74 (扩展机制业务模块 W2，Oracle M3)：菜单定义仓库——运行时收集后的菜单项查询。
    /// <para>由 <see cref="NavigationExtensionInitializer{TUserInfo}"/> 在 ConfigureServices 阶段
    /// 实例化全部贡献者、调用 <c>ConfigureMenu(context)</c> 后填充；供 <see cref="IMenuManager"/>
    /// 查询组装树形菜单。经 <c>TryAddSingleton</c> 注册，消费方自定义实现优先。</para>
    /// </summary>
    public interface IMenuDefinitionRepository
    {
        /// <summary>全部菜单项（只读快照）。</summary>
        IReadOnlyList<MenuItemDefinition> GetAll();

        /// <summary>批量填充菜单项（由初始化器调用，重复名忽略）。</summary>
        void AddRange(IEnumerable<MenuItemDefinition> menuItems);
    }
}