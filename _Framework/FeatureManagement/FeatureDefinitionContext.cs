using System;
using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Feature 定义上下文——收集贡献者声明的定义（对齐 <c>PermissionDefinitionContext</c>）。</summary>
public sealed class FeatureDefinitionContext
{
    private readonly List<FeatureDefinition> _definitions = [];

    /// <summary>已收集的定义（只读）。</summary>
    public IReadOnlyList<FeatureDefinition> Definitions => _definitions;

    /// <summary>添加定义——Name 必填（空/空白抛 <see cref="ArgumentException"/>）；重复抛 <see cref="InvalidOperationException"/>。</summary>
    public void Add(FeatureDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        if (string.IsNullOrWhiteSpace(definition.Name))
            throw new ArgumentException("Feature 定义 Name 不能为空", nameof(definition));
        if (_definitions.Any(d => d.Name == definition.Name))
            throw new InvalidOperationException($"Feature 定义重复：{definition.Name}");

        _definitions.Add(definition);
    }
}
