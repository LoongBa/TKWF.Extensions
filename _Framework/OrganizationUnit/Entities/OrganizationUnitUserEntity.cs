using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.OrganizationUnit
{
    /// <summary>
    /// 组织单元-用户关联实体（用户 ↔ OU 多对多 junction）。
    /// <para>唯一约束 UX_OrganizationUnitUser_User_OU 防重复分配——并发重复分配冲突由
    /// <see cref="IOrganizationUnitManager.AssignUserAsync"/> 捕获数据库异常转业务异常。</para>
    /// </summary>
    [Table("OrganizationUnitUser")]
    [FreeSql.DataAnnotations.Index("UX_OrganizationUnitUser_User_OU",
        nameof(OrganizationUnitId) + "," + nameof(UserId), IsUnique = true)]
    [DomainGenerateCode(DefaultPageSize = 50)]
    public partial class OrganizationUnitUserEntity
    {
        /// <summary>主键（自增）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, IsIdentity = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>组织单元 Id。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        public long OrganizationUnitId { get; set; }

        /// <summary>用户 Id（string，对齐 IUserInfo.UserIdString）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        [MaxLength(128)]
        public string UserId { get; set; } = "";

        /// <summary>创建时间（ServerTime Local——DB 填充；CanUpdate=false 列级兜底）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4, ServerTime = System.DateTimeKind.Local, CanUpdate = false)]
        public DateTimeOffset CreateTime { get; set; }

        /// <summary>更新时间（ServerTime Local——DB 填充）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5, ServerTime = System.DateTimeKind.Local)]
        public DateTimeOffset UpdateTime { get; set; }
    }
}
