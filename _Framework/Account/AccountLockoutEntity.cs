using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 账户锁定记录——用户名、失败计数与锁定截止时间。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；
    /// 保留 BCL <c>[Table("AccountLockout")]</c>；列映射用 FreeSql <c>[Column]</c>。</para>
    /// </summary>
    [Table("AccountLockout")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AccountLockoutEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>用户名（唯一）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(128)]
        public string UserName { get; set; } = "";

        /// <summary>连续失败次数。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public int FailedCount { get; set; }

        /// <summary>锁定截止时间（null = 未锁定）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public DateTime? LockoutEnd { get; set; }

        /// <summary>最近失败时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public DateTime? LastFailedTime { get; set; }

        /// <summary>创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.Now;

        /// <summary>更新时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTimeOffset UpdateTime { get; set; } = DateTimeOffset.Now;
    }
}