using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 用户实体——登录名、密码散列、联系方式与启用状态。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>。
    /// SG1 自动生成 <see cref="TKW.Framework.Domain.IDomainEntity"/> 部分与 DTO/DataService。</para>
    /// <para>保留 BCL <c>[Table("IdentityUser")]</c>（<c>FreeSqlTableStructureSynchronizer</c> 靠它发现实体建表）；
    /// 列映射用 FreeSql <c>[Column]</c>（IsPrimary/IsIdentity/Position，全限定避免与 BCL Schema 特性名冲突）。</para>
    /// </summary>
    [Table("IdentityUser")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class UserEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>登录名（唯一，不区分大小写存储为规范化值）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(128)]
        public string UserName { get; set; } = "";

        /// <summary>规范化用户名（大写，用于大小写无关查询）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string NormalizedUserName { get; set; } = "";

        /// <summary>显示名。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        [MaxLength(128)]
        public string DisplayName { get; set; } = "";

        /// <summary>密码散列（PasswordHasher 输出，格式 "Iterations.Salt.Hash"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        [MaxLength(256)]
        public string? PasswordHash { get; set; }

        /// <summary>邮箱。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        [MaxLength(256)]
        public string? Email { get; set; }

        /// <summary>手机号。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7)]
        [MaxLength(32)]
        public string? Phone { get; set; }

        /// <summary>是否启用（默认 true；禁用用户无法登录）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 8)]
        public bool IsActive { get; set; } = true;

        /// <summary>创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 9)]
        public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.Now;

        /// <summary>更新时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 10)]
        public DateTimeOffset UpdateTime { get; set; } = DateTimeOffset.Now;
    }
}