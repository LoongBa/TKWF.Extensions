namespace TKWF.Ext.FeatureManagement;

/// <summary>Feature 值类型（v0.1.0：Boolean/String/Int——存储统一为字符串，ValueType 为元数据）。
/// <para>v0.3.0：扩展 <see cref="Decimal"/>/<see cref="DateTime"/>/<see cref="Json"/>——
/// 类型化读写（<c>IFeatureManager.GetValueAsync&lt;T&gt;</c>/<c>SetValueAsync&lt;T&gt;</c>）+ 写时校验（对齐定义 ValueType）。
/// DB 层仍字符串存储（无类型列），类型语义集中在 FeatureManager 序列化/反序列化映射。</para></summary>
public enum FeatureValueType
{
    /// <summary>布尔开关（默认）。</summary>
    Boolean,

    /// <summary>字符串值。</summary>
    String,

    /// <summary>整数。</summary>
    Int,

    /// <summary>小数（v0.3.0 新增）——InvariantCulture 规范字符串往返。</summary>
    Decimal,

    /// <summary>日期时间（v0.3.0 新增）——ISO8601（"O" 格式，UTC 契约，可往返 RoundtripKind）。</summary>
    DateTime,

    /// <summary>JSON 复杂值（v0.3.0 新增）——对象/数组序列化为规范化 JSON 字符串。</summary>
    Json,
}
