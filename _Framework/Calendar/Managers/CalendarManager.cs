using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Transactions;
using TKW.Framework.Utility.Calendar.Recurrence;

namespace TKWF.Ext.Calendar
{
    /// <summary>
    /// 日历管理实现（public）——日历/事件生命周期 + occurrence 合并查询。
    /// <para>事务包裹（对齐 Approval/OU CONDITION-1）：事件 Create/Update（引用守卫 + 校验 + 多步写）、
    /// 日历 Delete（删除保护）经 <see cref="ITransactionManager"/> BeginAsync → 业务 → CommitAsync / 失败 RollbackAsync。</para>
    /// <para>RecurrenceEndUtc 同源推导（C2/D4）：UNTIL 直取；COUNT 用 <see cref="RecurrenceExpander"/> 展开
    /// （从 dtStart 到覆盖 COUNT 的充分范围，写入时受上限保护）取最后 occurrence——与查询展开同源，偏差构造性消除。</para>
    /// <para>数据访问红线：不注入 IFreeSql / IEntityDAC——只经 <see cref="ICalendarStore"/>（委托 DataService）。</para>
    /// <para>类为 public 但构造函数 internal（<see cref="ICalendarStore"/> 为 internal 契约）——
    /// 由 <see cref="CalendarExtensionInitializer{TUserInfo}.ConfigureServices(Microsoft.Extensions.DependencyInjection.IServiceCollection)"/>
    /// 以工厂方式 TryAddScoped 注册（消费方仍可自定义实现优先）。</para>
    /// </summary>
    public sealed class CalendarManager : ICalendarManager
    {
        private readonly ICalendarStore _store;
        private readonly ITransactionManager _transactionManager;
        private readonly ILogger<CalendarManager> _logger;

        /// <summary>COUNT 推导 RecurrenceEndUtc 的上限保护（防写入时 DoS——超大 COUNT 拒绝，C4）。</summary>
        private const int MaxDerivationCount = 100000;

        internal CalendarManager(
            ICalendarStore store,
            ITransactionManager transactionManager,
            ILogger<CalendarManager> logger)
        {
            _store = store ?? throw new ArgumentNullException(nameof(store));
            _transactionManager = transactionManager ?? throw new ArgumentNullException(nameof(transactionManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        // ── 日历 ──

        /// <inheritdoc />
        public async Task<CalendarEntity> CreateCalendarAsync(string code, string name,
            string? description = null, string? color = null, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(code);
            ArgumentException.ThrowIfNullOrWhiteSpace(name);

            var entity = new CalendarEntity
            {
                Code = code,
                Name = name,
                Description = description,
                Color = color,
                IsEnabled = true,
                CreateTime = DateTime.UtcNow,
                UpdateTime = DateTime.UtcNow
            };

            // 事务包裹（对齐 OU/Approval 写路径一致性）：单 Insert 亦走 Begin→Commit，保证失败全回滚
            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                // 并发同 Code 冲突由数据库唯一索引异常自然传播（败者显式异常，对齐 OU/Approval 先例）
                await _store.CreateAsync(entity, ct);

                await scope.CommitAsync(ct);
                return entity;
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task UpdateCalendarAsync(long id, string? name = null, string? description = null,
            string? color = null, bool? isEnabled = null, CancellationToken ct = default)
        {
            var entity = await _store.GetByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"日历 {id} 不存在");

            if (name != null) entity.Name = name;
            if (description != null) entity.Description = description;
            if (color != null) entity.Color = color;
            if (isEnabled.HasValue) entity.IsEnabled = isEnabled.Value;
            entity.UpdateTime = DateTime.UtcNow;

            await _store.UpdateAsync(entity, ct);
        }

        /// <inheritdoc />
        public async Task DeleteCalendarAsync(long id, CancellationToken ct = default)
        {
            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                var calendar = await _store.GetByIdAsync(id, ct)
                    ?? throw new InvalidOperationException($"日历 {id} 不存在");

                // 删除保护（D17/F7）：有事件 → 拒绝
                long eventCount = await _store.CountByCalendarIdAsync(id, ct);
                if (eventCount > 0)
                    throw new InvalidOperationException($"日历含 {eventCount} 个事件，请先删除");

                await _store.DeleteAsync(id, ct);

                await scope.CommitAsync(ct);
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }

        /// <inheritdoc />
        public Task<CalendarEntity?> GetCalendarAsync(long id, CancellationToken ct = default)
            => _store.GetByIdAsync(id, ct);

        /// <inheritdoc />
        public Task<IReadOnlyList<CalendarEntity>> GetCalendarsAsync(CancellationToken ct = default)
            => _store.GetAllAsync(ct);

        // ── 事件 ──

        /// <inheritdoc />
        public async Task<CalendarEventEntity> CreateEventAsync(
            long calendarId, string title, DateTime startUtc, DateTime? endUtc = null,
            string? description = null, string? location = null, bool allDay = false,
            string? recurrenceRule = null, CancellationToken ct = default)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(title);
            EnsureUtcKind(startUtc, nameof(startUtc));
            if (endUtc.HasValue)
            {
                EnsureUtcKind(endUtc.Value, nameof(endUtc));
                if (endUtc.Value < startUtc)
                    throw new ArgumentException("事件结束时间不能早于开始时间", nameof(endUtc));
            }

            // 规则校验（C3/D5）：非法规则串 fail-fast（FormatException 传播）；规范互逆——落库存 ToString()
            RecurrenceRule? rule = null;
            if (!string.IsNullOrEmpty(recurrenceRule))
                rule = RecurrenceRule.Parse(recurrenceRule);

            // C2 同源推导 RecurrenceEndUtc（UNTIL 直取 / COUNT 展开取最后 occurrence / 无界 null）
            DateTime? recurrenceEndUtc = DeriveRecurrenceEndUtc(rule, startUtc);

            // P2/P6：AllDay 事件起点归一为 UTC 日期午夜 + EndUtc 落库 = 次日 00:00（语义自洽）
            DateTime effectiveStartUtc = allDay ? startUtc.Date : startUtc;
            DateTime? finalEndUtc = allDay ? effectiveStartUtc.AddDays(1) : endUtc;

            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                // 引用守卫（F2/D3）：日历必须存在
                var calendar = await _store.GetByIdAsync(calendarId, ct)
                    ?? throw new InvalidOperationException($"日历 {calendarId} 不存在");

                var entity = new CalendarEventEntity
                {
                    CalendarId = calendarId,
                    Title = title,
                    StartUtc = effectiveStartUtc,
                    EndUtc = finalEndUtc,
                    Description = description,
                    Location = location,
                    AllDay = allDay,
                    RecurrenceRule = rule?.ToString(),
                    RecurrenceEndUtc = recurrenceEndUtc,
                    CreateTime = DateTime.UtcNow,
                    UpdateTime = DateTime.UtcNow
                };

                await _store.CreateEventAsync(entity, ct);

                await scope.CommitAsync(ct);
                return entity;
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task UpdateEventAsync(
            long id, long? calendarId = null, string? title = null, DateTime? startUtc = null,
            DateTime? endUtc = null, string? description = null, string? location = null,
            bool? allDay = null, string? recurrenceRule = null, CancellationToken ct = default)
        {
            using var scope = await _transactionManager.BeginAsync(ct: ct);
            try
            {
                var entity = await _store.GetEventByIdAsync(id, ct)
                    ?? throw new InvalidOperationException($"事件 {id} 不存在");

                // calendarId 变更 → 引用守卫
                if (calendarId.HasValue && calendarId.Value != entity.CalendarId)
                {
                    var target = await _store.GetByIdAsync(calendarId.Value, ct)
                        ?? throw new InvalidOperationException($"日历 {calendarId.Value} 不存在");
                    entity.CalendarId = calendarId.Value;
                }

                if (title != null) entity.Title = title;
                if (description != null) entity.Description = description;
                if (location != null) entity.Location = location;

                // startUtc 变更（先于 endUtc 校验 / AllDay 归一 / 规则重算）
                if (startUtc.HasValue)
                {
                    EnsureUtcKind(startUtc.Value, nameof(startUtc));
                    entity.StartUtc = startUtc.Value;
                }

                if (endUtc.HasValue)
                {
                    EnsureUtcKind(endUtc.Value, nameof(endUtc));
                    if (endUtc.Value < entity.StartUtc)
                        throw new ArgumentException("事件结束时间不能早于开始时间", nameof(endUtc));
                    entity.EndUtc = endUtc.Value;
                }

                if (allDay.HasValue)
                {
                    entity.AllDay = allDay.Value;
                    // P2/P6：AllDay 事件起点归一为 UTC 日期午夜 + EndUtc 落库 = 次日 00:00
                    if (allDay.Value)
                    {
                        entity.StartUtc = entity.StartUtc.Date;
                        entity.EndUtc = entity.StartUtc.AddDays(1);
                    }
                }

                // 规则/时间变更 → RecurrenceEndUtc 重算（D19/P7）
                if (recurrenceRule != null)
                {
                    if (recurrenceRule.Length == 0)
                    {
                        // 空串 = 清除重复（变单次事件）
                        entity.RecurrenceRule = null;
                        entity.RecurrenceEndUtc = null;
                    }
                    else
                    {
                        var rule = RecurrenceRule.Parse(recurrenceRule);   // FormatException 传播
                        entity.RecurrenceRule = rule.ToString();
                        entity.RecurrenceEndUtc = DeriveRecurrenceEndUtc(rule, entity.StartUtc);
                    }
                }
                else if (startUtc.HasValue && entity.RecurrenceRule != null)
                {
                    // 仅改 startUtc、保留规则 → 仍须重算（锚点移动）
                    var rule = RecurrenceRule.Parse(entity.RecurrenceRule);
                    entity.RecurrenceEndUtc = DeriveRecurrenceEndUtc(rule, entity.StartUtc);
                }

                entity.UpdateTime = DateTime.UtcNow;
                await _store.UpdateEventAsync(entity, ct);

                await scope.CommitAsync(ct);
            }
            catch
            {
                await scope.RollbackAsync(ct);
                throw;
            }
        }

        /// <inheritdoc />
        public async Task DeleteEventAsync(long id, CancellationToken ct = default)
        {
            var entity = await _store.GetEventByIdAsync(id, ct)
                ?? throw new InvalidOperationException($"事件 {id} 不存在");
            await _store.DeleteEventAsync(id, ct);
        }

        // ── occurrence 查询 ──

        /// <inheritdoc />
        public async Task<EventOccurrenceList> GetOccurrencesAsync(
            long? calendarId, DateTime fromUtc, DateTime toUtc, int maxCount = 1000, CancellationToken ct = default)
        {
            EnsureUtcKind(fromUtc, nameof(fromUtc));
            EnsureUtcKind(toUtc, nameof(toUtc));
            if (fromUtc >= toUtc)
                return new EventOccurrenceList(Array.Empty<EventOccurrence>(), false);
            if (maxCount <= 0)
                throw new ArgumentOutOfRangeException(nameof(maxCount), "maxCount 必须大于 0");

            // P3/D18：IsEnabled=false 日历默认排除——先取日历集判定（先例对齐：occurrence 过滤在查询侧）
            var calendars = await _store.GetAllAsync(ct);
            var enabledCalendarIds = calendars.Where(c => c.IsEnabled).Select(c => c.Id).ToHashSet();
            if (calendarId.HasValue && !enabledCalendarIds.Contains(calendarId.Value))
                return new EventOccurrenceList(Array.Empty<EventOccurrence>(), false);

            // C1 分路：单次 + 重复分别 SQL 查询（谓词下推——单次重叠判定 / 重复 RecurrenceEndUtc 预筛）
            var singles = await _store.GetSingleByRangeAsync(calendarId, fromUtc, toUtc, 0, MaxOccurrenceFetch, ct);
            var recurring = await _store.GetRecurringByRangeAsync(calendarId, fromUtc, toUtc, 0, MaxOccurrenceFetch, ct);

            var occurrences = new List<EventOccurrence>(singles.Count + recurring.Count * 8);

            // 单次直接命中（StartUtc < to 已由 SQL 下推；EndUtc 重叠判定在内存精确执行——
            // SQLite 参数/列墙钟语义在恰等边界不可靠（AllDay 次日 00:00 边界），见 DataService 注释）
            foreach (var e in singles)
            {
                if (!enabledCalendarIds.Contains(e.CalendarId)) continue;
                if (e.EndUtc.HasValue && e.EndUtc.Value <= fromUtc) continue;   // 半开 [from, to)：EndUtc == from 排除
                occurrences.Add(ToOccurrence(e, e.StartUtc));
            }

            // 重复事件展开（RecurrenceRule.Parse → RecurrenceExpander，绝对定位首发生——与锚点跨度无关，C4/D7）
            foreach (var e in recurring)
            {
                if (!enabledCalendarIds.Contains(e.CalendarId)) continue;
                if (string.IsNullOrEmpty(e.RecurrenceRule)) continue;

                var rule = RecurrenceRule.Parse(e.RecurrenceRule);
                var result = RecurrenceExpander.GetOccurrences(rule, e.StartUtc, fromUtc, toUtc, maxCount);
                foreach (var occ in result.Occurrences)
                    occurrences.Add(ToOccurrence(e, occ));
            }

            // 合并排序：StartUtc 升序（相等 + EventId 次级键，P6/D8）
            occurrences.Sort(static (a, b) =>
            {
                int c = a.StartUtc.CompareTo(b.StartUtc);
                return c != 0 ? c : a.EventId.CompareTo(b.EventId);
            });

            // 全局上限（P1）：per-event maxCount 只限单事件展开，恶意/大量重复事件可致总量暴涨——硬上限截断
            bool totalTruncated = occurrences.Count > MaxTotalOccurrences;
            if (totalTruncated)
                occurrences = occurrences.GetRange(0, MaxTotalOccurrences);

            // 上限截断（C4/D16）：总数超 maxCount → 显式 Truncated 标志；截断的是最晚 occurrence
            bool truncated = occurrences.Count > maxCount || totalTruncated;
            if (truncated)
                occurrences = occurrences.GetRange(0, Math.Min(maxCount, occurrences.Count));

            return new EventOccurrenceList(occurrences, truncated);
        }

        // ── 内部工具 ──

        /// <summary>UTC 契约校验（P7）：入口时间参数必须为 UTC Kind（fail-fast，对齐规则串校验风格）。</summary>
        private static void EnsureUtcKind(DateTime value, string paramName)
        {
            if (value.Kind != DateTimeKind.Utc)
                throw new ArgumentException($"时间参数 {paramName} 必须为 UTC（DateTimeKind.Utc）", paramName);
        }

        /// <summary>occurrence 合并总收集上限（P1 防 DoS——per-event maxCount 只限单事件，总量需硬上限）。</summary>
        private const int MaxTotalOccurrences = 1000000;

        /// <summary>
        /// RecurrenceEndUtc 同源推导（C2/D4）——与查询展开共用 <see cref="RecurrenceExpander"/>，偏差构造性消除：
        /// <list type="bullet">
        /// <item>UNTIL 有 → 直接取 UNTIL（含边界）；</item>
        /// <item>仅 COUNT → 从 dtStart 到<b>覆盖 COUNT 的充分范围</b>展开（严格上界：各频率每周期恒产生 ≥1 个 occurrence），
        ///      取最后一个 occurrence（受上限保护——COUNT 超限/展开截断 → 语义化 fail-fast，C4）；</item>
        /// <item>两者均无 → null（无限，查询侧上限防护）。</item>
        /// </list>
        /// </summary>
        private static DateTime? DeriveRecurrenceEndUtc(RecurrenceRule? rule, DateTime dtStartUtc)
        {
            if (rule == null)
                return null;
            if (rule.UntilUtc.HasValue)
                return rule.UntilUtc.Value;

            int? count = rule.Count;
            if (!count.HasValue)
                return null;   // 无界（无 COUNT/UNTIL）

            int countValue = count.Value;
            if (countValue <= 0)
                throw new InvalidOperationException($"重复规则 COUNT={countValue} 不会产生任何 occurrence");
            if (countValue > MaxDerivationCount)
                throw new InvalidOperationException($"重复规则 COUNT={countValue} 超过推导上限 {MaxDerivationCount}（可用 UNTIL 表达）");

            // 充分范围估算（严格覆盖 COUNT 次 occurrence 的上界）：
            // DAILY: +Interval*Count 天；WEEKLY: +7*Interval*Count 天（BYDAY 周内多天只会更密）；
            // MONTHLY: +Interval*Count 月（月末钳制仍恒 1/周期）；YEARLY: +Interval*Count 年（闰年钳制同理）。
            DateTime rangeEndUtc;
            try
            {
                rangeEndUtc = rule.Frequency switch
                {
                    RecurrenceFrequency.Daily => dtStartUtc.AddDays((double)rule.Interval * countValue),
                    RecurrenceFrequency.Weekly => dtStartUtc.AddDays(7.0 * rule.Interval * countValue),
                    RecurrenceFrequency.Monthly => dtStartUtc.AddMonths(checked(rule.Interval * countValue)),
                    RecurrenceFrequency.Yearly => dtStartUtc.AddYears(checked(rule.Interval * countValue)),
                    _ => throw new InvalidOperationException($"不支持的重复频率 {rule.Frequency}")
                };
            }
            catch (Exception ex) when (ex is ArgumentOutOfRangeException or OverflowException)
            {
                throw new InvalidOperationException("重复规则 COUNT/INTERVAL 过大，无法推导 RecurrenceEndUtc", ex);
            }

            var result = RecurrenceExpander.GetOccurrences(rule, dtStartUtc, dtStartUtc, rangeEndUtc, maxCount: countValue);
            if (result.Truncated || result.Occurrences.Count < countValue)
                throw new InvalidOperationException("重复规则展开结果不完整，无法推导 RecurrenceEndUtc");

            return result.Occurrences[^1];
        }

        /// <summary>occurrence 组装：EndUtc = occurrence 起点 + 事件时长偏移（EndUtc - StartUtc）；AllDay +1 天（P1/P2）。</summary>
        private static EventOccurrence ToOccurrence(CalendarEventEntity e, DateTime occStartUtc)
        {
            DateTime? occEndUtc;
            if (e.AllDay)
                occEndUtc = occStartUtc.AddDays(1);
            else if (e.EndUtc.HasValue)
                occEndUtc = occStartUtc + (e.EndUtc.Value - e.StartUtc);
            else
                occEndUtc = null;

            return new EventOccurrence(occStartUtc, occEndUtc, e.Id, e.CalendarId, e.Title, e.AllDay);
        }

        /// <summary>范围查询单次取数上限（对齐 DataService MaxBatchRead 防全表拉取）。</summary>
        private const int MaxOccurrenceFetch = 100000;
    }
}
