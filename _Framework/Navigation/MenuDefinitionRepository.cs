using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Navigation
{
    /// <summary>
    /// V4.9.74 (W2/W3，Oracle M3)：内存菜单定义仓库默认实现——收集贡献者菜单项，供 MenuManager 查询。
    /// </summary>
    internal sealed class MenuDefinitionRepository : IMenuDefinitionRepository
    {
        private readonly List<MenuItemDefinition> _menuItems = new();

        public IReadOnlyList<MenuItemDefinition> GetAll() => _menuItems;

        public void AddRange(IEnumerable<MenuItemDefinition> menuItems)
        {
            foreach (var item in menuItems)
            {
                if (item == null || item.Name == null) continue;
                if (!_menuItems.Any(m => m.Name == item.Name))
                    _menuItems.Add(item);
            }
        }
    }
}