using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;   // V0.2.0：SumAsync（SQL SUM 聚合下推——FreeSqlQueryableExtensions，ADR15）
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.FileManagement.DTOs;

namespace TKWF.Ext.FileManagement;

/// <summary>文件 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para>删除语义：<c>hasSoftDelete:false</c>——物理删除（实体不声明 IsDeleted）。</para>
/// <para>可空 FolderId 谓词双分支（对齐开发方案 3.2）：<c>folderId.HasValue ? e.FolderId == folderId.Value : e.FolderId == null</c>
/// ——SQL 下推，FreeSql 对 null 参数不自动生成 IS NULL。</para></summary>
partial class ManagedFileEntityDataService(IDomainUser user, IEntityDAC<ManagedFileEntity> dac)
    : DomainDataServiceBase<ManagedFileEntity, ManagedFileEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── Store/Manager 委托路径的业务方法（异常自然传播——唯一约束/业务规则语义由 Manager 层处理） ──

    /// <summary>按目录 + 文件名查文件（UX_ManagedFile_Folder_Name 唯一约束双条件；folderId 可空 → 根级文件）。</summary>
    public Task<ManagedFileEntity?> GetByFolderAndNameAsync(long? folderId, string name, CancellationToken ct = default)
        => EntityGetAsync(CombineAnd(BuildFolderPredicate(folderId), f => f.Name == name), ct);

    /// <summary>按目录分页查询（folderId 可空 → 根级文件；Name → Id 排序）。</summary>
    public async Task<IReadOnlyList<ManagedFileEntity>> GetByFolderAsync(long? folderId, int skip, int take, CancellationToken ct = default)
        => await EntitySelectAsync(BuildFolderPredicate(folderId), skip, take,
            q => q.OrderBy(f => f.Name).ThenBy(f => f.Id), ct);

    /// <summary>按内容哈希分页查询（去重预查；IX_ManagedFile_Sha256 索引）。</summary>
    public async Task<IReadOnlyList<ManagedFileEntity>> GetBySha256Async(string sha256, int skip, int take, CancellationToken ct = default)
        => await EntitySelectAsync(f => f.Sha256 == sha256, skip, take,
            q => q.OrderBy(f => f.Id), ct);

    /// <summary>按文件名关键字模糊分页查询（Name.Contains 下推）。</summary>
    public async Task<IReadOnlyList<ManagedFileEntity>> SearchByNameAsync(string keyword, int skip, int take, CancellationToken ct = default)
        => await EntitySelectAsync(f => f.Name.Contains(keyword), skip, take,
            q => q.OrderBy(f => f.Name).ThenBy(f => f.Id), ct);

    /// <summary>按目录统计文件数（删除保护计数——SQL COUNT 下推；folderId 可空 → 根级文件计数）。</summary>
    public Task<long> CountByFolderIdAsync(long? folderId, CancellationToken ct = default)
        => Dac.CountAsync(QueryForUser().Where(BuildFolderPredicate(folderId)), ct);

    /// <summary>
    /// 按目录求文件总容量（V0.2.0 配额——SQL SUM 下推；folderId 可空 → 根级文件）。
    /// <para>独立 QueryForUser() 起新查询（FreeSql ISelect 原地可变陷阱——禁止链式复用过滤后的 ISelect）。</para>
    /// <para>P2-4：SQL SUM(Size) 空表/空目录返回 NULL——decimal → long 转换前 ?? 0（空目录返回 0）。</para>
    /// </summary>
    public async Task<long> SumSizeByFolderIdAsync(long? folderId, CancellationToken ct = default)
    {
        decimal sum = await QueryForUser()
            .Where(BuildFolderPredicate(folderId))
            .SumAsync(e => (decimal)e.Size, ct);
        return (long)sum;
    }

    /// <summary>
    /// 全局文件总容量（V0.2.0 配额——SQL SUM 下推全表）。
    /// <para>独立 QueryForUser() 起新查询（ISelect 原地可变陷阱）。P2-4：空表 NULL → 0。</para>
    /// </summary>
    public async Task<long> SumSizeAllAsync(CancellationToken ct = default)
    {
        decimal sum = await QueryForUser().SumAsync(e => (decimal)e.Size, ct);
        return (long)sum;
    }

    /// <summary>新增文件（回写自增 Id）。</summary>
    public Task<ManagedFileEntity> CreateAsync(ManagedFileEntity entity, CancellationToken ct = default)
        => EntityCreateAsync(entity, ct);

    /// <summary>更新文件（全字段更新，返回更新后实体）。</summary>
    public Task<ManagedFileEntity> UpdateAsync(ManagedFileEntity entity, CancellationToken ct = default)
        => EntityUpdateAsync(entity, ct);

    /// <summary>目录谓词（可空双分支：HasValue → 等值；null → IS NULL——SQL 下推）。</summary>
    private static Expression<Func<ManagedFileEntity, bool>> BuildFolderPredicate(long? folderId)
    {
        Expression<Func<ManagedFileEntity, bool>> predicate = folderId.HasValue
            ? f => f.FolderId == folderId.Value
            : f => f.FolderId == null;
        return predicate;
    }

    /// <summary>合并两个谓词（AND 逻辑）。</summary>
    private static Expression<Func<T, bool>> CombineAnd<T>(
        Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var param = Expression.Parameter(typeof(T));
        var body = Expression.AndAlso(
            Expression.Invoke(left, param),
            Expression.Invoke(right, param));
        return Expression.Lambda<Func<T, bool>>(body, param);
    }

    private const int MaxBatchRead = 100000;
}
