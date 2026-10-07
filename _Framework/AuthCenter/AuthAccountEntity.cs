using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 平台账号实体——认证中心身份锚 + 凭据核心（「你是谁」：UId/Phone[凭据角色]/PasswordHash/联邦锚点/安全态，不含档案、不含业务角色）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>
    /// <para>V0.9.0 身份域重构（ADR-AuthCenter-身份域数据模型与密码能力边界 A.1/A.2）：<b>凭据/档案表级分离</b>——
    /// 档案（Nickname/Avatar/TeacherVerified）迁 <see cref="UserProfileEntity"/>；微信 openid 三列
    /// （WechatMpOpenId/WechatWebOpenId/UnionId）删——联邦 Id 归一化到 <see cref="PlatformAccountMapEntity"/>
    /// （ChannelId+ExternalUserId，A.8）；本实体仅保留凭据白名单列。</para>
    /// <para>JWT <c>sub=user:{UId}</c> 引用本实体平台内部 id；业务角色由各业务系统 <c>IAuthorizationMapper</c> 本地映射。
    /// 表名 <c>TKWF_AuthAccount</c>（ADR100 表名别名——AuthCenter 表前缀批次，用户裁定 2026-10-07）。</para>
    /// </summary>
    [Table("TKWF_AuthAccount")]
    [FreeSql.DataAnnotations.Index("TKWFIX_AuthAccount_UId", nameof(UId), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("TKWFIX_AuthAccount_Phone", nameof(Phone), IsUnique = true)]
    // ⚠️ 微信便捷登录无手机号账号：Phone 可空（唯一索引对 NULL 多行放行——SQLite/PG 语义）。
    // ⚠️ V0.9.0：微信 openid 唯一索引（UX_AuthAccount_WechatMp/WebOpenId）已删——联邦 Id 归一化到
    //    PlatformAccountMap（UX_PlatformAccountMap_Channel），防重复建号由 LinkAsync UX catch 兜底（§5.4 P1-3）。
    // 联盟锚点（自建 unionid，SSO 联邦层）：唯一索引防重复（可空 NULL 放行）
    [FreeSql.DataAnnotations.Index("TKWFIX_AuthAccount_FederationAnchorOpenId", nameof(FederationAnchorOpenId), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AuthAccountEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>平台内部 id（MemberUId 风格 u-{unixMs}-{rand}）——JWT sub 引用；唯一索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(32)]
        public string UId { get; set; } = "";

        /// <summary>手机号 = 身份主键（凭据角色——短信路径必填唯一；微信便捷登录无手机号账号为 null——唯一索引对 NULL 放行）；唯一索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(20)]
        public string? Phone { get; set; }

        /// <summary>密码散列（复用主框架 PasswordHasher PBKDF2；password Provider 启用时使用——V0.9.0 B.9 落地启用，不再预留）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(256)]
        public string? PasswordHash { get; set; }

        /// <summary>联盟锚点 openid（自建 unionid，SSO 联邦层——联盟认证服务号 snsapi_base 取；跨商户稳定锚点；
        /// 一对一；唯一索引对 NULL 放行）。V0.9.0：微信平台 openid 归一化到 PlatformAccountMap（ChannelId+ExternalUserId），
        /// 本列仅承载 SSO 联邦自建锚点（FederationAnchorOpenId，N2 冗余快查列）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(64)]
        public string? FederationAnchorOpenId { get; set; }

        /// <summary>认证强度（1=手机号 / 2=联邦快捷认证——微信/QQ/支付宝/OIDC；V0.9.0 A.3 泛化，剔 3=教师核实）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public int AuthLevel { get; set; } = 1;

        /// <summary>密码/绑定变更自增 → 旧 Refresh Token 失效（闭环 DMP PlatformAdmin.TokenVersion 未消费缺口）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public int TokenVersion { get; set; }

        /// <summary>账号启用（默认 true；禁用无法登录）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>账号冻结（V0.9.0 ADR-密码策略与口令协议 决策 3——临时安全处置，与禁用/终结态语义正交）。
        /// <para><c>true</c> = 冻结中：认证路径（Provider/UserHelper/Refresh）拦截新签发；存量 access 靠 2h TTL 自然失效。
        /// 解冻 Claim：<see cref="IsFrozen"/> = false + <see cref="FreezeEnd"/> = null（SecurityLog 事件 Unfreeze）。</para></summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public bool IsFrozen { get; set; }

        /// <summary>冻结自动到期时间（可选 UTC；null = 永久冻结直至显式解冻）。
        /// <para>认证检查判定：<c>IsFrozen &amp;&amp; (FreezeEnd is null || FreezeEnd &gt; UtcNow)</c>——</para>
        /// <para>到达 FreezeEnd 自动失效（IsFrozen 仍 true 但视为已解冻——到期不自动清列，免写路径）。</para></summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public DateTime? FreezeEnd { get; set; }

        /// <summary>冻结生效判定（[NotMapped] 计算属性——认证路径统一检查点，V0.9.0 ADR-密码策略与口令协议 决策 3）：
        /// <c>IsFrozen 且未过 FreezeEnd</c>（FreezeEnd null=永久冻结）。</summary>
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        [System.Text.Json.Serialization.JsonIgnore]
        public bool IsFrozenEffective
            => IsFrozen && (FreezeEnd is null || FreezeEnd > DateTime.UtcNow);

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 11)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 12)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;

        /// <summary>初始密码强制改密标记（V0.9.0 ADR-密码策略与口令协议 决策 5——A.2 逐列准入通过）。
        /// <para>true = 首次登录认证成功后须改密（PasswordAuthenticationProvider 返回信号 → 门面/装配层编排改密）；
        /// 改密落地后清 false。典型场景：管理员下发初始密码 / 找回重置后首登。</para></summary>
        [FreeSql.DataAnnotations.Column(Position = 13)]
        public bool MustChangePassword { get; set; }
    }
}
