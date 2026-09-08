using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Approval;

/// <summary>
/// 审批流定义实体——持久化审批流程定义（Code 唯一 + 顺序步骤链定义 + 或签/会签模式）。
/// <para>[DomainGenerateCode] 不指定 UserType（ADR42 D4——扩展不自建 UserInfo）；
/// 审计字段用 DateTime（UTC）——FreeSql SQLite 不支持 DateTimeOffset。</para>
/// </summary>
[Table("ApprovalFlow")]
[FreeSql.DataAnnotations.Index("UX_ApprovalFlow_Code", nameof(Code), IsUnique = true)]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class ApprovalFlowEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>流程编码（唯一——如 "expense"、"leave"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    [MaxLength(64)]
    public string Code { get; set; } = "";

    /// <summary>流程名称（显示名——如"报销审批""请假审批"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(128)]
    public string Name { get; set; } = "";

    /// <summary>流程描述（可选）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4, IsNullable = true)]
    [MaxLength(512)]
    public string? Description { get; set; }

    /// <summary>步骤链 JSON（<see cref="ApprovalStepDefinition"/> 列表序列化）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5, StringLength = -1)]
    public string StepsJson { get; set; } = "[]";

    /// <summary>是否启用（禁用后 StartAsync 抛异常）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    public bool IsEnabled { get; set; } = true;

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>更新时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8)]
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
}
