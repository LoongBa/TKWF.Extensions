using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// 安全日志表实体——记录认证/授权相关安全事件（登录成功/失败、登出、改密、密码重置、账户锁定、注册、挑战）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType，ADR42 D4）。
    /// SG1 自动生成 <see cref="TKW.Framework.Domain.Interfaces.IDomainEntity"/> 部分与 DTO/DataService。</para>
    /// <para>保留 BCL <c>[Table("SecurityLog")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
    /// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
    /// <para><b>只增不改语义（Oracle C2）</b>：本实体无 Update/Delete 业务方法、无
    /// <c>[GenerateController(FromDataService=true)]</c>——安全日志只经 DataService <c>EntityCreateAsync</c> 追加写入。</para>
    /// </summary>
    [Table("SecurityLog")]
    [FreeSql.DataAnnotations.Index("IX_SecurityLog_UserEventTime", nameof(UserName) + "," + nameof(EventType) + "," + nameof(CreateTime))]
    [FreeSql.DataAnnotations.Index("IX_SecurityLog_IpTime", nameof(IpAddress) + "," + nameof(CreateTime))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class SecurityLogEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>
        /// 事件类型。合法值（使用指南注明，无编译期约束）：
        /// Login / Logout / PasswordChange / PasswordReset / Lockout / Register / Challenge。
        /// </summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(32)]
        public string EventType { get; set; } = "";

        /// <summary>事件分类（Authentication / Authorization）。v0.1.0 事件均为 Authentication。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(32)]
        public string EventCategory { get; set; } = "";

        /// <summary>尝试用户名（登录失败 = 请求输入，防枚举语义下仍保留审计来源）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string UserName { get; set; } = "";

        /// <summary>用户 ID（认证成功后回填，未认证为 null）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public long? UserId { get; set; }

        /// <summary>客户端 IP（经 IAmbientContext["ClientIp"] 采集，非 Web 环境为 null）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(64)]
        public string? IpAddress { get; set; }

        /// <summary>客户端 UserAgent（经 IAmbientContext["UserAgent"] 采集，无则 null）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(512)]
        public string? UserAgent { get; set; }

        /// <summary>结果（Success / Failed）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        [MaxLength(16)]
        public string Result { get; set; } = "Success";

        /// <summary>
        /// 详情——失败时记录异常消息（脱敏：仅异常消息，绝不含密码/令牌明文；成功时记录返回值消息）。
        /// 长文本（nvarchar(max) 等价，StringLength=-1 跨 Provider 映射，对齐 JobResultEntity 先例）。
        /// </summary>
        [FreeSql.DataAnnotations.Column(Position = 9, StringLength = -1)]
        public string? Detail { get; set; }

        /// <summary>关联 ID（分布式链路追踪，对齐 AuditLog 追踪链）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(64)]
        public string? CorrelationId { get; set; }

        /// <summary>记录创建时间（UTC）。CanUpdate=false——只增不改语义的列级兜底。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11, CanUpdate = false)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    }
}
