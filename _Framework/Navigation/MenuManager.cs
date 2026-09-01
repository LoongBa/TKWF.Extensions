using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Navigation
{
    /// <summary>
    /// V4.9.74 (扩展机制业务模块 W3)：菜单管理器默认实现——权限过滤 + 扁平排序 + 循环检测。
    /// <para><b>权限过滤</b>（Oracle ✅）：<see cref="MenuItemDefinition.RequiredPermissions"/> 非空时调
    /// <see cref="IPermissionChecker"/> 判定（<c>Logic=All</c> 全部授予显示 / <c>Logic=Any</c> 任一授予显示）；
    /// checker 未注册 → <b>降级不过滤</b>（返回全菜单——菜单是展示层数据非安全边界，安全由
    /// <c>PermissionFilterAttribute</c> 兜底，见 ADR39）。</para>
    /// <para><b>返回形态</b>：扁平 <c>MenuItemDefinition[]</c>（<c>Parent</c> 引用保留，UI 层据此建树——
    /// D17 §5.2 模型）；按 <c>(深度, Order)</c> 排序使顶层优先、父子相邻，便于消费方线性渲染。
    /// <b>循环检测</b>（Oracle L1）：Parent 引用成环（A→B→A）抛明确异常，防无限递归。</para>
    /// </summary>
    public class MenuManager<TUserInfo> : IMenuManager
        where TUserInfo : class, TKW.Framework.Domain.Interfaces.IUserInfo, new()
    {
        private readonly IMenuDefinitionRepository _repository;
        private readonly IServiceProvider _serviceProvider;

        public MenuManager(IMenuDefinitionRepository repository, IServiceProvider serviceProvider)
        {
            _repository = repository;
            _serviceProvider = serviceProvider;
        }

        /// <summary>获取主菜单（"Main"）——便捷入口。</summary>
        public Task<MenuItemDefinition[]> GetMainMenuAsync() => GetMenuAsync("Main");

        /// <summary>获取指定菜单的扁平菜单项（含权限过滤 + 排序 + 循环检测）。</summary>
        public async Task<MenuItemDefinition[]> GetMenuAsync(string menuName)
        {
            // 注：V4.9.74 简化——单一菜单定义集（未按 menuName 分区）；menuName 为前瞻扩展点
            // （Main/Admin/Mobile 多菜单组），当前所有项归入传入的菜单名。
            var all = _repository.GetAll();
            var checker = _serviceProvider.GetService<IPermissionChecker>();

            // 1. 权限过滤（checker 缺失 → 不过滤，降级）
            var visible = new List<MenuItemDefinition>();
            foreach (var item in all)
                if (await IsVisibleAsync(item, checker)) visible.Add(item);
            if (visible.Count == 0) return Array.Empty<MenuItemDefinition>();

            // 2. 循环检测 + 深度计算（成环抛异常，Oracle L1）
            var depthMap = ComputeDepths(visible);

            // 3. 扁平排序：深度优先（浅→深），同级按 Order
            return visible
                .OrderBy(i => depthMap[i.Name])
                .ThenBy(i => i.Order)
                .ToArray();
        }

        /// <summary>判定菜单项是否可见：IsEnabled + IsVisible + RequiredPermissions 权限过滤。
        /// <para>V4.9.74 审核（Oracle M1）：async 化——避免 sync-over-async 反模式（GetAwaiter().GetResult()
        /// 占用线程池线程等待 checker I/O）；GetMenuAsync 已 async，await 无副作用。</para></summary>
        private async Task<bool> IsVisibleAsync(MenuItemDefinition item, IPermissionChecker? checker)
        {
            if (!item.IsEnabled || !item.IsVisible) return false;
            var required = item.RequiredPermissions;
            if (required == null || required.Length == 0) return true;

            // checker 未注册 → 降级不过滤（展示层非安全边界，Oracle ✅）
            if (checker == null) return true;

            var grants = await checker.IsGrantedAsync(required).ConfigureAwait(false);
            return item.Logic switch
            {
                PermissionLogic.All => required.All(p => grants.GetValueOrDefault(p) == true),
                PermissionLogic.Any => required.Any(p => grants.GetValueOrDefault(p) == true),
                _ => false
            };
        }

        /// <summary>计算每个菜单项的深度（顶层=0）；检测 Parent 循环引用（Oracle L1）。</summary>
        private static Dictionary<string, int> ComputeDepths(List<MenuItemDefinition> items)
        {
            var byName = items.ToDictionary(i => i.Name, i => i);
            var depthMap = new Dictionary<string, int>(StringComparer.Ordinal);

            foreach (var item in items)
                ComputeDepth(item.Name, byName, depthMap, new HashSet<string>(StringComparer.Ordinal));

            return depthMap;
        }

        private static int ComputeDepth(string name, Dictionary<string, MenuItemDefinition> byName,
            Dictionary<string, int> depthMap, HashSet<string> visiting)
        {
            if (depthMap.TryGetValue(name, out var cached)) return cached;
            if (!visiting.Add(name))
                throw new InvalidOperationException($"菜单 Parent 循环引用: {name}");

            var item = byName[name];
            var depth = 0;
            if (item.Parent != null && byName.ContainsKey(item.Parent))
                depth = ComputeDepth(item.Parent, byName, depthMap, visiting) + 1;

            visiting.Remove(name);
            depthMap[name] = depth;
            return depth;
        }
    }
}