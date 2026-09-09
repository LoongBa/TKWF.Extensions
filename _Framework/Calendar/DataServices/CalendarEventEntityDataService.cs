using System;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Calendar.DTOs;

namespace TKWF.Ext.Calendar;

/// <summary>事件 DataService——<c>partial</c> 骨架（.g.cs 由 xCodeGen 生成承载 CRUD，不入库）。
/// <para>范围查询 C1 分路谓词（对齐 BackgroundJobs BuildFilterPredicate + CombineAnd 范式，SQL 下推）：</para>
/// <list type="bullet">
/// <item><see cref="GetSingleByRangeAsync"/>——单次分支（<c>RecurrenceRule == null</c>）：重叠判定
/// <c>StartUtc &lt; to</c> + <c>(EndUtc == null || EndUtc &gt; from)</c>（跨范围事件含入，F4）</item>
/// <item><see cref="GetRecurringByRangeAsync"/>——重复分支（<c>RecurrenceRule != null</c>）：
/// <c>(RecurrenceEndUtc == null || RecurrenceEndUtc &gt;= from)</c>——<b>不得混入 EndUtc &gt; from</b>
/// （重复事件 EndUtc 是首 occurrence 时长锚点，可能远早于 from，混入会系统性漏事件——C1 回归 D7）</item>
/// </list></summary>
partial class CalendarEventEntityDataService(IDomainUser user, IEntityDAC<CalendarEventEntity> dac)
    : DomainDataServiceBase<CalendarEventEntity, CalendarEventEntityDto>(user, dac, hasSoftDelete: false)
{
    // ── Store/Manager 委托路径的业务方法（异常自然传播） ──

    /// <summary>单次事件范围查询（C1 分路：RecurrenceRule==null 且 StartUtc 重叠判定；按 StartUtc → Id 升序）。</summary>
    public Task<List<CalendarEventEntity>> GetSingleByRangeAsync(
        long? calendarId, DateTime? fromUtc, DateTime? toUtc, int skip, int take, CancellationToken ct = default)
        => EntitySelectAsync(BuildSingleFilterPredicate(calendarId, fromUtc, toUtc), skip, take,
            q => q.OrderBy(e => e.StartUtc).ThenBy(e => e.Id), ct);

    /// <summary>重复事件范围查询（C1 分路：RecurrenceRule!=null 且 RecurrenceEndUtc 预筛；不得混入 EndUtc&gt;from）。</summary>
    public Task<List<CalendarEventEntity>> GetRecurringByRangeAsync(
        long? calendarId, DateTime? fromUtc, DateTime? toUtc, int skip, int take, CancellationToken ct = default)
        => EntitySelectAsync(BuildRecurringFilterPredicate(calendarId, fromUtc, toUtc), skip, take,
            q => q.OrderBy(e => e.StartUtc).ThenBy(e => e.Id), ct);

    /// <summary>按 Id 查事件实体（Manager 更新/删除定位用）。</summary>
    public Task<CalendarEventEntity?> GetByIdAsync(long id, CancellationToken ct = default)
        => EntityGetAsync(e => e.Id == id, ct);

    /// <summary>按日历查全部事件（删除保护/审计用）。</summary>
    public Task<List<CalendarEventEntity>> GetByCalendarIdAsync(long calendarId, CancellationToken ct = default)
        => EntitySelectAsync(e => e.CalendarId == calendarId, 0, MaxBatchRead, null, ct);

    /// <summary>按日历统计事件数（删除保护计数——SQL COUNT 下推）。</summary>
    public Task<long> CountByCalendarIdAsync(long calendarId, CancellationToken ct = default)
        => Dac.CountAsync(QueryForUser().Where(e => e.CalendarId == calendarId), ct);

    /// <summary>新增事件（回写自增 Id）。</summary>
    public Task<CalendarEventEntity> CreateAsync(CalendarEventEntity entity, CancellationToken ct = default)
        => EntityCreateAsync(entity, ct);

    /// <summary>更新事件（全字段更新，返回更新后实体）。</summary>
    public Task<CalendarEventEntity> UpdateAsync(CalendarEventEntity entity, CancellationToken ct = default)
        => EntityUpdateAsync(entity, ct);

    /// <summary>按 Id 批量物理删除（hasSoftDelete:false）。</summary>
    public Task<int> DeleteBatchAsync(IEnumerable<long> ids, CancellationToken ct = default)
        => EntityDeleteBatchAsync(ids, ct);

    /// <summary>单次分支谓词（C1）：RecurrenceRule==null + CalendarId + StartUtc &lt; to。
    /// <para><b>EndUtc 重叠判定不下推（SQLite 参数/列墙钟语义在恰等边界不可靠——AllDay 次日 00:00 边界
    /// 实测误含入）</b>：fromUtc 参数保留签名兼容，EndUtc &gt; from 判定移至 Manager 内存
    /// （Store 已规范化 UTC，精确比较）。SQL 下推仅 StartUtc &lt; to——永不漏事件（跨范围事件含入），
    /// 多取的边界行由 Manager 内存排除。</para></summary>
    private static Expression<Func<CalendarEventEntity, bool>> BuildSingleFilterPredicate(
        long? calendarId, DateTime? fromUtc, DateTime? toUtc)
    {
        Expression<Func<CalendarEventEntity, bool>> predicate = e => e.RecurrenceRule == null;
        if (calendarId.HasValue)
            predicate = CombineAnd(predicate, e => e.CalendarId == calendarId.Value);
        if (toUtc.HasValue)
            predicate = CombineAnd(predicate, e => e.StartUtc < toUtc.Value);
        return predicate;
    }

    /// <summary>重复分支谓词（C1）：RecurrenceRule!=null + CalendarId + StartUtc &lt; to + (RecurrenceEndUtc==null || RecurrenceEndUtc &gt;= from)。
    /// <para><b>不混入 EndUtc&gt;from</b>——重复事件 EndUtc 是首 occurrence 时长锚点（可能远早于 from），混入会漏事件。</para></summary>
    private static Expression<Func<CalendarEventEntity, bool>> BuildRecurringFilterPredicate(
        long? calendarId, DateTime? fromUtc, DateTime? toUtc)
    {
        Expression<Func<CalendarEventEntity, bool>> predicate = e => e.RecurrenceRule != null;
        if (calendarId.HasValue)
            predicate = CombineAnd(predicate, e => e.CalendarId == calendarId.Value);
        if (fromUtc.HasValue)
            predicate = CombineAnd(predicate, e => e.RecurrenceEndUtc == null || e.RecurrenceEndUtc >= fromUtc.Value);
        if (toUtc.HasValue)
            predicate = CombineAnd(predicate, e => e.StartUtc < toUtc.Value);
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
