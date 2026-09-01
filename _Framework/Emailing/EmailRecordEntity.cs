using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// 邮件记录表实体——存储发送邮件的记录（收件人、发件人、主题、正文、状态、错误信息等）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。
    /// SG1 自动生成 IDomainEntity 部分与 DTO/DataService。</para>
    /// <para>保留 BCL <c>[Table("EmailRecord")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
    /// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
    /// </summary>
    [Table("EmailRecord")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class EmailRecordEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>收件人（多个以逗号分隔）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(512)]
        public string To { get; set; } = "";

        /// <summary>发件人地址。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(256)]
        public string? From { get; set; }

        /// <summary>邮件主题。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(512)]
        public string Subject { get; set; } = "";

        /// <summary>邮件正文。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public string? Body { get; set; }

        /// <summary>是否为 HTML 格式正文。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public bool IsHtml { get; set; }

        /// <summary>发送状态（Pending / Sent / Failed）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(32)]
        public string Status { get; set; } = "Pending";

        /// <summary>错误信息（发送失败时记录）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public string? ErrorMessage { get; set; }

        /// <summary>重试次数。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public int RetryCount { get; set; }

        /// <summary>记录创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public DateTime CreateTime { get; set; } = DateTime.Now;

        /// <summary>实际发送时间（发送成功时记录）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime? SendTime { get; set; }
    }
}
