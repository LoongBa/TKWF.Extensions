using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 登录尝试实体——登录保护（限流/审计数据源，只增语义）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4）。</para>
    /// <para>认证中心实例自身库的登录尝试计数（含 authType 维度），与 SecurityLog 事件日志 / Account 锁定 / RateLimiting 中间件互补不重叠。</para>
    /// </summary>
    [Table("TKWF_AuthLoginAttempt")]
    [FreeSql.DataAnnotations.Index("TKWFIX_AuthLoginAttempt_UserAuthTime", nameof(UserIdentity) + "," + nameof(AuthType) + "," + nameof(AttemptTime))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AuthLoginAttemptEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>登录标识（手机号/openid/用户名）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(100)]
        public string UserIdentity { get; set; } = "";

        /// <summary>认证方式：sms/federated/password/redeem（T5 2026-10-09——federated 替代 wechat）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(20)]
        public string AuthType { get; set; } = "";

        /// <summary>是否成功。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public bool IsSuccess { get; set; }

        /// <summary>客户端 IP（IPv4/IPv6）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(45)]
        public string? IpAddress { get; set; }

        /// <summary>客户端 User-Agent。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(500)]
        public string? UserAgent { get; set; }

        /// <summary>失败原因（密码错误/账号不存在/限流）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(100)]
        public string? FailReason { get; set; }

        /// <summary>尝试时间（UTC）；索引 (UserIdentity, AuthType, AttemptTime)。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime AttemptTime { get; set; } = DateTime.UtcNow;
    }
}
