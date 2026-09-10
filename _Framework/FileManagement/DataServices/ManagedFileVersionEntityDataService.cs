using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;   // Oracle P2-1：MaxAsync（SQL MAX——FreeSqlQueryableExtensions）
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.FileManagement.DTOs;

namespace TKWF.Ext.FileManagement;

/// <summary>文件版本 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para>删除语义：<c>hasSoftDelete:false</c>——物理删除（实体不声明 IsDeleted）。</para>
/// <para>V0.2.0：版本行 append-only 不可变（CreateTime CanUpdate=false）——无 Update 业务方法。</para></summary>
partial class ManagedFileVersionEntityDataService(IDomainUser user, IEntityDAC<ManagedFileVersionEntity> dac)
    : DomainDataServiceBase<ManagedFileVersionEntity, ManagedFileVersionEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── Store 委托路径的业务方法（异常自然传播——版本号冲突语义由 Manager 层处理） ──

    /// <summary>按文件查询全部版本（Version 升序；上限 1000——P2-6，对齐 PrintTemplates ListVersionsByTemplateAsync 先例）。</summary>
    public async Task<IReadOnlyList<ManagedFileVersionEntity>> GetByFileAsync(long fileId, CancellationToken ct = default)
        => await EntitySelectAsync(v => v.FileId == fileId, 0, MaxVersionListSize,
            q => q.OrderBy(v => v.Version), ct);

    /// <summary>按文件 + 版本号查单个版本（UX_mfv_file_version 双条件；不存在返回 null）。</summary>
    public Task<ManagedFileVersionEntity?> GetByFileAndVersionAsync(long fileId, int version, CancellationToken ct = default)
        => EntityGetAsync(v => v.FileId == fileId && v.Version == version, ct);

    /// <summary>
    /// 查询文件当前最大版本号（Oracle P2-1 修复——SQL MAX 下推，非全量拉取取首行；无版本返回 0——首版 1 的前置）。
    /// <para>独立 QueryForUser() 起新查询（ISelect 原地可变陷阱）。</para>
    /// </summary>
    public Task<int> GetMaxVersionAsync(long fileId, CancellationToken ct = default)
        => QueryForUser()
            .Where(v => v.FileId == fileId)
            .MaxAsync(v => v.Version, ct);   // SQL MAX——空表返回 0（int 默认值）

    /// <summary>新增版本行（UX_mfv_file_version 唯一约束兜底并发冲突——败者由 Manager 补偿清理）。</summary>
    public Task<ManagedFileVersionEntity> CreateAsync(ManagedFileVersionEntity entity, CancellationToken ct = default)
        => EntityCreateAsync(entity, ct);

    /// <summary>
    /// 按文件物理删除全部版本行（文件删除清理，Oracle P1-2）。
    /// <para>Oracle P2-2 修复：循环删除直至无返回行——1000+ 版本文件不留孤儿行（单批上限防全量拉取）。</para>
    /// </summary>
    public async Task DeleteByFileIdAsync(long fileId, CancellationToken ct = default)
    {
        while (true)
        {
            var batch = await EntitySelectAsync(v => v.FileId == fileId, 0, DeleteBatchSize,
                q => q.OrderBy(v => v.Id), ct);
            if (batch.Count == 0) break;
            await EntityDeleteBatchAsync(batch.Select(v => v.Id), ct);
        }
    }

    /// <summary>
    /// 按文件查询全部版本 StoredPath（Oracle P2-2 修复——删除清理需全量 Blob 路径，循环拉取不留孤儿）。
    /// <para>独立批查询（每批 1000，循环直至无返回）——对齐 DeleteByFileIdAsync 循环语义。</para>
    /// </summary>
    public async Task<IReadOnlyList<string>> GetStoredPathsByFileAsync(long fileId, CancellationToken ct = default)
    {
        var paths = new List<string>();
        while (true)
        {
            var batch = await EntitySelectAsync(v => v.FileId == fileId, paths.Count, DeleteBatchSize,
                q => q.OrderBy(v => v.Id), ct);
            if (batch.Count == 0) break;
            paths.AddRange(batch.Select(v => v.StoredPath));
        }
        return paths;
    }

    /// <summary>版本列表查询上限（P2-6——防高频版本文件全量拉取）。</summary>
    private const int MaxVersionListSize = 1000;

    /// <summary>删除批量步长（P2-2 循环删除——每批上限）。</summary>
    private const int DeleteBatchSize = 1000;
}

