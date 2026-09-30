using System;
using FreeSql.DataAnnotations;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.OrganizationUnit;

/// <summary>
/// 组织单元-用户关联视图实体（VEntity）——跨表 JOIN <c>OrganizationUnitUser</c> → <c>OrganizationUnit</c>，
/// 按 OUPath 前缀单查询返回用户归属 + OU 列（读模型联邦，V0.2.0 下推）。
/// <para>V0.2.0：替代 <c>GetUserIdsInOrganizationUnitAsync(includeDescendants: true)</c> 两步查询
/// （先取子树 OU Id 集合 → 再 junction 查 UserIds）——JOIN + Path 前缀谓词下推 DB，单查询完成。
/// VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。
/// ⚠️ 敏感视图（含用户归属明细）：ExposeGraphqlQuery = false，数据访问统一经 IOrganizationUnitManager 门面。</para>
/// </summary>
[Table(Name = "vw_UserOrganizationUnitView", DisableSyncStructure = true)]
[DomainGenerateCode(IsView = true,
    ViewSql = @"CREATE OR REPLACE VIEW ""vw_UserOrganizationUnitView"" AS
SELECT ouu.""Id"", ouu.""OrganizationUnitId"", ouu.""UserId"", ou.""Path"" AS ""OUPath"", ou.""Code"" AS ""OUCode"",
       ou.""Level"" AS ""OULevel"", ou.""Name"" AS ""OUName""
FROM ""OrganizationUnitUser"" ouu
INNER JOIN ""OrganizationUnit"" ou ON ouu.""OrganizationUnitId"" = ou.""Id""",
    ViewSqlSQLite = @"CREATE VIEW IF NOT EXISTS ""vw_UserOrganizationUnitView"" AS
SELECT ouu.""Id"", ouu.""OrganizationUnitId"", ouu.""UserId"", ou.""Path"" AS ""OUPath"", ou.""Code"" AS ""OUCode"",
       ou.""Level"" AS ""OULevel"", ou.""Name"" AS ""OUName""
FROM ""OrganizationUnitUser"" ouu
INNER JOIN ""OrganizationUnit"" ou ON ouu.""OrganizationUnitId"" = ou.""Id""",
    ExposeGraphqlQuery = false, // ⚠️ 敏感视图显式关闭（含用户归属明细），默认 true 不关即裸暴露
    DefaultPageSize = 50)]
public partial class UserOrganizationUnitView
{
    /// <summary>主键——透传 OrganizationUnitUser.Id（每关联行唯一稳定）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>组织单元 Id（外层过滤键，视图外参数化）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public long OrganizationUnitId { get; set; }

    /// <summary>用户 Id（string，对齐 IUserInfo.UserIdString）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    public string UserId { get; set; } = "";

    /// <summary>组织单元物化路径（透传 OU.Path，前缀过滤键——精确前缀比较，非 LIKE，见 GetByPathPrefixAsync 实证注记）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    public string OUPath { get; set; } = "";

    /// <summary>组织单元编码（透传 OU.Code，白名单 [A-Za-z0-9_.-]）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    public string OUCode { get; set; } = "";

    /// <summary>组织单元层级深度（透传 OU.Level，根=0）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6)]
    public int OULevel { get; set; }

    /// <summary>组织单元显示名（透传 OU.Name，可含任意字符）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    public string OUName { get; set; } = "";
}
