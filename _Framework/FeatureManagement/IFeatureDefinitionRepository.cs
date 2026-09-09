using System.Collections.Generic;

namespace TKWF.Ext.FeatureManagement;

/// <summary>Feature 定义仓库（对齐 <c>IPermissionDefinitionRepository</c>）。</summary>
public interface IFeatureDefinitionRepository
{
    /// <summary>全部定义。</summary>
    IReadOnlyList<FeatureDefinition> GetAll();

    /// <summary>是否含指定名定义。</summary>
    bool Contains(string name);

    /// <summary>批量添加（重复名忽略）。</summary>
    void AddRange(IEnumerable<FeatureDefinition> definitions);
}
