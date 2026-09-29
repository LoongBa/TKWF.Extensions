using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Authentication
{
    /// <summary>
    /// 短信验证码记录实体——频控数据源（独立于 AuthLoginAttempt）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>
    /// <para>验证码单向散列（SHA256）落库，不存明文；校验成功后 IsVerified=true 单次消费。</para>
    /// </summary>
    [Table("SmsRecord")]
    [FreeSql.DataAnnotations.Index("IX_SmsRecord_PhoneSceneTime", nameof(Phone) + "," + nameof(Scene) + "," + nameof(CreateTime))]
    [FreeSql.DataAnnotations.Index("IX_SmsRecord_IpTime", nameof(IpAddress) + "," + nameof(CreateTime))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class SmsRecordEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>手机号。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(20)]
        public string Phone { get; set; } = "";

        /// <summary>场景：login/register/bind/reset。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(20)]
        public string Scene { get; set; } = "";

        /// <summary>验证码 SHA256 hex（不存明文）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(64)]
        public string CodeHash { get; set; } = "";

        /// <summary>校验成功后置 true（单次消费）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public bool IsVerified { get; set; }

        /// <summary>请求 IP（频控维度）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(45)]
        public string? IpAddress { get; set; }

        /// <summary>过期时间（UTC，TTL 5min）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public DateTime ExpireAt { get; set; }

        /// <summary>创建时间（UTC）；索引 (Phone, Scene, CreateTime) / (IpAddress, CreateTime)。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    }
}
