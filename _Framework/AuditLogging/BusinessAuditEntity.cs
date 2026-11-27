using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 业务事实审计表实体——一行 = 一次业务对象变更事实（Actor + Target + Action + Before/After）。
    /// <para>与 <see cref="AuditLogEntity"/>（方法调用审计）分立：<b>写模型分立，每面单一事实源</b>（P2）。
    /// 内容列显式 <c>[MaxLength(-1)]</c>（AGENTS §8 2026-11-14 事故红线——防 Pg 22001）。</para>
    /// <para><b>默认 append-only</b>（ADR-AuditLogging-不可抵赖与删除策略）——<b>当前不暴露 REST/GraphQL</b>（DataService 无
    /// <c>[GenerateController]</c>）；将来若暴露，须 <c>ExcludeMethods</c> + 专用详情 DTO（仿 <c>AuditLogDetailDto</c>，
    /// 防 <c>BeforeJson</c>/<c>AfterJson</c> PII 直出）。</para>
    /// </summary>
    [Table("TKWF_BusinessAudit")]
    [FreeSql.DataAnnotations.Index("TKWFIX_BusinessAudit_ExecutionTime", nameof(ExecutionTime), IsUnique = false)]
    [FreeSql.DataAnnotations.Index("TKWFIX_BusinessAudit_Target", nameof(TargetType) + "," + nameof(TargetId), IsUnique = false)]
    [FreeSql.DataAnnotations.Index("TKWFIX_BusinessAudit_Actor", nameof(ActorType) + "," + nameof(ActorId), IsUnique = false)]
    [FreeSql.DataAnnotations.Index("TKWFIX_BusinessAudit_CorrelationId", nameof(CorrelationId), IsUnique = false)]
    [FreeSql.DataAnnotations.Index("TKWFIX_BusinessAudit_Tenant", nameof(TenantId) + "," + nameof(ExecutionTime), IsUnique = false)]
    [DomainGenerateCode(SubDomain = "AuditLogging", SubDomainRoutePrefix = "/AuditLogging", DefaultPageSize = 50)]
    public partial class BusinessAuditEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>租户 Id（可空单义；DB-per-tenant 下恒定；P15 键空间维度）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        public long? TenantId { get; set; }

        /// <summary>操作者类型（User/System/Job/Service/Anonymous）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(32)]
        public string ActorType { get; set; } = "";

        /// <summary>操作者 Id（系统帧如 job:{name}）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string? ActorId { get; set; }

        /// <summary>操作者显示名（脱敏）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(200)]
        public string? ActorName { get; set; }

        /// <summary>业务对象类型（merchant/contract/whitelist/config/channel…）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(100)]
        public string TargetType { get; set; } = "";

        /// <summary>业务对象标识。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(200)]
        public string? TargetId { get; set; }

        /// <summary>动作类型（Created/Updated/… 或 {domain}.{entity}.{verb}）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        [MaxLength(100)]
        public string ActionType { get; set; } = "";

        /// <summary>变更摘要（脱敏）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        [MaxLength(-1)]
        public string? Summary { get; set; }

        /// <summary>变更前（脱敏 JSON）。<b>标准 CRUD 排除暴露</b>——防 PII。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(-1)]
        public string? BeforeJson { get; set; }

        /// <summary>变更后（脱敏 JSON）。<b>标准 CRUD 排除暴露</b>——防 PII。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        [MaxLength(-1)]
        public string? AfterJson { get; set; }

        /// <summary>扩展属性（脱敏 JSON；业务面独有，C12）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        [MaxLength(-1)]
        public string? AttributesJson { get; set; }

        /// <summary>关联 Id（三面同域，长度对齐 AuditCorrelationId.MaxLength = 128）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 13)]
        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        /// <summary>业务发生时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 14)]
        public DateTime ExecutionTime { get; set; }

        /// <summary>落库时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 15)]
        public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.Now;
    }
}
