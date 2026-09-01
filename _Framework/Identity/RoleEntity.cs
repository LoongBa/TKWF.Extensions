using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 角色实体——角色名、显示名与系统角色标记。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；
    /// 保留 BCL <c>[Table("IdentityRole")]</c>；列映射用 FreeSql <c>[Column]</c>。</para>
    /// </summary>
    [Table("IdentityRole")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class RoleEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>角色名（唯一，如 "Admin"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        [MaxLength(128)]
        public string Name { get; set; } = "";

        /// <summary>显示名（如 "管理员"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string DisplayName { get; set; } = "";

        /// <summary>是否系统内置角色（默认 false；系统角色不可删除）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public bool IsSystemRole { get; set; }

        /// <summary>创建时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public DateTimeOffset CreateTime { get; set; } = DateTimeOffset.Now;

        /// <summary>更新时间。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6)]
        public DateTimeOffset UpdateTime { get; set; } = DateTimeOffset.Now;
    }
}