using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// 测试持久化实体——模拟消费方定义的指标结果表（v0.2.0 持久化契约消费方实体）。
/// <para>消费方实体由 <c>partial class</c> + <c>[Table]</c> + <c>[DomainGenerateCode]</c> 声明——SG1 分析器
/// 生成元数据 + IDomainEntity 接口，xCodeGen（<c>.xCodeGen\extensions\metrics-tests.xCodeGen.json</c>）生成
/// <c>TestMetricResultEntity.g.cs</c>/<c>Dto.g.cs</c>/<c>DataService.g.cs</c>/<c>Conditions.g.cs</c>——
/// 对齐扩展项目固有模式（2026-09-14 标准管线，替代早期手写 DataService 基座方案）。</para>
/// <para>表结构对齐方案 §3.4 示例：Id 主键自增 / SpecKey / Name / ValueText（JSON 化值）/ Unit /
/// DimensionsJson / CalculatedAtUtc / CreateTime。</para>
/// </summary>
[Table("TestMetricResult")]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class TestMetricResultEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>规格键（如 "sales"），可空。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    [MaxLength(128)]
    public string? SpecKey { get; set; }

    /// <summary>指标名（如 "repurchase-rate-30d"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(128)]
    public string Name { get; set; } = "";

    /// <summary>值（JSON 化文本；标量 ToString / string 直通 / 复杂对象 JSON——TestMetricResultStore switch 三路径）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    public string? ValueText { get; set; }

    /// <summary>单位（如 "%"、"CNY"），可空。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    [MaxLength(64)]
    public string? Unit { get; set; }

    /// <summary>维度 JSON（如 {"bucket":"2026-08-01"}），可空。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    [MaxLength(2000)]
    public string? DimensionsJson { get; set; }

    /// <summary>计算时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7, CanUpdate = false)]
    public DateTime CalculatedAtUtc { get; set; }

    /// <summary>记录创建时间。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}