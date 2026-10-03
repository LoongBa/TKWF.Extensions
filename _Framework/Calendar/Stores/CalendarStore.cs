using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Calendar
{
    /// <summary>
    /// 日历存储实现（internal）——经 <see cref="CalendarEntityDataService"/> +
    /// <see cref="CalendarEventEntityDataService"/>（SG1/xCodeGen 生成的 DataService）委托持久化，
    /// 遵循数据访问红线（2026-09-07 用户裁定）：扩展不直接注入 IFreeSql / IEntityDAC，只依赖 DataService。
    /// <para>异常自然传播（不静默）——唯一约束冲突/业务规则违反由 Manager 处理；事务由 Manager 统一管理。</para>
    /// </summary>
    internal sealed class CalendarStore : ICalendarStore
    {
        private readonly IDomainUser _user;
        private CalendarEntityDataService? _calendarDataService;
        private CalendarEventEntityDataService? _eventDataService;

        private CalendarEntityDataService CalendarDataService => _calendarDataService ??= _user.Use<CalendarEntityDataService>();
        private CalendarEventEntityDataService EventDataService => _eventDataService ??= _user.Use<CalendarEventEntityDataService>();

        public CalendarStore(IDomainUser user)
        {
            _user = user ?? throw new ArgumentNullException(nameof(user));
        }

        // ── 日历 ──

        public Task<CalendarEntity?> GetByIdAsync(long id, CancellationToken ct = default)
            => CalendarDataService.EntityGetAsync(c => c.Id == id, ct);

        public Task<CalendarEntity?> GetByCodeAsync(string code, CancellationToken ct = default)
            => CalendarDataService.GetByCodeAsync(code, ct);

        public Task<IReadOnlyList<CalendarEntity>> GetAllAsync(CancellationToken ct = default)
            => CalendarDataService.GetAllAsync(ct);

        public async Task<long> CreateAsync(CalendarEntity entity, CancellationToken ct = default)
        {
            await CalendarDataService.EntityCreateAsync(entity, ct);
            return entity.Id;
        }

        public Task UpdateAsync(CalendarEntity entity, CancellationToken ct = default)
            => CalendarDataService.EntityUpdateAsync(entity, ct);

        public Task DeleteAsync(long id, CancellationToken ct = default)
            => CalendarDataService.DeleteEntityAsync(id, ct);

        // ── 事件 ──

        public async Task<CalendarEventEntity?> GetEventByIdAsync(long id, CancellationToken ct = default)
            => NormalizeEvent(await EventDataService.GetByIdAsync(id, ct));

        public async Task<IReadOnlyList<CalendarEventEntity>> GetSingleByRangeAsync(
            long? calendarId, DateTime? fromUtc, DateTime? toUtc, int skip, int take, CancellationToken ct = default)
            => NormalizeEvents(await EventDataService.GetSingleByRangeAsync(calendarId, fromUtc, toUtc, skip, take, ct));

        public async Task<IReadOnlyList<CalendarEventEntity>> GetRecurringByRangeAsync(
            long? calendarId, DateTime? fromUtc, DateTime? toUtc, int skip, int take, CancellationToken ct = default)
            => NormalizeEvents(await EventDataService.GetRecurringByRangeAsync(calendarId, fromUtc, toUtc, skip, take, ct));

        public async Task<IReadOnlyList<CalendarEventEntity>> GetByCalendarIdAsync(long calendarId, CancellationToken ct = default)
            => NormalizeEvents(await EventDataService.GetByCalendarIdAsync(calendarId, ct));

        public Task<long> CountByCalendarIdAsync(long calendarId, CancellationToken ct = default)
            => EventDataService.CountByCalendarIdAsync(calendarId, ct);

        public async Task<long> CreateEventAsync(CalendarEventEntity entity, CancellationToken ct = default)
        {
            await EventDataService.CreateAsync(entity, ct);
            return entity.Id;
        }

        public Task UpdateEventAsync(CalendarEventEntity entity, CancellationToken ct = default)
            => EventDataService.UpdateAsync(entity, ct);

        public Task DeleteEventAsync(long id, CancellationToken ct = default)
            => EventDataService.DeleteBatchAsync(new[] { id }, ct);

        // ── 内部：SQLite DateTime 读出规范化 ──
        // FreeSql SQLite 把 UTC DateTime 存为本地墙钟（无 Kind 标识），读出 Kind=Unspecified 且偏移 +8
        // （调研/Tagging 先例）——统一按"本地墙钟语义"转回 UTC，保证 occurrence 计算/校验绝对正确。

        private static IReadOnlyList<CalendarEventEntity> NormalizeEvents(IReadOnlyList<CalendarEventEntity> events)
        {
            if (events.Count == 0) return events;
            foreach (var e in events) NormalizeEvent(e);
            return events;
        }

        private static CalendarEventEntity? NormalizeEvent(CalendarEventEntity? e)
        {
            if (e == null) return null;
            e.StartUtc = NormalizeUtc(e.StartUtc);
            if (e.EndUtc.HasValue) e.EndUtc = NormalizeUtc(e.EndUtc.Value);
            if (e.RecurrenceEndUtc.HasValue) e.RecurrenceEndUtc = NormalizeUtc(e.RecurrenceEndUtc.Value);
            return e;
        }

        private static DateTime NormalizeUtc(DateTime value)
            => value.Kind switch
            {
                DateTimeKind.Utc => value,
                DateTimeKind.Unspecified => DateTime.SpecifyKind(value, DateTimeKind.Local).ToUniversalTime(),
                _ => value.ToUniversalTime(),   // Local（P5：与 UtcAssert 语义对齐；SQLite 实际恒 Unspecified，防御分支）
            };
    }
}
