using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 平台账号实体——认证中心身份源（「你是谁」：手机号主键 + 微信绑定 + 认证声明，不含业务角色）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>
    /// <para>JWT <c>sub=user:{UId}</c> 引用本实体平台内部 id；业务角色由各业务系统 <c>IAuthorizationMapper</c> 本地映射。</para>
    /// </summary>
    [Table("AuthAccount")]
    [FreeSql.DataAnnotations.Index("UX_AuthAccount_UId", nameof(UId), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("UX_AuthAccount_Phone", nameof(Phone), IsUnique = true)]
    // ⚠️ 微信便捷登录无手机号账号：Phone 可空（唯一索引对 NULL 多行放行——SQLite/PG 语义）；
    //    短信路径必填由 SmsAuthenticationProvider 保证。微信 openid 列唯一索引防重复创建（Provider 建号兜底）。
    [FreeSql.DataAnnotations.Index("UX_AuthAccount_WechatMpOpenId", nameof(WechatMpOpenId), IsUnique = true)]
    [FreeSql.DataAnnotations.Index("UX_AuthAccount_WechatWebOpenId", nameof(WechatWebOpenId), IsUnique = true)]
    // 联盟锚点（自建 unionid，SSO 联邦层）：唯一索引防重复（可空 NULL 放行）
    [FreeSql.DataAnnotations.Index("UX_AuthAccount_FederationAnchorOpenId", nameof(FederationAnchorOpenId), IsUnique = true)]
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

        /// <summary>手机号 = 身份主键（短信路径必填唯一；微信便捷登录无手机号账号为 null——唯一索引对 NULL 放行）；唯一索引。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(20)]
        public string? Phone { get; set; }

        /// <summary>密码散列（复用主框架 PasswordHasher PBKDF2；password Provider 启用时使用——本期 Provider 扩展点，列预留）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(256)]
        public string? PasswordHash { get; set; }

        /// <summary>公众号 openid（网页授权或菜单/聊天被动采集）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(64)]
        public string? WechatMpOpenId { get; set; }

        /// <summary>开放平台网站应用 openid（扫码）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(64)]
        public string? WechatWebOpenId { get; set; }

    /// <summary>微信开放平台 unionid（双形态归并）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    [MaxLength(64)]
    public string? UnionId { get; set; }

    /// <summary>联盟锚点 openid（自建 unionid，SSO 联邦层——联盟认证服务号 snsapi_base 取；跨商户稳定锚点；
    /// 一对一；唯一索引对 NULL 放行）。⚠️ 与 UnionId（微信开放平台 unionid）语义不同——UnionId 保留原语义，不可重定义（ADR-SSO P1-2）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8)]
    [MaxLength(64)]
    public string? FederationAnchorOpenId { get; set; }

    /// <summary>昵称（微信授权拉取）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9)]
    [MaxLength(128)]
    public string? Nickname { get; set; }

    /// <summary>头像 URL（string 非二进制——DMP 实践确认）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10)]
    [MaxLength(512)]
    public string? Avatar { get; set; }

    /// <summary>教师审核标记——认证中心仅标「已核实教师」，不含业务权限（业务角色由消费方 MapRoles 映射）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 11)]
    public bool TeacherVerified { get; set; }

    /// <summary>认证强度（1=手机号 / 2=微信便捷 / 3=教师核实）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 12)]
    public int AuthLevel { get; set; } = 1;

    /// <summary>密码/绑定变更自增 → 旧 Refresh Token 失效（闭环 DMP PlatformAdmin.TokenVersion 未消费缺口）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 13)]
    public int TokenVersion { get; set; }

    /// <summary>账号启用（默认 true；禁用无法登录）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 14)]
    public bool IsEnabled { get; set; } = true;

    /// <summary>创建时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 15)]
    public DateTime CreateTime { get; set; } = DateTime.UtcNow;

    /// <summary>更新时间（UTC）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 16)]
    public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
