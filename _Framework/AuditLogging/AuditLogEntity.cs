using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计日志表实体——记录方法级调用事件（调用者、目标方法、参数脱敏 JSON、耗时、成功/异常、关联 ID）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。
    /// SG1 自动生成 <see cref="TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>
    /// <para>保留 BCL <c>[Table("AuditLog")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
    /// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
    /// </summary>
    [Table("AuditLog")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AuditLogEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>调用者用户名。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(128)]
        public string? UserName { get; set; }

        /// <summary>调用者用户 ID。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string? UserId { get; set; }

        /// <summary>目标服务名（类名）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(200)]
        public string ServiceName { get; set; } = "";

        /// <summary>目标方法名。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(200)]
        public string MethodName { get; set; } = "";

        /// <summary>参数 JSON（已脱敏）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public string? ArgumentsJson { get; set; }

        /// <summary>执行时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime ExecutionTime { get; set; }

        /// <summary>执行耗时（毫秒）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public int DurationMs { get; set; }

        /// <summary>是否执行成功。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public bool Success { get; set; }

        /// <summary>异常信息（失败时记录）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public string? Exception { get; set; }

        /// <summary>关联 ID（分布式链路追踪）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        [MaxLength(128)]
        public string? CorrelationId { get; set; }

        /// <summary>记录创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.Now;
    }
}
