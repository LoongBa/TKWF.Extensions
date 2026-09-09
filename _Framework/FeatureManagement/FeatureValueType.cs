namespace TKWF.Ext.FeatureManagement;

/// <summary>Feature 值类型（v0.1.0：Boolean/String/Int——存储统一为字符串，ValueType 为元数据）。</summary>
public enum FeatureValueType
{
    /// <summary>布尔开关（默认）。</summary>
    Boolean,

    /// <summary>字符串值。</summary>
    String,

    /// <summary>整数。</summary>
    Int,
}
