using System;
using FreeSql.DataAnnotations;
using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.DataDictionary;

/// <summary>
/// 数据字典项-定义视图实体（VEntity）——跨表 JOIN <c>DictionaryItem</c> → <c>DictionaryDefinition</c>，
/// 按 DefinitionCode 单查询返回项 + 定义列（读模型联邦）。
/// <para>V0.2.0：替代 <c>GetOrLoadAggregateAsync</c> 项查询两步（先按 DefinitionId 查项）——JOIN 下推 DB，单查询完成。
/// VEntity 只读：<c>IDomainViewEntity</c> 由 SG1 自动生成，禁 IEntityDAC 写操作。
/// ⚠️ 树语义保护：ExposeGraphqlQuery = false（裸平表绕过 BuildTree 树语义），数据访问统一经 IDictionaryManager 门面。
/// ⚠️ INNER JOIN 语义："定义存在但无项"返回零行——Manager 先单查定义（不存在→null），再走视图查项（零行=空项列表），
/// "空字典"语义保留（oracle3 C-1/H1 方案 b）。</para>
/// </summary>
[Table(Name = "vw_DictionaryItemView", DisableSyncStructure = true)]
[DomainGenerateCode(IsView = true,
    ViewSql = @"CREATE OR REPLACE VIEW ""vw_DictionaryItemView"" AS
SELECT i.""Id"", i.""DefinitionId"", d.""Code"" AS ""DefinitionCode"", i.""Code"", i.""DisplayName"", i.""Value"",
       i.""Order"", i.""IsEnabled"", i.""ParentCode"", i.""Level"", i.""Path"", d.""DisplayName"" AS ""DefinitionDisplayName""
FROM ""DictionaryItem"" i
INNER JOIN ""DictionaryDefinition"" d ON i.""DefinitionId"" = d.""Id""",
    ViewSqlSQLite = @"CREATE VIEW IF NOT EXISTS ""vw_DictionaryItemView"" AS
SELECT i.""Id"", i.""DefinitionId"", d.""Code"" AS ""DefinitionCode"", i.""Code"", i.""DisplayName"", i.""Value"",
       i.""Order"", i.""IsEnabled"", i.""ParentCode"", i.""Level"", i.""Path"", d.""DisplayName"" AS ""DefinitionDisplayName""
FROM ""DictionaryItem"" i
INNER JOIN ""DictionaryDefinition"" d ON i.""DefinitionId"" = d.""Id""",
    ExposeGraphqlQuery = false, // C4：裸平表绕过 BuildTree 树语义——数据访问经门面聚合，不直连 GraphQL
    DefaultPageSize = 50)]
public partial class DictionaryItemView
{
    /// <summary>主键——透传 DictionaryItem.Id（每项唯一稳定）。</summary>
    [FreeSql.DataAnnotations.Column(IsPrimary = true, Position = 1)]
    public long Id { get; set; }

    /// <summary>所属字典定义 ID（透传 DictionaryItem.DefinitionId）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 2)]
    public long DefinitionId { get; set; }

    /// <summary>字典编码（透传 DictionaryDefinition.Code，外层过滤键——替代两步查定义）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 3)]
    public string DefinitionCode { get; set; } = "";

    /// <summary>字典项编码（如 "Male"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 4)]
    public string Code { get; set; } = "";

    /// <summary>显示名（如 "男"）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 5)]
    public string DisplayName { get; set; } = "";

    /// <summary>关联值（可为空）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 6, IsNullable = true)]
    public string? Value { get; set; }

    /// <summary>排序（小值在前，排序键）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 7)]
    public int Order { get; set; }

    /// <summary>是否启用（透传 DictionaryItem.IsEnabled）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 8)]
    public bool IsEnabled { get; set; } = true;

    /// <summary>父项编码（透传 DictionaryItem.ParentCode，BuildTree 键——留内存）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 9, IsNullable = true)]
    public string? ParentCode { get; set; }

    /// <summary>层级深度（透传 DictionaryItem.Level）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 10)]
    public int Level { get; set; }

    /// <summary>物化路径（透传 DictionaryItem.Path）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 11)]
    public string Path { get; set; } = "";

    /// <summary>定义显示名（透传 DictionaryDefinition.DisplayName，展示扩展）。</summary>
    [FreeSql.DataAnnotations.Column(Position = 12)]
    public string DefinitionDisplayName { get; set; } = "";
}
