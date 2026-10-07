using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 兑换历史读模型（跨扩展 VEntity——视图 <c>TKWFV_UserRedemptionHistory</c>）。
/// <para>JOIN 本扩展 <c>RedemptionCode</c> + AuthCenter <c>TKWF_AuthAccount</c>（按 <c>RedeemedByUId = UId</c> 行级 FK，P4；
/// ⚠️ AuthCenter V0.9.0 身份域重构——表名 <c>TKWF_AuthAccount</c>（ADR100 表名别名）+ 凭据白名单列，档案列已迁 UserProfileEntity，
/// 本视图仅投影身份锚 UId）——视图归授权面（业务域扩展，AGENTS §8）；<c>TKWFV_</c> 前缀暂用（前置裁定 P3 / ADR C.16）。</para>
/// <para>⚠️ 视图不过滤（历史完整性）——<c>Status=1</c>（Redeemed）过滤在 DataService；<c>INNER JOIN</c> 天然排除未兑换行
/// （<c>RedeemedByUId</c> NULL 匹配不到 TKWF_AuthAccount）。</para>
/// </summary>
[FreeSql.DataAnnotations.Table(Name = "TKWFV_UserRedemptionHistory", DisableSyncStructure = true)]
[DomainGenerateCode(IsView = true,
    ViewSql = @"CREATE OR REPLACE VIEW ""TKWFV_UserRedemptionHistory"" AS
SELECT rc.""Id"", rc.""RedeemedByUId"" AS ""UserId"", rc.""CodeMasked"", rc.""ProductName"", rc.""TargetAppId"",
       rc.""RedeemedAtUtc"", rc.""Status"", ac.""UId""
FROM ""TKWF_RedemptionCode"" rc
INNER JOIN ""TKWF_AuthAccount"" ac ON rc.""RedeemedByUId"" = ac.""UId""",
    ViewSqlSQLite = @"CREATE VIEW IF NOT EXISTS ""TKWFV_UserRedemptionHistory"" AS
SELECT rc.""Id"", rc.""RedeemedByUId"" AS ""UserId"", rc.""CodeMasked"", rc.""ProductName"", rc.""TargetAppId"",
       rc.""RedeemedAtUtc"", rc.""Status"", ac.""UId""
FROM ""TKWF_RedemptionCode"" rc
INNER JOIN ""TKWF_AuthAccount"" ac ON rc.""RedeemedByUId"" = ac.""UId""",
    ExposeGraphqlQuery = false,
    DefaultPageSize = 50)]
public partial class UserRedemptionHistoryView
{
    /// <summary>透传 <c>RedemptionCode.Id</c>（主键）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>兑换人平台内部 id——SQL AS 别名（<c>RedeemedByUId AS "UserId"</c>），对齐 AuthAccount.UId 语义。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public string UserId { get; set; } = "";

    /// <summary>脱敏 code（授权面已脱敏存储——UserCenter 契约 CodeMasked 透传）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    public string CodeMasked { get; set; } = "";

    [FreeSql.DataAnnotations.Column(Position = 4)]
    public string ProductName { get; set; } = "";

    [FreeSql.DataAnnotations.Column(Position = 5)]
    public string TargetAppId { get; set; } = "";

    [FreeSql.DataAnnotations.Column(Position = 6, IsNullable = true)]
    public DateTime? RedeemedAtUtc { get; set; }

    /// <summary>1=Redeemed（兑换历史查询过滤键）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    public int Status { get; set; }

    /// <summary>身份锚（JOIN TKWF_AuthAccount——AuthAccount.UId 语义，P4 行级 FK 验证）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8)]
    public string UId { get; set; } = "";
}
