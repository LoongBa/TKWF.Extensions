using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 认证因子实体——AuthAccount 凭据白名单的因子化演进（WAVE 1：新增 SG1 数据底座，严格增量不改既有）。
    /// <para><c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>
    /// <para>FactorType 语义：1=Password / 2=Sms / 3=Email / 4=Federated / 5=Totp(预留) / 6=Passkey(预留)。</para>
    /// <para>Identifier 规范化：Password=UId；Sms=E.164；Email=小写 trim；Federated=<c>{channelId}|{externalUserId}</c>。</para>
    /// <para>SecretHash：PBKDF2/AES-GCM blob（原 AuthAccount.PasswordHash 的因子化落点）。</para>
    /// <para>State：0=Active / 1=Unverified（解绑=物理删除——无 Revoked 态）。</para>
    /// <para>Metadata：扩展 JSON，<b>不参与查询</b>（仅序列化存读，不做任何筛选/排序条件）。</para>
    /// 表名 <c>TKWF_AuthFactor</c>（ADR100 表名前缀——AuthCenter 表前缀批次）。
    /// </summary>
    [Table("TKWF_AuthFactor")]
    [FreeSql.DataAnnotations.Index("TKWFIX_AuthFactor_Type_Identifier", nameof(FactorType) + "," + nameof(Identifier), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("TKWFIX_AuthFactor_UId", nameof(UId) + "," + nameof(FactorType))]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AuthFactorEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>平台内部 id（MemberUId 风格 u-{unixMs}-{rand}）——JWT sub 引用；与 FactorType 联合索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(32)]
        public string UId { get; set; } = "";

        /// <summary>因子类型：1=Password / 2=Sms / 3=Email / 4=Federated / 5=Totp(预留) / 6=Passkey(预留)。
        /// <para>与 Identifier 构成唯一索引（每用户每标识单因子）；与 UId 构成查询索引。</para></summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public int FactorType { get; set; }

        /// <summary>规范化标识：Password=UId；Sms=E.164；Email=小写 trim；Federated=<c>{channelId}|{externalUserId}</c>。
        /// <para>唯一索引（与 FactorType 复合），对空串/NULL 仍唯一（本列非空默认 ""——业务写入前规范化）。</para></summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(256)]
        public string Identifier { get; set; } = "";

        /// <summary>因子密文/散列 blob（PBKDF2/AES-GCM——原 AuthAccount.PasswordHash 语义，服务端零明文）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(512)]
        public string? SecretHash { get; set; }

        /// <summary>SecretHash 最后更新时刻（UTC）——改密审计与历史比对锚点。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTime? SecretUpdatedAt { get; set; }

        /// <summary>因子状态：0=Active / 1=Unverified（解绑=物理删除——无 Revoked 态，删行即弃）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public int State { get; set; }

        /// <summary>初始密码强制改密标记（原 AuthAccount.MustChangePassword——因子化落点）。
        /// <para>true = 首次登录认证成功后须改密；改密落地后清 false。</para></summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public bool MustChange { get; set; }

        /// <summary>绑定时刻（UTC；null = 未记录绑定时间）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime? BoundAt { get; set; }

        /// <summary>验证通过时刻（UTC；null = 从未验证）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public DateTime? VerifiedAt { get; set; }

        /// <summary>最近使用时刻（UTC；null = 未使用过）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime? LastUsedAt { get; set; }

        /// <summary>扩展 JSON（最大 1024）。<b>不参与查询</b>——仅作为扩展信息的序列化存读载体，
        /// 禁止在筛选/排序/聚合条件中使用本列（保持查询面稳定、可下推）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        [MaxLength(1024)]
        public string? Metadata { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 13)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 14)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
