using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.SmsCode
{
    /// <summary>
    /// 短信验证码记录实体——发送/校验频控与作废的数据源。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，
    /// 扩展不感知消费方用户类型；对齐 AuthCenter <c>SmsRecordEntity</c> 先例）。</para>
    /// <para><b>X' 冻结项 1</b>：主体键列名 <c>Subject</c>（不叫 Phone）——不透明主体键，v1 恒=手机号；
    /// <c>DeliveryTarget</c> 与 Subject 分离（MFA 未来 subject=user×method 而投递=绑定号）。</para>
    /// <para>验证码单向散列（SHA256 hex 小写，<c>FixedTimeEquals</c> 比对）落库，不存明文；
    /// 校验成功 <c>IsVerified=true</c> 单次消费；<c>FailedAttempts</c> 为同码连续失败计数（M3——失败作废机制落点）。</para>
    /// <para>只增语义（对齐 AuthCenter SmsRecord 先例）：写路径 = 创建（发送）/ MarkVerified（校验消费）/
    /// <c>FailedAttempts</c> CAS 自增（失败）/ 重发作废旧码（ExpireAt 置 now）；无公开 Update/Delete 业务方法。</para>
    /// </summary>
    [Table("TKWF_SmsCode")]
    [FreeSql.DataAnnotations.Index("TKWFIX_SmsCode_SubjectSceneTime", nameof(Subject) + "," + nameof(Scene) + "," + nameof(CreateTime))]
    [FreeSql.DataAnnotations.Index("TKWFIX_SmsCode_IpTime", nameof(IpAddress) + "," + nameof(CreateTime))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class SmsCodeRecordEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>不透明主体键（X' 冻结项 1——v1 恒=手机号；列名 Subject 不叫 Phone）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(20)]
        public string Subject { get; set; } = "";

        /// <summary>投递目标（v1 恒=手机号，与 Subject 分离——MFA 未来 subject=user×method 而投递=绑定号）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(20)]
        public string? DeliveryTarget { get; set; }

        /// <summary>场景（SmsScenes 白名单，未知场景 fail-closed）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(20)]
        public string Scene { get; set; } = "";

        /// <summary>验证码 SHA256 hex（不存明文，FixedTimeEquals 比对）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(64)]
        public string CodeHash { get; set; } = "";

        /// <summary>校验成功后置 true（单次消费 CAS）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public bool IsVerified { get; set; }

        /// <summary>请求 IP（频控维度；空白/未注册 → "unknown" 桶）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(45)]
        public string? IpAddress { get; set; }

        /// <summary>过期时间（UTC，TTL 默认 5min）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime ExpireAt { get; set; }

        /// <summary>同码连续失败计数（M3——FailedVerifyInvalidateThreshold 作废机制落点；null=0 语义，CAS 自增）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public long? FailedAttempts { get; set; }

        /// <summary>创建时间（UTC）；索引 (Subject,Scene,CreateTime) / (IpAddress,CreateTime)。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;
    }
}