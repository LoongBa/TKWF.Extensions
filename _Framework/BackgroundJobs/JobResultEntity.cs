using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 业务结果实体（V0.1.0）——作业内显式记录业务产出，经 <see cref="T:TKWF.Ext.BackgroundJobs.IJobResultRecorder"/> 落库。
/// <para><c>[DomainGenerateCode]</c> 不指定 UserType（ADR42 D4）；v0.1.0 无 ExecutionId（Oracle C4 移除，YAGNI）。</para>
/// </summary>
[Table("JobResult")]
[FreeSql.DataAnnotations.Index("IX_JobResult_JobId", nameof(JobId))]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class JobResultEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>调度器 JobId（弱关联透传）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    [MaxLength(64)]
    public string JobId { get; set; } = "";

    /// <summary>结果类型（默认 "success"，消费方可自定义分类）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(32)]
    public string ResultType { get; set; } = "success";

    /// <summary>业务产出 JSON（nvarchar(max)）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4, StringLength = -1)]
    public string? ResultJson { get; set; }

    /// <summary>摘要（可选，MaxLength 512）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    [MaxLength(512)]
    public string? Summary { get; set; }

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
