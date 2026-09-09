using System.Collections.Generic;

namespace TKWF.Ext.FeatureManagement;

/// <summary>
/// Feature 定义（编译期声明，对齐 <c>PermissionDefinition</c>）。
/// <para>ValueType/DefaultValue 为元数据；值存储统一为字符串（DefaultValue 亦为字符串表示）。</para>
/// </summary>
public sealed class FeatureDefinition
{
    /// <summary>Feature 名（必填，全局唯一）。</summary>
    public required string Name { get; init; }

    /// <summary>显示名。</summary>
    public string? DisplayName { get; init; }

    /// <summary>描述。</summary>
    public string? Description { get; init; }

    /// <summary>分组（管理界面分组）。</summary>
    public string? Group { get; init; }

    /// <summary>值类型（默认布尔开关）。</summary>
    public FeatureValueType ValueType { get; init; } = FeatureValueType.Boolean;

    /// <summary>默认值（字符串表示——Boolean 用 "true"/"false"；无存储值时兜底）。</summary>
    public string? DefaultValue { get; init; }

    /// <summary>允许设置值的 Provider 层（可空 = 默认全层 Global/Tenant/Role/User）。</summary>
    public IReadOnlyList<string>? AllowedProviders { get; init; }
}
