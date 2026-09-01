using System;

namespace TKWF.Ext.Permissions.Abstractions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W2)：权限定义——编译期声明 + 运行时值（对齐 D17 §5.1.2）。
    /// <para>权限名约定：点分层级（如 <c>"Order.Create"</c>），<c>Parent</c> 表达层级关系。</para>
    /// <para>V4.9.85 (ADR48 D7)：迁移至 Abstractions 项目（依赖倒置）。</para>
    /// </summary>
    public class PermissionDefinition
    {
        /// <summary>权限唯一名称（如 "Order.Create"）。</summary>
        public string Name { get; init; } = "";

        /// <summary>显示名（支持 i18n key）。</summary>
        public string DisplayName { get; set; } = "";

        /// <summary>权限分组（如 "Order"）。</summary>
        public string? Group { get; set; }

        /// <summary>父权限名（层级关系）。</summary>
        public string? Parent { get; set; }

        /// <summary>权限描述。</summary>
        public string? Description { get; set; }
    }
}
