using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
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

    /// <summary>查询文件当前最大版本号（无版本返回 0——首版 1 的前置；SQL MAX 下推）。</summary>
    public async Task<int> GetMaxVersionAsync(long fileId, CancellationToken ct = default)
    {
        var all = await EntitySelectAsync(v => v.FileId == fileId, 0, MaxVersionListSize,
            q => q.OrderByDescending(v => v.Version), ct);
        return all.Count > 0 ? all[0].Version : 0;
    }

    /// <summary>新增版本行（UX_mfv_file_version 唯一约束兜底并发冲突——败者由 Manager 补偿清理）。</summary>
    public Task<ManagedFileVersionEntity> CreateAsync(ManagedFileVersionEntity entity, CancellationToken ct = default)
        => EntityCreateAsync(entity, ct);

    /// <summary>按文件物理删除全部版本行（文件删除清理，Oracle P1-2）。</summary>
    public async Task DeleteByFileIdAsync(long fileId, CancellationToken ct = default)
    {
        var versions = await EntitySelectAsync(v => v.FileId == fileId, 0, MaxVersionListSize,
            q => q.OrderBy(v => v.Id), ct);
        if (versions.Count > 0)
            await EntityDeleteBatchAsync(versions.Select(v => v.Id), ct);
    }

    private const int MaxVersionListSize = 1000;
}
