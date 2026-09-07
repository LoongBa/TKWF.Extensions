using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Tagging;

/// <summary>
/// 标签命中记录实体（V0.3.0）——持久化 <c>TKW.Framework.Utility.Tags.TagHit</c> 结果，支撑"高频标签/时间分布/维度占比"分析。
/// <para>字段对齐 <c>TagHit</c> record（算法输出模型）+ 原文快照/时间戳；<c>[DomainGenerateCode]</c> 不指定 UserType
/// （ADR42 D4）；HitTime 用 DateTime（UTC）——FreeSql SQLite 不支持 DateTimeOffset（Oracle P1-1）。</para>
/// </summary>
[Table("TagHit")]
[FreeSql.DataAnnotations.Index("IX_TagHit_Dimension_Time", nameof(Dimension) + "," + nameof(HitTime), IsUnique = false)]
[FreeSql.DataAnnotations.Index("IX_TagHit_TagName", nameof(TagName), IsUnique = false)]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class TagHitRecordEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>维度。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    [MaxLength(128)]
    public string Dimension { get; set; } = "";

    /// <summary>标签名。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(128)]
    public string TagName { get; set; } = "";

    /// <summary>命中原文片段。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    [MaxLength(256)]
    public string MatchedValue { get; set; } = "";

    /// <summary>命中起始位置。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    public int StartIndex { get; set; }

    /// <summary>命中长度。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    public int Length { get; set; }

    /// <summary>规则优先级。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    public int Priority { get; set; }

    /// <summary>互斥组。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8, IsNullable = true)]
    [MaxLength(64)]
    public string? ExclusionGroup { get; set; }

    /// <summary>原文快照（可选——分析上下文）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9, IsNullable = true)]
    public string? SourceText { get; set; }

    /// <summary>命中时间（UTC——SQLite 可靠，Oracle P1-1；分析索引）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10, CanUpdate = false)]
    public DateTime HitTime { get; set; } = DateTime.UtcNow;
}