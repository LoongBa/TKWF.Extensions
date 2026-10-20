using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 账号冻结实体——多原因冻结行（V0.11.0 起 AuthAccount.IsFrozen/FreezeEnd 两列的因子化演进底座）。
    /// <para><c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>
    /// <para><b>活跃判据：<c>ReleasedAt == null &amp;&amp; (FreezeEnd == null || FreezeEnd &gt; DateTime.UtcNow)</c></b>——
    /// 解冻置 ReleasedAt 非空（软释放保留审计行），到期 FreezeEnd 到时不自动清行（免写路径，判据天然失效）。</para>
    /// <para>Scope：0=Account（全站登录）/ 1=Login / 2=Feature；<b>V0.11.0 恒为 0</b>，其余为预留位。</para>
    /// 表名 <c>TKWF_AuthAccountFreeze</c>（ADR100 表名前缀——AuthCenter 表前缀批次）。
    /// </summary>
    [Table("TKWF_AuthAccountFreeze")]
    [FreeSql.DataAnnotations.Index("TKWFIX_AuthAccountFreeze_UId", nameof(UId) + "," + nameof(ReleasedAt))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AuthAccountFreezeEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>平台内部 id（u-{unixMs}-{rand}）；与 ReleasedAt 构成查询索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(32)]
        public string UId { get; set; } = "";

        /// <summary>冻结原因——机器可读键（manual / wechat_unfollow / risk_control / arrears / violation，业务可自定义）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(64)]
        public string Reason { get; set; } = "";

        /// <summary>冻结范围：0=Account（全站登录）/ 1=Login / 2=Feature。<b>V0.11.0 恒为 0</b>，其余为预留位
        /// （扩展未来细分冻结面时启用，当前实现一律写 0）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public int Scope { get; set; }

        /// <summary>来源标识（32）——触发方/系统标识（如 risk_control 引擎实例、手动操作控制台）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(32)]
        public string Source { get; set; } = "";

        /// <summary>冻结自动到期时间（UTC；null = 永久冻结直至显式解冻）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTime? FreezeEnd { get; set; }

        /// <summary>解冻时刻（UTC；null = 冻结中/未解冻——活跃判据之一）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime? ReleasedAt { get; set; }

        /// <summary>解冻操作者（64——显示名/操作者标识快照）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        [MaxLength(64)]
        public string? ReleasedBy { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>创建操作者（64——发起冻结的操作者标识快照）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        [MaxLength(64)]
        public string? CreatedBy { get; set; }

        /// <summary>备注（512——冻结原因补充说明，自由文本）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        [MaxLength(512)]
        public string? Note { get; set; }
    }
}
