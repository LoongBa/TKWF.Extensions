using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.OrganizationUnit;
using TKWF.Ext.OrganizationUnit.DTOs;

namespace TKWF.Ext.OrganizationUnit;

/// <summary>
/// 组织单元-用户关联视图只读 DataService（VEntity V0.2.0）——跨表 JOIN 单查询下推 DB。
/// <para>VEntity 由 xCodeGen 跳过 DataService 模板（Engine.cs L42-46），此处手写继承
/// <see cref="DomainReadOnlyDataServiceBase{TEntity, TDto}"/>（2 参数版，与扩展现有 DataService 一致）。
/// 注入 <see cref="IEntityReadOnlyDAC{TEntity}"/>（只读契约）——绝不用 IEntityDAC（FreeSqlEntityDAC 静态守卫）。
/// 不标 <c>[GenerateController(FromDataService = true)]</c>：用户归属查询是 Manager 内部能力，
/// REST 经 IOrganizationUnitManager 门面暴露；ExposeGraphqlQuery=false（敏感视图），无 GraphQL resolver。</para>
/// </summary>
partial class UserOrganizationUnitViewDataService(IDomainUser user, IEntityReadOnlyDAC<UserOrganizationUnitView> dac)
    : DomainReadOnlyDataServiceBase<UserOrganizationUnitView, UserOrganizationUnitViewDto>(user, dac, hasSoftDelete: false)
{
    /// <summary>
    /// 单查询跨表：按 OUPath 前缀返回用户归属（JOIN OrganizationUnitUser → OrganizationUnit 下推 DB，
    /// 携带 OU 列，替代 GetUserIdsInOrganizationUnitAsync 子树两步查询）。
    /// <para>⚠️ P3 LIKE 转义实证（2026-10-01）：与 <see cref="OrganizationUnitEntityDataService.GetByPathPrefixAsync"/>
    /// 同一风险（OU Code 白名单含 '_'）——SQLite 探针实证 StartsWith 走 <c>instr</c>（字面匹配）无歧义，
    /// 但 PG 翻译未实证（生产为 PG，可能走 LIKE 致 '_' 通配符失配）——故同样采用<b>精确前缀比较</b>
    /// （<c>Length &gt;= len &amp;&amp; Substring(0, len) == prefix</c> → SQLite <c>substr</c> / PG <c>substring</c>，
    /// 字面函数双方言安全），N3 用例 'A_B' vs 'AXB' 实证无失配。</para>
    /// <para>limit 显式 <c>int.MaxValue</c>（无界——<c>MaxListLimit=1000</c> 保护，子树归属查询语义为全量）。</para>
    /// </summary>
    public async Task<IReadOnlyList<UserOrganizationUnitView>> GetByOuPathPrefixAsync(string pathPrefix, CancellationToken ct = default)
        => await SelectAsync(v => v,
            predicate: v => v.OUPath.Length >= pathPrefix.Length && v.OUPath.Substring(0, pathPrefix.Length) == pathPrefix,
            orderBy: null, limit: int.MaxValue, ct: ct);
}
