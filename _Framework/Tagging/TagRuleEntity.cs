using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Tagging;

/// <summary>
/// 标签规则实体（V0.3.0）——持久化标签匹配规则，供 <see cref="ITagRuleStore"/> 管理 + <c>ITagService.LoadRules</c> 供给算法。
/// <para>字段对齐 <c>TKW.Framework.Utility.Tags.TagRule</c>（算法输入模型）；<c>[DomainGenerateCode]</c> 不指定 UserType
/// （ADR42 D4——扩展不自建 UserInfo）；审计字段用 DateTime（UTC）——FreeSql SQLite 不支持 DateTimeOffset（Oracle P1-1）。</para>
/// </summary>
[Table("TagRule")]
[FreeSql.DataAnnotations.Index("UX_TagRule_Dimension_TagName_Pattern",
    nameof(Dimension) + "," + nameof(TagName) + "," + nameof(Pattern), IsUnique = true)]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class TagRuleEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>维度（如 "Category"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    [MaxLength(128)]
    public string Dimension { get; set; } = "";

    /// <summary>标签名（如 "电子"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(128)]
    public string TagName { get; set; } = "";

    /// <summary>匹配模式（<see cref="TKW.Framework.Utility.Tags.TagMatchMode"/> 枚举值）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    public int MatchMode { get; set; }

    /// <summary>匹配串/正则。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    [MaxLength(256)]
    public string Pattern { get; set; } = "";

    /// <summary>是否启用。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    public bool IsEnabled { get; set; } = true;

    /// <summary>优先级（越高越先）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    public int Priority { get; set; }

    /// <summary>互斥组（同组择优）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8, IsNullable = true)]
    [MaxLength(64)]
    public string? ExclusionGroup { get; set; }

    /// <summary>是否默认规则。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9)]
    public bool IsDefaultRule { get; set; }

    /// <summary>默认标签名（"其它"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10)]
    [MaxLength(128)]
    public string DefaultTagName { get; set; } = "其它";

    /// <summary>创建时间（UTC——SQLite 可靠，Oracle P1-1）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 11, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>更新时间（UTC——SQLite 可靠，Oracle P1-1）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 12)]
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
}