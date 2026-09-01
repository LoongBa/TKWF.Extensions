using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Account
{
    /// <summary>
    /// 密码重置码记录——用户名、重置码、过期时间与使用状态。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；
    /// 保留 BCL <c>[Table("PasswordResetCode")]</c>；列映射用 FreeSql <c>[Column]</c>。</para>
    /// </summary>
    [Table("PasswordResetCode")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class PasswordResetCodeEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>用户名。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(128)]
        public string UserName { get; set; } = "";

        /// <summary>重置码（随机生成）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string ResetCode { get; set; } = "";

        /// <summary>
        /// 过期时间。
        /// <para>注：FreeSql SQLite 对 DateTimeOffset 写入不可靠（读回丢失），统一用 DateTime（本地时间语义）。</para>
        /// </summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public DateTime ExpireTime { get; set; }

        /// <summary>是否已使用（幂等消费）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public bool IsUsed { get; set; }

        /// <summary>创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.Now;
    }
}