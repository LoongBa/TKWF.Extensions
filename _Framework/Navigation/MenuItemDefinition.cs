using System;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Navigation
{
    /// <summary>
    /// V4.9.74 (扩展机制业务模块 W2)：菜单项——树形数据模型，不含渲染信息（对齐 D17 §5.2.2）。
    /// <para>命名约定：<see cref="Name"/> 是菜单项唯一标识（层级关联用）；<see cref="Parent"/> 指向父菜单项 Name（顶层为 null）。</para>
    /// </summary>
    public class MenuItemDefinition
    {
        /// <summary>菜单项唯一名称（如 "Orders"）。</summary>
        public string Name { get; init; } = "";

        /// <summary>显示名（支持 i18n key）。</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>导航 URL（如 "/orders"）。</summary>
        public string? Url { get; set; }

        /// <summary>图标标识（如 "orders"——前端图标库 key）。</summary>
        public string? Icon { get; set; }

        /// <summary>同级排序（小值在前）。</summary>
        public int Order { get; set; }

        /// <summary>父菜单项 Name（顶层为 null）。</summary>
        public string? Parent { get; set; }

        /// <summary>
        /// 所需权限名列表（对齐 <see cref="RequirePermissionAttribute"/> 语义，Oracle M2）。
        /// <para>null/空 → 不设权限限制（始终显示）；非空 → <see cref="Logic"/> 决定判定方式
        /// （All 全部授予显示 / Any 任一授予显示）。</para>
        /// </summary>
        public string[]? RequiredPermissions { get; set; }

        /// <summary>权限判定逻辑（默认 All，对齐 <see cref="RequirePermissionAttribute.Logic"/>）。</summary>
        public PermissionLogic Logic { get; set; } = PermissionLogic.All;

        /// <summary>是否启用（false 则不显示）。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>是否可见（false 则不显示——前端 UI 可保留占位）。</summary>
        public bool IsVisible { get; set; } = true;
    }
}