using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthCenter
{
    /// <summary>
    /// 应用授权实体——OAuth2 authorization grant 语义（V0.8.0 `/grants` 端点数据底座 + B-口令兑换产品线写入点预留）。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>（不指定 UserType——ADR42 D4，供任意装配实例）。</para>
    /// <para>语义：记录"用户授权某应用访问其数据的持久授权"（应用授权有效期 <see cref="ValidUntil"/>，跨会话），
    /// 与令牌生命周期（2h access / 30d refresh）正交；唯一约束 UX(UserId,AppId,Source) 防并发 exchange 产生重复 grant 行。</para>
    /// </summary>
    [Table("AuthGrant")]
    [FreeSql.DataAnnotations.Index("UX_AuthGrant_User_App_Source",
        nameof(UserId) + "," + nameof(AppId) + "," + nameof(Source), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class AuthGrantEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>平台内部 id（JWT sub 同源——授权归属用户）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(50)]
        public string UserId { get; set; } = "";

        /// <summary>授权应用 id（/grants 查询维度）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(100)]
        public string AppId { get; set; } = "";

        /// <summary>逗号分隔授权范围（v0.8.0 login 源空串；⚠️ 已知局限——按单 scope 查询需 LIKE 无法走索引，
        /// 未来 scope 维度查询变热时 normalizate 为 AuthGrantScopeEntity(GrantId, Scope)，本迭代不过度设计）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(500)]
        public string Scopes { get; set; } = "";

        /// <summary>应用授权有效期（UTC；null = 持续至 Status 翻转为 Revoked——对齐 OAuth2 grant persist 语义，
        /// 非会话有效期；login 源默认 null；redeem 源未来按口令 valid_until 填值）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public DateTime? ValidUntil { get; set; }

        /// <summary>授权来源（AuthGrantSources 常量：login 登录授权 / redeem 口令兑换预留）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(20)]
        public string Source { get; set; } = "";

        /// <summary>授权状态（0 Active / 1 Revoked——管理吊销预留，本迭代仅 Active）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        public int Status { get; set; }

        /// <summary>创建时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public DateTime CreateTime { get; set; } = DateTime.UtcNow;

        /// <summary>更新时间（UTC）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTime UpdateTime { get; set; } = DateTime.UtcNow;
    }
}
