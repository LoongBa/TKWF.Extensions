using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 密码重置码实体（V0.9.0 B.10/B.11——找回多通道自建链路落库底座）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（ADR42 D4，供任意装配实例）。</para>
    /// <para>⚠️ 与 Account 扩展 <c>PasswordResetCodeEntity</c>（userName-keyed，表 <c>PasswordResetCode</c>）<b>同名不同命名空间</b>
    /// （本类 <c>TKWF.Ext.AuthCenter</c> / 表 <c>TKWF_PasswordResetCode</c>）——ADR B.10 明确 AuthCenter 自建 UId-keyed 链路
    /// （绑定 Account 契约按 userName 写 IdentityUser，与 AuthAccount UId/Phone 模型平行不互认），两实体零冲突。</para>
    /// <para>CodeHash 单向散列（SHA256 hex——不存明文；对标 SmsRecord.CodeHash）；多通道字段 Channel=SMS/Email/OAuthTicket
    /// （B.10 层级区分：验证码通道可达 / 扫码身份持有证明）；自带投递（B.11 修复 Account 侧 InitiateResetAsync 生成码不投递缺口）。</para>
    /// <para>表名 <c>TKWF_PasswordResetCode</c>（ADR100——AuthCenter 表前缀批次）。</para>
    /// </summary>
    [Table("TKWF_PasswordResetCode")]
    [FreeSql.DataAnnotations.Index("TKWFIX_PasswordResetCode_UId", nameof(UId), IsUnique = false)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class PasswordResetCodeEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>平台内部 id（AuthAccount.UId——UId-keyed，非 userName；找回目标账号）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(32)]
        public string UId { get; set; } = "";

        /// <summary>找回通道（SMS/Email/OAuthTicket——SmsScenes.Reset 语义 / Emailing 投递 / 扫码前置证明）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(20)]
        public string Channel { get; set; } = "";

        /// <summary>重置码散列（SHA256 hex——不存明文；对标 SmsRecord.CodeHash 单向散列）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(64)]
        public string CodeHash { get; set; } = "";

        /// <summary>过期时间（UTC——TTL 一次性；扫码通道对接 OAuthTicket 生命周期）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public DateTime ExpireAt { get; set; }

        /// <summary>是否已消费（单次消费防重放——兑现后置 true，重复提交拒绝）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public bool IsConsumed { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    }
}