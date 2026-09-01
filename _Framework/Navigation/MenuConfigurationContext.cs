using System;
using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Navigation
{
    /// <summary>
    /// V4.9.74 (扩展机制业务模块 W2)：菜单配置上下文——贡献者调用 <see cref="Add"/> 声明菜单项，
    /// 由扩展初始化器在 ConfigureServices 阶段收集（仿 <c>PermissionDefinitionContext</c>）。
    /// </summary>
    public class MenuConfigurationContext
    {
        private readonly List<MenuItemDefinition> _menuItems = new();

        /// <summary>已收集的菜单项（只读）。</summary>
        public IReadOnlyList<MenuItemDefinition> MenuItems => _menuItems;

        /// <summary>
        /// 声明一个菜单项。
        /// </summary>
        /// <param name="menuItem">菜单项（Name 必填且唯一）。</param>
        /// <exception cref="ArgumentNullException">menuItem 或 menuItem.Name 为空。</exception>
        /// <exception cref="InvalidOperationException">菜单项名重复。</exception>
        public void Add(MenuItemDefinition menuItem)
        {
            if (menuItem == null) throw new ArgumentNullException(nameof(menuItem));
            if (string.IsNullOrWhiteSpace(menuItem.Name))
                throw new ArgumentNullException(nameof(menuItem), "菜单项 Name 不能为空");
            if (_menuItems.Any(m => m.Name == menuItem.Name))
                throw new InvalidOperationException($"菜单项名重复: {menuItem.Name}");

            _menuItems.Add(menuItem);
        }
    }
}