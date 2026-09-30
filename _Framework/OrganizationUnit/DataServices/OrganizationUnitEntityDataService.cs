using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.OrganizationUnit.DTOs;

namespace TKWF.Ext.OrganizationUnit;

/// <summary>组织单元 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para>删除语义（C2）：<c>hasSoftDelete:false</c>——物理删除（实体不声明 IsDeleted）。</para></summary>
partial class OrganizationUnitEntityDataService(IDomainUser user, IEntityDAC<OrganizationUnitEntity> dac)
    : DomainDataServiceBase<OrganizationUnitEntity, OrganizationUnitEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── Store/Manager 委托路径的业务方法（异常自然传播——事务/唯一冲突语义由 Manager 层处理） ──

    /// <summary>按编码查询组织单元（Code 唯一索引）。</summary>
    public async Task<OrganizationUnitEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await EntityGetAsync(t => t.Code == code, ct);

    /// <summary>全量查询（按 Level → SortOrder → Id 排序；树组装/事务内快照用）。</summary>
    public async Task<IReadOnlyList<OrganizationUnitEntity>> GetAllAsync(CancellationToken ct = default)
        => await EntitySelectAsync(null, 0, MaxBatchRead,
            q => q.OrderBy(o => o.Level).ThenBy(o => o.SortOrder).ThenBy(o => o.Id), ct);

    /// <summary>按 Id 集合批量查询（顺序无关，调用方自行排序）。</summary>
    public async Task<IReadOnlyList<OrganizationUnitEntity>> GetByIdsAsync(IReadOnlyList<long> ids, CancellationToken ct = default)
        => await EntitySelectAsync(t => ids.Contains(t.Id), 0, Math.Max(1, ids.Count), null, ct);

    /// <summary>
    /// 按物化路径前缀查询（V0.2.0 SQL 下推——替代 GetSubTreeAsync 的 GetAllAsync + 内存 Path.StartsWith）。
    /// <para>⚠️ P3 LIKE 转义实证（2026-10-01，SQLite 探针）：OU Code 白名单含 '_'——<c>StartsWith</c> 在
    /// SQLite 实际翻译为 <c>instr(Path, @prefix) = 1</c>（字面位置匹配，SQLite 侧无通配符歧义）；但
    /// <b>PostgreSQL 翻译未实证</b>（测试跑 SQLite，生产为 PG——PG 上 StartsWith 可能走 LIKE，'_' 即单字符
    /// 通配符，查 "A_B" 子树会误中 "/AXB/..."）。为<b>双方言无条件安全</b>，改用<b>精确前缀比较</b>：
    /// <c>Path.Length &gt;= len &amp;&amp; Path.Substring(0, len) == prefix</c>（plan §九 P3 备选）——
    /// SQLite 实证翻译 <c>length(Path) &gt;= len AND substr(Path, 1, len) = @prefix</c>；PG 经 FreeSql
    /// 翻译 <c>substring(Path, 1, len) = @prefix</c>——substr/substring 均为字面函数，双方言无通配符歧义
    /// （N1/N3 用例 'A_B' vs 'AXB' 实证无失配）。</para>
    /// </summary>
    public async Task<IReadOnlyList<OrganizationUnitEntity>> GetByPathPrefixAsync(string pathPrefix, CancellationToken ct = default)
        => await EntitySelectAsync(
            t => t.Path.Length >= pathPrefix.Length && t.Path.Substring(0, pathPrefix.Length) == pathPrefix,
            0, int.MaxValue,
            q => q.OrderBy(o => o.Level).ThenBy(o => o.SortOrder).ThenBy(o => o.Id), ct);

    /// <summary>
    /// 按 Code 集合批量查询（V0.2.0 SQL 下推——替代 GetAncestorsAsync 的内存逐段反查；Code 全局唯一）。
    /// <para>谓词 <c>codes.Contains(t.Code)</c> 经 FreeSql 翻译为 <c>Code IN (@p0, @p1, ...)</c>——
    /// Code 唯一索引保证 IN 结果 = 段序列对应 OU（数据一致时即祖先链）。返回后由调用方校验数量
    /// （缺失段抛异常——GetAncestors_MissingAncestor_Throws 语义保持）。</para>
    /// </summary>
    public async Task<IReadOnlyList<OrganizationUnitEntity>> GetByCodesAsync(IReadOnlyList<string> codes, CancellationToken ct = default)
        => await EntitySelectAsync(
            t => codes.Contains(t.Code), 0, Math.Max(1, codes.Count),
            q => q.OrderBy(o => o.Level).ThenBy(o => o.SortOrder).ThenBy(o => o.Id), ct);

    /// <summary>按 Id 物理删除（hasSoftDelete:false——EntitySoftDeleteAsync 对未启用软删实体抛异常，故用批量物理删）。</summary>
    public async Task DeleteEntityAsync(long id, CancellationToken ct = default)
        => await EntityDeleteBatchAsync(new[] { id }, ct);

    private const int MaxBatchRead = 100000;
}
