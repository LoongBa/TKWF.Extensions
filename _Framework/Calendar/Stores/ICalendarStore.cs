using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Calendar
{
    /// <summary>
    /// 日历存储抽象（internal）——日历与事件的持久化操作，经 SG1 DataService 委托实现。
    /// <para>异常自然传播（不静默）：唯一约束冲突/业务异常由 <see cref="ICalendarManager"/> 处理；
    /// 事务由 Manager 层统一管理（Store 不触碰 <c>ITransactionManager</c>）。</para>
    /// <para>数据访问红线（2026-09-07 用户裁定）：Store 不注入 IFreeSql / IEntityDAC——只依赖 DataService。</para>
    /// </summary>
    internal interface ICalendarStore
    {
        // ── 日历 ──

        /// <summary>按 Id 读取日历（不存在返回 null）。</summary>
        Task<CalendarEntity?> GetByIdAsync(long id, CancellationToken ct = default);

        /// <summary>按 Code 读取日历（不存在返回 null）。</summary>
        Task<CalendarEntity?> GetByCodeAsync(string code, CancellationToken ct = default);

        /// <summary>全量读取（按 Id 排序）。</summary>
        Task<IReadOnlyList<CalendarEntity>> GetAllAsync(CancellationToken ct = default);

        /// <summary>新增日历（回写自增 Id，返回 Id）。</summary>
        Task<long> CreateAsync(CalendarEntity entity, CancellationToken ct = default);

        /// <summary>更新日历（全字段更新）。</summary>
        Task UpdateAsync(CalendarEntity entity, CancellationToken ct = default);

        /// <summary>物理删除日历（调用方确保无事件——删除保护由 Manager 层强制）。</summary>
        Task DeleteAsync(long id, CancellationToken ct = default);

        // ── 事件 ──

        /// <summary>按 Id 读取事件（不存在返回 null）。</summary>
        Task<CalendarEventEntity?> GetEventByIdAsync(long id, CancellationToken ct = default);

        /// <summary>单次事件范围查询（C1 分路谓词：RecurrenceRule==null + StartUtc 重叠判定，SQL 下推）。</summary>
        Task<IReadOnlyList<CalendarEventEntity>> GetSingleByRangeAsync(
            long? calendarId, DateTime? fromUtc, DateTime? toUtc, int skip, int take, CancellationToken ct = default);

        /// <summary>重复事件范围查询（C1 分路谓词：RecurrenceRule!=null + RecurrenceEndUtc 预筛，SQL 下推）。</summary>
        Task<IReadOnlyList<CalendarEventEntity>> GetRecurringByRangeAsync(
            long? calendarId, DateTime? fromUtc, DateTime? toUtc, int skip, int take, CancellationToken ct = default);

        /// <summary>按日历查全部事件（删除保护/审计用）。</summary>
        Task<IReadOnlyList<CalendarEventEntity>> GetByCalendarIdAsync(long calendarId, CancellationToken ct = default);

        /// <summary>按日历统计事件数（删除保护计数）。</summary>
        Task<long> CountByCalendarIdAsync(long calendarId, CancellationToken ct = default);

        /// <summary>新增事件（回写自增 Id，返回 Id）。</summary>
        Task<long> CreateEventAsync(CalendarEventEntity entity, CancellationToken ct = default);

        /// <summary>更新事件（全字段更新）。</summary>
        Task UpdateEventAsync(CalendarEventEntity entity, CancellationToken ct = default);

        /// <summary>按 Id 物理删除事件（occurrence 不落库——展开是查询时计算，D17）。</summary>
        Task DeleteEventAsync(long id, CancellationToken ct = default);
    }
}
