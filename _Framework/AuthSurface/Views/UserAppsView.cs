using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 我的应用读模型（跨扩展 VEntity——视图 <c>TKWFV_UserApps</c>）。
/// <para>JOIN AuthCenter <c>AuthGrant</c>（应用授权底座）+ <c>TKWF_AuthAccount</c>（<c>UserId = UId</c> 行级 FK；
/// ⚠️ AuthCenter V0.9.0 身份域重构——表名 <c>TKWF_AuthAccount</c>（ADR100）+ 凭据白名单列，档案列已迁 UserProfileEntity，
/// 本视图仅投影身份锚 UId/AuthLevel）+ 本扩展 <c>AuthApp</c>（LEFT JOIN 取 AppName——目录未录入的应用显示裸 AppId 而非丢行）。</para>
/// <para>⚠️ 视图不过滤 <c>Status</c>/<c>ValidUntil</c>（"我的应用"有效过滤在 DataService——C# 侧 UtcNow 无 CURRENT_TIMESTAMP 方言风险；
/// 视图保留全量便于审计与 v0.2.0 授权快照）。ViewSql 零 <c>vw_</c> 嵌套（视图链禁令 VIEW002 合规——JOIN 均为基表）。</para>
/// </summary>
[FreeSql.DataAnnotations.Table(Name = "TKWFV_UserApps", DisableSyncStructure = true)]
[DomainGenerateCode(IsView = true,
    ViewSql = @"CREATE OR REPLACE VIEW ""TKWFV_UserApps"" AS
SELECT g.""Id"", g.""UserId"", g.""AppId"", a.""AppName"", g.""Scopes"", g.""ValidUntil"", g.""Source"", g.""Status"", g.""CreateTime"",
       ac.""UId"", ac.""AuthLevel""
FROM ""AuthGrant"" g
INNER JOIN ""TKWF_AuthAccount"" ac ON g.""UserId"" = ac.""UId""
LEFT JOIN ""AuthApp"" a ON g.""AppId"" = a.""AppId""",
    ViewSqlSQLite = @"CREATE VIEW IF NOT EXISTS ""TKWFV_UserApps"" AS
SELECT g.""Id"", g.""UserId"", g.""AppId"", a.""AppName"", g.""Scopes"", g.""ValidUntil"", g.""Source"", g.""Status"", g.""CreateTime"",
       ac.""UId"", ac.""AuthLevel""
FROM ""AuthGrant"" g
INNER JOIN ""TKWF_AuthAccount"" ac ON g.""UserId"" = ac.""UId""
LEFT JOIN ""AuthApp"" a ON g.""AppId"" = a.""AppId""",
    ExposeGraphqlQuery = false,
    DefaultPageSize = 50)]
public partial class UserAppsView
{
    /// <summary>透传 <c>AuthGrant.Id</c>（主键）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>授权归属用户（= AuthAccount.UId 同源，JWT sub 语义）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public string UserId { get; set; } = "";

    [FreeSql.DataAnnotations.Column(Position = 3)]
    public string AppId { get; set; } = "";

    /// <summary>应用名（LEFT JOIN AuthApp——目录未录入时 null，显示裸 AppId）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4, IsNullable = true)]
    public string? AppName { get; set; }

    [FreeSql.DataAnnotations.Column(Position = 5)]
    public string Scopes { get; set; } = "";

    /// <summary>应用授权有效期（null=持续至吊销，跨会话——非会话有效期）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6, IsNullable = true)]
    public DateTime? ValidUntil { get; set; }

    /// <summary>授权来源（login/redeem——AuthGrantSources 语义；v0.1.0 仅 login 源数据）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    public string Source { get; set; } = "";

    /// <summary>0=Active / 1=Revoked。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8)]
    public int Status { get; set; }

    [FreeSql.DataAnnotations.Column(Position = 9)]
    public DateTime CreateTime { get; set; }

    /// <summary>身份锚（JOIN TKWF_AuthAccount——AuthAccount.UId 语义）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10)]
    public string UId { get; set; } = "";

    /// <summary>认证强度（1=手机号 / 2=联邦快捷——AuthCenter V0.9.0 A.3 泛化语义）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 11)]
    public int AuthLevel { get; set; }
}
