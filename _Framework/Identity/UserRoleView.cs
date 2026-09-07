using System;
using FreeSql.DataAnnotations;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 用户-角色视图实体（VEntity）——跨表 JOIN <c>IdentityUserRole</c> → <c>IdentityRole</c>，按 UserId 单查询返回角色。
    /// <para>V0.2.0：替代两步查询（先查 RoleId 集合再查 Role）——JOIN 下推 DB，单查询完成。
    /// VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。</para>
    /// </summary>
    [Table(Name = "vw_UserRoleView", DisableSyncStructure = true)]
    [DomainGenerateCode(IsView = true,
        ViewSql = @"CREATE OR REPLACE VIEW ""vw_UserRoleView"" AS
SELECT ur.""UserId"", r.""Id"", r.""Name"", r.""DisplayName"", r.""IsSystemRole"", r.""CreateTime"", r.""UpdateTime""
FROM ""IdentityUserRole"" ur
INNER JOIN ""IdentityRole"" r ON ur.""RoleId"" = r.""Id""",
        ViewSqlSQLite = @"CREATE VIEW IF NOT EXISTS ""vw_UserRoleView"" AS
SELECT ur.""UserId"", r.""Id"", r.""Name"", r.""DisplayName"", r.""IsSystemRole"",
       NULL AS ""CreateTime"", NULL AS ""UpdateTime""
FROM ""IdentityUserRole"" ur
INNER JOIN ""IdentityRole"" r ON ur.""RoleId"" = r.""Id""",
        ExposeGraphqlQuery = true,
        DefaultPageSize = 50)]
    public partial class UserRoleView
    {
        /// <summary>主键——透传 Role.Id（每个 UserId 最多一条，唯一稳定）。</summary>
        [FreeSql.DataAnnotations.Column(IsPrimary = true, Position = 1)]
        public long Id { get; set; }

        /// <summary>用户 ID（外层过滤键，视图外参数化）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 2)]
        public long UserId { get; set; }

        /// <summary>角色名（如 "Admin"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 3)]
        public string Name { get; set; } = "";

        /// <summary>角色显示名（如 "管理员"）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 4)]
        public string DisplayName { get; set; } = "";

        /// <summary>是否系统内置角色。</summary>
        [FreeSql.DataAnnotations.Column(Position = 5)]
        public bool IsSystemRole { get; set; }

        /// <summary>角色创建时间（透传 Role.CreateTime，DateTimeOffset）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 6, CanUpdate = false)]
        public DateTimeOffset CreateTime { get; set; }

        /// <summary>角色更新时间（透传 Role.UpdateTime，DateTimeOffset）。</summary>
        [FreeSql.DataAnnotations.Column(Position = 7, CanUpdate = false)]
        public DateTimeOffset UpdateTime { get; set; }
    }
}
