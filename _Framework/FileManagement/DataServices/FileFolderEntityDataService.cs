using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.FileManagement.DTOs;

namespace TKWF.Ext.FileManagement;

/// <summary>目录 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para>删除语义：<c>hasSoftDelete:false</c>——物理删除（实体不声明 IsDeleted）。
/// 删除保护（无子目录且无文件）由 Manager 层强制（D17）。</para>
/// <para>可空 ParentId 谓词（对齐 ManagedFile 双分支）：<c>parentId.HasValue ? e.ParentId == parentId.Value : e.ParentId == null</c>
/// ——SQL 语义下推（FreeSql 对 null 参数不自动生成 IS NULL）。</para></summary>
partial class FileFolderEntityDataService(IDomainUser user, IEntityDAC<FileFolderEntity> dac)
    : DomainDataServiceBase<FileFolderEntity, FileFolderEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── Store/Manager 委托路径的业务方法（异常自然传播——唯一约束/业务规则语义由 Manager 层处理） ──

    /// <summary>按编码查询目录（Code 唯一索引 UX_FileFolder_Code）。</summary>
    public Task<FileFolderEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
        => EntityGetAsync(f => f.Code == code, ct);

    /// <summary>全量查询（Level → SortOrder → Id 排序；树组装/同级 max+1 计算用）。</summary>
    public async Task<IReadOnlyList<FileFolderEntity>> GetAllAsync(CancellationToken ct = default)
        => await EntitySelectAsync(null, 0, MaxBatchRead,
            q => q.OrderBy(f => f.Level).ThenBy(f => f.SortOrder).ThenBy(f => f.Id), ct);

    /// <summary>按父目录查直接子目录（parentId=null → 根目录；SortOrder → Id 排序）。</summary>
    public async Task<IReadOnlyList<FileFolderEntity>> GetChildrenByParentIdAsync(long? parentId, CancellationToken ct = default)
        => await EntitySelectAsync(BuildParentPredicate(parentId), 0, MaxBatchRead,
            q => q.OrderBy(f => f.SortOrder).ThenBy(f => f.Id), ct);

    /// <summary>按父目录统计直接子目录数（删除保护计数——SQL COUNT 下推；parentId=null → 根目录计数）。</summary>
    public Task<long> CountChildrenByParentIdAsync(long? parentId, CancellationToken ct = default)
        => Dac.CountAsync(QueryForUser().Where(BuildParentPredicate(parentId)), ct);

    /// <summary>新增目录（回写自增 Id）。</summary>
    public Task<FileFolderEntity> CreateAsync(FileFolderEntity entity, CancellationToken ct = default)
        => EntityCreateAsync(entity, ct);

    /// <summary>更新目录（全字段更新，返回更新后实体）。</summary>
    public Task<FileFolderEntity> UpdateAsync(FileFolderEntity entity, CancellationToken ct = default)
        => EntityUpdateAsync(entity, ct);

    /// <summary>父目录谓词（可空双分支：HasValue → 等值；null → IS NULL——SQL 下推）。</summary>
    private static Expression<Func<FileFolderEntity, bool>> BuildParentPredicate(long? parentId)
    {
        Expression<Func<FileFolderEntity, bool>> predicate = parentId.HasValue
            ? f => f.ParentId == parentId.Value
            : f => f.ParentId == null;
        return predicate;
    }

    private const int MaxBatchRead = 100000;
}
