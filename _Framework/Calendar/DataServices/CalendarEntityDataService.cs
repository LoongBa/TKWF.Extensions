using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Calendar.DTOs;

namespace TKWF.Ext.Calendar;

/// <summary>日历 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para>删除语义：<c>hasSoftDelete:false</c>——物理删除（实体不声明 IsDeleted）。</para></summary>
partial class CalendarEntityDataService(IDomainUser user, IEntityDAC<CalendarEntity> dac)
    : DomainDataServiceBase<CalendarEntity, CalendarEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── Store/Manager 委托路径的业务方法（异常自然传播——事务/唯一冲突语义由 Manager 层处理） ──

    /// <summary>按编码查询日历（Code 唯一索引）。</summary>
    public async Task<CalendarEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
        => await EntityGetAsync(t => t.Code == code, ct);

    /// <summary>全量查询（按 Id 排序；occurrence 查询 IsEnabled 过滤 + 管理端列表用）。</summary>
    public async Task<IReadOnlyList<CalendarEntity>> GetAllAsync(CancellationToken ct = default)
        => await EntitySelectAsync(null, 0, MaxBatchRead, q => q.OrderBy(o => o.Id), ct);

    /// <summary>按 Id 集合批量查询（顺序无关，调用方自行排序）。</summary>
    public async Task<IReadOnlyList<CalendarEntity>> GetByIdsAsync(IReadOnlyList<long> ids, CancellationToken ct = default)
        => await EntitySelectAsync(t => ids.Contains(t.Id), 0, Math.Max(1, ids.Count), null, ct);

    /// <summary>按 Id 物理删除（hasSoftDelete:false——EntitySoftDeleteAsync 对未启用软删实体抛异常，故用批量物理删）。</summary>
    public async Task DeleteEntityAsync(long id, CancellationToken ct = default)
        => await EntityDeleteBatchAsync(new[] { id }, ct);

    private const int MaxBatchRead = 100000;
}
