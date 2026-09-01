using System;
using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.Permissions.Abstractions
{
    /// <summary>
    /// V4.9.72 (扩展机制业务模块 W2)：权限定义上下文——贡献者调用 <see cref="Add"/> 声明权限，
    /// 由扩展初始化器在 ConfigureServices 阶段收集（对齐 D17 §5.1.2）。
    /// <para>V4.9.85 (ADR48 D7)：迁移至 Abstractions 项目（依赖倒置）。</para>
    /// </summary>
    public class PermissionDefinitionContext
    {
        private readonly List<PermissionDefinition> _definitions = new();

        /// <summary>已收集的权限定义（只读）。</summary>
        public IReadOnlyList<PermissionDefinition> Definitions => _definitions;

        /// <summary>
        /// 声明一个权限定义。
        /// </summary>
        /// <param name="definition">权限定义（Name 必填且唯一）。</param>
        /// <exception cref="ArgumentNullException">definition 或 definition.Name 为空。</exception>
        /// <exception cref="InvalidOperationException">权限名重复。</exception>
        public void Add(PermissionDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (string.IsNullOrWhiteSpace(definition.Name))
                throw new ArgumentNullException(nameof(definition), "权限定义 Name 不能为空");
            if (_definitions.Any(d => d.Name == definition.Name))
                throw new InvalidOperationException($"权限名重复: {definition.Name}");

            _definitions.Add(definition);
        }
    }
}
