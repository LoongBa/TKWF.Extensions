using System;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 用户-角色映射实体——用户与角色的多对多关系。
    /// <para>SG1 化：声明式实体——<c>partial</c> + <c>[DomainGenerateCode]</c>；
    /// 保留 BCL <c>[Table("IdentityUserRole")]</c>；列映射用 FreeSql <c>[Column]</c>。</para>
    /// </summary>
    [Table("IdentityUserRole")]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class UserRoleEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>用户 ID（<see cref="UserEntity.Id"/>，索引）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        public long UserId { get; set; }

        /// <summary>角色 ID（<see cref="RoleEntity.Id"/>，索引）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public long RoleId { get; set; }
    }
}