using System.Collections.Generic;
using System.Linq;

namespace TKWF.Ext.FeatureManagement;

/// <summary>内存 Feature 定义仓库（internal——收集期填充，运行期只读）。</summary>
internal sealed class InMemoryFeatureDefinitionRepository : IFeatureDefinitionRepository
{
    private readonly List<FeatureDefinition> _definitions = [];

    public IReadOnlyList<FeatureDefinition> GetAll() => _definitions;

    public bool Contains(string name) => _definitions.Any(d => d.Name == name);

    public void AddRange(IEnumerable<FeatureDefinition> definitions)
    {
        foreach (var d in definitions)
        {
            if (!_definitions.Any(x => x.Name == d.Name))
                _definitions.Add(d);   // 重复名忽略（对齐 Permissions）
        }
    }
}
