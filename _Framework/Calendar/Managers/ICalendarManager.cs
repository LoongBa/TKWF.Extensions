using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Calendar
{
    /// <summary>
    /// 日历管理门面（公开）——日历/事件 CRUD（引用守卫 + 校验 + RecurrenceEndUtc 同源推导 + 事务包裹）+ occurrence 查询。
    /// <para>业务规则：删除保护（D17）、EndUtc ≥ StartUtc 校验（P3）、RecurrenceRule.Parse fail-fast（C3/D5）、
    /// RecurrenceEndUtc 同源推导（C2/D4）、IsEnabled=false 日历排除（P3/D18）、展开上限截断 + Truncated 标志（C4/D16）。</para>
    /// </summary>
    public interface ICalendarManager
    {
        // ── 日历 ──

        /// <summary>创建日历（Code 唯一——并发冲突由数据库唯一索引异常自然传播，D1/D2）。</summary>
        Task<CalendarEntity> CreateCalendarAsync(string code, string name, string? description = null, string? color = null, CancellationToken ct = default);

        /// <summary>更新日历（仅更新非空字段 + UpdateTime）。</summary>
        Task UpdateCalendarAsync(long id, string? name = null, string? description = null, string? color = null, bool? isEnabled = null, CancellationToken ct = default);

        /// <summary>删除日历（删除保护：含事件 → InvalidOperationException，D17）。</summary>
        Task DeleteCalendarAsync(long id, CancellationToken ct = default);

        /// <summary>按 Id 读取日历（不存在返回 null）。</summary>
        Task<CalendarEntity?> GetCalendarAsync(long id, CancellationToken ct = default);

        /// <summary>全量读取日历（含禁用——occurrence 查询才过滤 IsEnabled，D18）。</summary>
        Task<IReadOnlyList<CalendarEntity>> GetCalendarsAsync(CancellationToken ct = default);

        // ── 事件 ──

        /// <summary>创建事件（日历存在引用守卫 + EndUtc ≥ StartUtc 校验 + 规则 Parse 校验 + RecurrenceEndUtc 同源推导 + 事务原子提交，D3/D4）。</summary>
        Task<CalendarEventEntity> CreateEventAsync(
            long calendarId, string title, DateTime startUtc, DateTime? endUtc = null,
            string? description = null, string? location = null, bool allDay = false,
            string? recurrenceRule = null, CancellationToken ct = default);

        /// <summary>更新事件（calendarId 变更引用守卫；改 startUtc/规则 → RecurrenceEndUtc 重算（D19）；空串规则 = 清除重复；事务包裹）。</summary>
        Task UpdateEventAsync(
            long id, long? calendarId = null, string? title = null, DateTime? startUtc = null,
            DateTime? endUtc = null, string? description = null, string? location = null,
            bool? allDay = null, string? recurrenceRule = null, CancellationToken ct = default);

        /// <summary>删除事件（仅删事件行——occurrence 不落库，D17）。</summary>
        Task DeleteEventAsync(long id, CancellationToken ct = default);

        // ── occurrence 查询 ──

        /// <summary>按日历 + 时间范围查 occurrence（单次直接命中 + 重复展开合并排序；IsEnabled=false 日历排除；上限截断 + Truncated 标志，D8/D16/D18）。</summary>
        Task<EventOccurrenceList> GetOccurrencesAsync(long? calendarId, DateTime fromUtc, DateTime toUtc, int maxCount = 1000, CancellationToken ct = default);
    }
}
