using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.BackgroundJobs;

/// <summary>
/// 作业执行历史实体（V0.1.0）——每次执行一行，记录耗时/重试/异常归档。
/// <para>经 <see cref="T:TKW.Framework.Domain.BackgroundJobs.IBackgroundJobExecutionListener"/> 自动落库（三实现一套适配）。</para>
/// <para><c>[DomainGenerateCode]</c> 不指定 UserType（ADR42 D4——扩展不自建 UserInfo）；
/// 审计字段用 DateTime（UTC）——FreeSql SQLite 不支持 DateTimeOffset（Oracle P1-1）。</para>
/// </summary>
[Table("JobExecution")]
[FreeSql.DataAnnotations.Index("IX_JobExecution_JobId", nameof(JobId))]
[FreeSql.DataAnnotations.Index("IX_JobExecution_Provider_Success", nameof(Provider) + "," + nameof(IsSuccess))]
[FreeSql.DataAnnotations.Index("IX_JobExecution_StartedAt", nameof(StartedAtUtc))]
[DomainGenerateCode(DefaultPageSize = 50)]
public partial class JobExecutionEntity
{
    /// <summary>主键（自增）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>调度器 JobId（弱关联透传，不跨模块引用实体）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    [MaxLength(64)]
    public string JobId { get; set; } = "";

    /// <summary>作业类型（AssemblyQualifiedName）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    [MaxLength(1024)]
    public string JobType { get; set; } = "";

    /// <summary>调度器 Provider（builtin|hangfire|quartz）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    [MaxLength(16)]
    public string Provider { get; set; } = "";

    /// <summary>是否成功。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    public bool IsSuccess { get; set; }

    /// <summary>是否被取消（OperationCanceledException）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    public bool IsCancelled { get; set; }

    /// <summary>第几次执行尝试（内置重试 1-based；Hangfire/Quartz=1）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    public int RetryAttempt { get; set; }

    /// <summary>执行耗时（毫秒）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8)]
    public long DurationMs { get; set; }

    /// <summary>执行开始时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9)]
    public DateTime StartedAtUtc { get; set; }

    /// <summary>执行完成时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10)]
    public DateTime CompletedAtUtc { get; set; }

    /// <summary>异常归档（ex.ToString() 纯文本，对齐契约 Error——P1-1）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 11, StringLength = -1)]
    public string? ErrorText { get; set; }

    /// <summary>租户 Id（透传记录，依赖 TKWF 框架已有机制）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 12)]
    public long? TenantId { get; set; }

    /// <summary>创建时间（UTC——SQLite 可靠，Oracle P1-1）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 13, CanUpdate = false)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;
}
