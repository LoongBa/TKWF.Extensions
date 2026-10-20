using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 平台账号实体——认证中心身份锚（「你是谁」：仅 UId + 账号级安全态；凭据归 <see cref="AuthFactorEntity"/>，
    /// 档案归 <see cref="UserProfileEntity"/>，冻结归 <see cref="AuthAccountFreezeEntity"/>，不含业务角色）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>
    /// <para>V0.11.0 认证关系模型（方案 §4.1 T1）：<b>凭据/冻结/认证强度全部因子化</b>——删
    /// Phone/PasswordHash/MustChangePassword/FederationAnchorOpenId/AuthLevel/IsFrozen/FreezeEnd 七列；
    /// 派生值改经 <c>IAuthFactorService</c>（手机号/认证强度）与 <c>IFreezeStateProvider</c>（冻结判定）。</para>
    /// <para>JWT <c>sub=user:{UId}</c> 引用本实体平台内部 id；业务角色由各业务系统 <c>IAuthorizationMapper</c> 本地映射。
    /// 表名 <c>TKWF_AuthAccount</c>（ADR100 表名别名——AuthCenter 表前缀批次，用户裁定 2026-10-07）。</para>
    /// </summary>
    [Table("TKWF_AuthAccount")]
    [FreeSql.DataAnnotations.Index("TKWFIX_AuthAccount_UId", nameof(UId), IsUnique = true)]
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

        /// <summary>密码/绑定变更自增 → 旧 Refresh Token 失效（闭环 DMP PlatformAdmin.TokenVersion 未消费缺口）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public int TokenVersion { get; set; }

        /// <summary>账号启用（默认 true；禁用无法登录）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public bool IsEnabled { get; set; } = true;

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
