using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Utility.Calendar.Recurrence;
using TKWF.Ext.Calendar;

namespace TKWF.Ext.Calendar.Tests;

/// <summary>
/// Calendar 扩展业务门面测试——D1-D8（日历/事件 CRUD + 范围查询 + occurrence 展开合并）+ D17-D19（删除保护/禁用过滤/更新重算）。
/// <para>每用例独立 SQLite 内存库（<see cref="CalendarTestHost.Create"/>）；DateTime 断言统一
/// <see cref="UtcAssert"/>（Unspecified 视为 UTC，对齐 ADR D5 SQLite Kind 容错）。</para>
/// </summary>
public class CalendarManagerTests
{
    private static CalendarTestHost NewHost() => CalendarTestHost.Create();

    /// <summary>构造 UTC 时刻（Kind=Utc）。</summary>
    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0, int second = 0)
        => new DateTime(year, month, day, hour, minute, second, DateTimeKind.Utc);

    // ── D1 创建日历：字段落库 + IsEnabled 默认 true ──

    [Fact]
    public async Task CreateCalendar_PersistsAllFields_EnabledByDefault()
    {
        using var host = NewHost();

        var created = await host.Manager.CreateCalendarAsync(
            "WORK", "工作日历", "主工作日历", "#3498DB", ct: CancellationToken.None);

        Assert.True(created.Id > 0);
        Assert.Equal("WORK", created.Code);
        Assert.Equal("工作日历", created.Name);
        Assert.Equal("主工作日历", created.Description);
        Assert.Equal("#3498DB", created.Color);
        Assert.True(created.IsEnabled);

        // 持久化复验
        var reloaded = await host.Manager.GetCalendarAsync(created.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal("WORK", reloaded!.Code);
        Assert.Equal("主工作日历", reloaded.Description);
        Assert.Equal("#3498DB", reloaded.Color);
        Assert.True(reloaded.IsEnabled);
    }

    // ── D2 重复 Code 拒绝（库级唯一约束） ──

    [Fact]
    public async Task CreateCalendar_DuplicateCode_Throws()
    {
        using var host = NewHost();
        await host.Manager.CreateCalendarAsync("DUP", "First", ct: CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(
            () => host.Manager.CreateCalendarAsync("DUP", "Second", ct: CancellationToken.None));

        // 库级唯一约束拒绝 → 仅首行落库
        var all = await host.Manager.GetCalendarsAsync(CancellationToken.None);
        Assert.Single(all);
        Assert.Equal("First", all[0].Name);
    }

    // ── D3 创建单次事件：归属校验 + EndUtc ≥ StartUtc ──

    [Fact]
    public async Task CreateEvent_CalendarNotFound_ThrowsInvalidOperationException()
    {
        using var host = NewHost();

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Manager.CreateEventAsync(999, "孤儿事件", Utc(2026, 9, 9, 9), ct: CancellationToken.None));
    }

    [Fact]
    public async Task CreateEvent_EndUtcBeforeStart_ThrowsArgumentException()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);

        await Assert.ThrowsAsync<ArgumentException>(
            () => host.Manager.CreateEventAsync(cal.Id, "倒置", Utc(2026, 9, 9, 10), Utc(2026, 9, 9, 9), ct: CancellationToken.None));
    }

    [Fact]
    public async Task CreateEvent_SingleEvent_PersistsFields()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);

        var evt = await host.Manager.CreateEventAsync(
            cal.Id, "晨会", Utc(2026, 9, 9, 9, 0), Utc(2026, 9, 9, 9, 30),
            "站会", "3F 会议室", ct: CancellationToken.None);

        Assert.True(evt.Id > 0);
        Assert.Equal(cal.Id, evt.CalendarId);
        Assert.Equal("晨会", evt.Title);
        Assert.Equal("站会", evt.Description);
        Assert.Equal("3F 会议室", evt.Location);
        Assert.False(evt.AllDay);
        Assert.Null(evt.RecurrenceRule);
        UtcAssert.Equal(Utc(2026, 9, 9, 9, 0), evt.StartUtc);
        UtcAssert.Equal(Utc(2026, 9, 9, 9, 30), evt.EndUtc!.Value);
    }

    // ── D4 创建重复事件：RecurrenceEndUtc 同源推导（UNTIL 直取 / COUNT 展开最后 / 无界 null） ──

    [Fact]
    public async Task CreateEvent_RecurringWithUntil_StoresUntilDirectly()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        var until = Utc(2026, 12, 31, 23, 59, 59);

        var evt = await host.Manager.CreateEventAsync(
            cal.Id, "周会", Utc(2026, 9, 9, 10),
            recurrenceRule: "FREQ=WEEKLY;BYDAY=MO;UNTIL=20261231T235959Z", ct: CancellationToken.None);

        Assert.Equal("FREQ=WEEKLY;INTERVAL=1;BYDAY=MO;UNTIL=20261231T235959Z", evt.RecurrenceRule);
        Assert.NotNull(evt.RecurrenceEndUtc);
        // UNTIL 分支：直接取 UNTIL 值
        UtcAssert.Equal(until, evt.RecurrenceEndUtc!.Value);
    }

    [Fact]
    public async Task CreateEvent_RecurringWithCount_DerivesLastOccurrence()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);

        // FREQ=DAILY;COUNT=5 → 最后（第 5 次）occurrence = dtStart + 4 天——RecurrenceEndUtc 与展开同源（C2）
        var evt = await host.Manager.CreateEventAsync(
            cal.Id, "巡检", Utc(2026, 9, 9, 9, 0), recurrenceRule: "FREQ=DAILY;COUNT=5", ct: CancellationToken.None);

        Assert.NotNull(evt.RecurrenceEndUtc);
        UtcAssert.Equal(Utc(2026, 9, 13, 9, 0), evt.RecurrenceEndUtc!.Value);
    }

    // ── C1 回归（审核）：WEEKLY+COUNT 创建/更新必须成功（此前 Expander 误标截断致推导抛异常） ──

    [Fact]
    public async Task CreateEvent_RecurringWeeklyWithCount_Succeeds_DerivesLastOccurrence()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);

        // 无 BYDAY：COUNT=5 → 5 个周一（dtStart=9/9 周一），最后 = 9/9 + 4 周
        var evt = await host.Manager.CreateEventAsync(
            cal.Id, "周会", Utc(2026, 9, 9, 9, 0), recurrenceRule: "FREQ=WEEKLY;COUNT=5", ct: CancellationToken.None);
        Assert.NotNull(evt.RecurrenceEndUtc);
        UtcAssert.Equal(Utc(2026, 10, 7, 9, 0), evt.RecurrenceEndUtc!.Value);

        // BYDAY=MO,WE,FR：COUNT=4 → 9/9(一) 9/11(三) 9/14(一) 9/16(三)——最后 = 9/16
        var evt2 = await host.Manager.CreateEventAsync(
            cal.Id, "例会", Utc(2026, 9, 9, 9, 0), recurrenceRule: "FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=4", ct: CancellationToken.None);
        Assert.NotNull(evt2.RecurrenceEndUtc);
        UtcAssert.Equal(Utc(2026, 9, 16, 9, 0), evt2.RecurrenceEndUtc!.Value);
    }

    [Fact]
    public async Task CreateEvent_RecurringCount1_DerivesDtStart()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);

        // COUNT=1 → 唯一 occurrence = dtStart（P4 补漏：管理器级 COUNT=1 推导）
        var evt = await host.Manager.CreateEventAsync(
            cal.Id, "单次重复", Utc(2026, 9, 9, 9, 0), recurrenceRule: "FREQ=DAILY;COUNT=1", ct: CancellationToken.None);

        Assert.NotNull(evt.RecurrenceEndUtc);
        UtcAssert.Equal(Utc(2026, 9, 9, 9, 0), evt.RecurrenceEndUtc!.Value);
    }

    [Fact]
    public async Task CreateEvent_RecurringCount_AllOccurrencesAtOrBeforeEnd()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);

        // 性质断言（P4）：COUNT 事件的全部 occurrence ≤ RecurrenceEndUtc（同源推导不低估）
        var evt = await host.Manager.CreateEventAsync(
            cal.Id, "月末", Utc(2026, 1, 31, 9, 0), recurrenceRule: "FREQ=MONTHLY;COUNT=6", ct: CancellationToken.None);
        Assert.NotNull(evt.RecurrenceEndUtc);

        var occurrences = RecurrenceExpander.GetOccurrences(
            RecurrenceRule.Parse(evt.RecurrenceRule!), evt.StartUtc, evt.StartUtc, evt.RecurrenceEndUtc.Value.AddDays(1));
        Assert.Equal(6, occurrences.Occurrences.Count);
        Assert.All(occurrences.Occurrences, occ => Assert.True(occ <= evt.RecurrenceEndUtc!.Value, $"occurrence {occ:O} 超过 RecurrenceEndUtc"));
        UtcAssert.Equal(evt.RecurrenceEndUtc.Value, occurrences.Occurrences[^1]);
    }

    [Fact]
    public async Task CreateEvent_RecurringUnbounded_RecurrenceEndUtcNull()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);

        var evt = await host.Manager.CreateEventAsync(
            cal.Id, "无限", Utc(2026, 9, 9, 9), recurrenceRule: "FREQ=DAILY", ct: CancellationToken.None);

        Assert.NotNull(evt.RecurrenceRule);
        // 无界规则（无 COUNT/UNTIL）→ RecurrenceEndUtc null，查询侧上限防护
        Assert.Null(evt.RecurrenceEndUtc);
    }

    // ── D5 非法规则串 fail-fast → FormatException ──

    [Fact]
    public async Task CreateEvent_InvalidRule_CountAndUntilTogether_ThrowsFormatException()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);

        // C3：COUNT 与 UNTIL 互斥（RFC 5545 MUST NOT 同现）→ fail-fast
        await Assert.ThrowsAsync<FormatException>(
            () => host.Manager.CreateEventAsync(cal.Id, "坏规则", Utc(2026, 9, 9, 9),
                recurrenceRule: "FREQ=DAILY;COUNT=3;UNTIL=20261231T235959Z", ct: CancellationToken.None));
    }

    [Fact]
    public async Task CreateEvent_InvalidRule_UnknownClause_ThrowsFormatException()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);

        // 未知子句（BYMONTH 不在子集）→ fail-fast，不静默忽略
        await Assert.ThrowsAsync<FormatException>(
            () => host.Manager.CreateEventAsync(cal.Id, "未知子句", Utc(2026, 9, 9, 9),
                recurrenceRule: "FREQ=WEEKLY;BYMONTH=1", ct: CancellationToken.None));
    }

    // ── D6 单次事件范围查询：重叠判定 + AllDay 跨午夜边界 ──

    [Fact]
    public async Task GetOccurrences_SingleEvent_CrossingRange_Included()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        // 事件横跨范围：StartUtc 早于 from，EndUtc 落在 [from, to) 内 → 重叠含入
        await host.Manager.CreateEventAsync(cal.Id, "跨范围", Utc(2026, 9, 8, 22), Utc(2026, 9, 9, 2), ct: CancellationToken.None);

        var result = await host.Manager.GetOccurrencesAsync(
            cal.Id, Utc(2026, 9, 9, 0, 0), Utc(2026, 9, 10, 0, 0), ct: CancellationToken.None);

        var occ = Assert.Single(result.Occurrences);
        Assert.Equal("跨范围", occ.Title);
        Assert.False(result.Truncated);
    }

    [Fact]
    public async Task GetOccurrences_SingleEvent_OutsideRange_Excluded()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        // 完全在范围前（EndUtc <= from）→ 排除
        await host.Manager.CreateEventAsync(cal.Id, "过去", Utc(2026, 9, 1, 9), Utc(2026, 9, 1, 10), ct: CancellationToken.None);
        // 完全在范围后（StartUtc >= to）→ 排除
        await host.Manager.CreateEventAsync(cal.Id, "未来", Utc(2026, 9, 11, 9), Utc(2026, 9, 11, 10), ct: CancellationToken.None);

        var result = await host.Manager.GetOccurrencesAsync(
            cal.Id, Utc(2026, 9, 9, 0, 0), Utc(2026, 9, 10, 0, 0), ct: CancellationToken.None);

        Assert.Empty(result.Occurrences);
    }

    [Fact]
    public async Task GetOccurrences_SingleEvent_NoEndUtc_Instant_Included()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        // 瞬时事件（EndUtc null）：StartUtc 在 [from, to) 内 → 含入
        await host.Manager.CreateEventAsync(cal.Id, "瞬时", Utc(2026, 9, 9, 12), ct: CancellationToken.None);

        var result = await host.Manager.GetOccurrencesAsync(
            cal.Id, Utc(2026, 9, 9, 0, 0), Utc(2026, 9, 10, 0, 0), ct: CancellationToken.None);

        var occ = Assert.Single(result.Occurrences);
        Assert.Equal("瞬时", occ.Title);
        Assert.Null(occ.EndUtc);
    }

    [Fact]
    public async Task GetOccurrences_AllDay_MidnightBoundary_Excluded()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        // AllDay 语义：EndUtc = 次日 00:00 UTC——半开区间 [from, to) 下 AllDay 日与 to 同刻时排除（自洽）
        await host.Manager.CreateEventAsync(cal.Id, "全天", Utc(2026, 9, 9, 0, 0), Utc(2026, 9, 10, 0, 0),
            allDay: true, ct: CancellationToken.None);

        // [次日 00:00, ...) → 该 AllDay 日排除
        var nextDay = await host.Manager.GetOccurrencesAsync(
            cal.Id, Utc(2026, 9, 10, 0, 0), Utc(2026, 9, 11, 0, 0), ct: CancellationToken.None);
        Assert.Empty(nextDay.Occurrences);

        // 当天 [09-09, 09-10) → 含入
        var sameDay = await host.Manager.GetOccurrencesAsync(
            cal.Id, Utc(2026, 9, 9, 0, 0), Utc(2026, 9, 10, 0, 0), ct: CancellationToken.None);
        Assert.Single(sameDay.Occurrences);
    }

    // ── D7 C1 回归：重复事件 dtStart/EndUtc 远早于 from → occurrence 必须返回 ──

    [Fact]
    public async Task GetOccurrences_Recurring_AnchorFarBeforeFrom_ReturnsOccurrence()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        // C1 回归：dtStart=2020-01-01 且 EndUtc（首 occurrence 时长锚点）同样远早于 from=2024——
        // 重复路径谓词（CalendarId + StartUtc<to + (RecurrenceEndUtc==null || RecurrenceEndUtc>=from)）
        // 不得混入 EndUtc > from，否则系统性漏事件
        await host.Manager.CreateEventAsync(
            cal.Id, "每日巡检", Utc(2020, 1, 1, 9, 0), Utc(2020, 1, 1, 10, 0),
            recurrenceRule: "FREQ=DAILY", ct: CancellationToken.None);

        var result = await host.Manager.GetOccurrencesAsync(
            cal.Id, Utc(2024, 1, 1, 0, 0), Utc(2024, 1, 2, 0, 0), ct: CancellationToken.None);

        var occ = Assert.Single(result.Occurrences);
        Assert.Equal("每日巡检", occ.Title);
        UtcAssert.Equal(Utc(2024, 1, 1, 9, 0), occ.StartUtc);
        // 时长偏移：occurrence EndUtc = 起点 + 锚点时长（1 小时）
        UtcAssert.Equal(Utc(2024, 1, 1, 10, 0), occ.EndUtc!.Value);
    }

    // ── D8 occurrence 合并：单次 + 重复排序 + EndUtc 偏移 ──

    [Fact]
    public async Task GetOccurrences_MergesSingleAndRecurring_SortedByStartUtcThenEventId()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        // 先建重复事件（Id 更小），再建单次事件（Id 更大）——09-10 09:00 撞点验证 EventId 次级键（P6）
        var recurring = await host.Manager.CreateEventAsync(
            cal.Id, "重复", Utc(2026, 9, 9, 9, 0), Utc(2026, 9, 9, 10, 0),
            recurrenceRule: "FREQ=DAILY;COUNT=3", ct: CancellationToken.None);   // 09-09/09-10/09-11 09:00
        var single = await host.Manager.CreateEventAsync(
            cal.Id, "单次", Utc(2026, 9, 10, 9, 0), Utc(2026, 9, 10, 10, 0), ct: CancellationToken.None);

        var result = await host.Manager.GetOccurrencesAsync(
            cal.Id, Utc(2026, 9, 9, 0, 0), Utc(2026, 9, 12, 0, 0), ct: CancellationToken.None);

        Assert.Equal(4, result.Occurrences.Count);
        // 合并排序：StartUtc 升序；09-10 09:00 撞点按 EventId 升序（重复先建 → 重复在前）
        UtcAssert.Equal(Utc(2026, 9, 9, 9, 0), result.Occurrences[0].StartUtc);
        UtcAssert.Equal(Utc(2026, 9, 10, 9, 0), result.Occurrences[1].StartUtc);
        UtcAssert.Equal(Utc(2026, 9, 10, 9, 0), result.Occurrences[2].StartUtc);
        UtcAssert.Equal(Utc(2026, 9, 11, 9, 0), result.Occurrences[3].StartUtc);
        Assert.Equal(recurring.Id, result.Occurrences[1].EventId);
        Assert.Equal(single.Id, result.Occurrences[2].EventId);
    }

    [Fact]
    public async Task GetOccurrences_AllDay_EndUtc_PlusOneDay()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        await host.Manager.CreateEventAsync(cal.Id, "全天", Utc(2026, 9, 9, 0, 0), Utc(2026, 9, 10, 0, 0),
            allDay: true, ct: CancellationToken.None);

        var result = await host.Manager.GetOccurrencesAsync(
            cal.Id, Utc(2026, 9, 9, 0, 0), Utc(2026, 9, 10, 0, 0), ct: CancellationToken.None);

        // AllDay occurrence：EndUtc = StartUtc + 1 天（P2 语义）
        var occ = Assert.Single(result.Occurrences);
        Assert.True(occ.AllDay);
        UtcAssert.Equal(Utc(2026, 9, 10, 0, 0), occ.EndUtc!.Value);
    }

    [Fact]
    public async Task GetOccurrences_NonAllDay_EndUtc_DurationOffset()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        // 首 occurrence 时长锚点 = 2 小时 → 展开 occurrence EndUtc = 起点 + 2h
        await host.Manager.CreateEventAsync(
            cal.Id, "两小时", Utc(2026, 9, 9, 9, 0), Utc(2026, 9, 9, 11, 0),
            recurrenceRule: "FREQ=WEEKLY;BYDAY=MO", ct: CancellationToken.None);

        // 2026-09-14 为周一（锚点 09-09 周三 +5 天）
        var result = await host.Manager.GetOccurrencesAsync(
            cal.Id, Utc(2026, 9, 14, 0, 0), Utc(2026, 9, 15, 0, 0), ct: CancellationToken.None);

        var occ = Assert.Single(result.Occurrences);
        UtcAssert.Equal(Utc(2026, 9, 14, 9, 0), occ.StartUtc);
        UtcAssert.Equal(Utc(2026, 9, 14, 11, 0), occ.EndUtc!.Value);
    }

    // ── D17 删除保护：有事件日历禁删 / 空日历可删 / 删事件仅删事件行 ──

    [Fact]
    public async Task DeleteCalendar_WithEvents_ThrowsInvalidOperationException()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        await host.Manager.CreateEventAsync(cal.Id, "E", Utc(2026, 9, 9, 9), ct: CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Manager.DeleteCalendarAsync(cal.Id, CancellationToken.None));

        // 删除被拒 → 日历仍存在
        Assert.NotNull(await host.Manager.GetCalendarAsync(cal.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteCalendar_WithoutEvents_Succeeds()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);

        await host.Manager.DeleteCalendarAsync(cal.Id, CancellationToken.None);

        Assert.Empty(await host.Manager.GetCalendarsAsync(CancellationToken.None));
    }

    [Fact]
    public async Task DeleteEvent_RemovesOnlyEventRow()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        var evt = await host.Manager.CreateEventAsync(cal.Id, "E1", Utc(2026, 9, 9, 9), ct: CancellationToken.None);

        await host.Manager.DeleteEventAsync(evt.Id, CancellationToken.None);

        // occurrence 不落库（查询时展开）——删事件行后范围查询不再含该事件
        var result = await host.Manager.GetOccurrencesAsync(
            cal.Id, Utc(2026, 1, 1, 0, 0), Utc(2026, 12, 31, 0, 0), ct: CancellationToken.None);
        Assert.Empty(result.Occurrences);
        // 日历仍在
        Assert.NotNull(await host.Manager.GetCalendarAsync(cal.Id, CancellationToken.None));
    }

    // ── D18 IsEnabled=false 日历排除 occurrence 查询，GetCalendars 仍返回 ──

    [Fact]
    public async Task GetOccurrences_DisabledCalendar_Excluded()
    {
        using var host = NewHost();
        var enabled = await host.Manager.CreateCalendarAsync("ON", "启用", ct: CancellationToken.None);
        var disabled = await host.Manager.CreateCalendarAsync("OFF", "停用", ct: CancellationToken.None);
        await host.Manager.UpdateCalendarAsync(disabled.Id, isEnabled: false, ct: CancellationToken.None);
        await host.Manager.CreateEventAsync(enabled.Id, "在启用日历", Utc(2026, 9, 9, 9), ct: CancellationToken.None);
        await host.Manager.CreateEventAsync(disabled.Id, "在停用日历", Utc(2026, 9, 9, 9), ct: CancellationToken.None);

        // 不指定日历 → 全量：P3 默认排除 IsEnabled=false 日历的事件
        var result = await host.Manager.GetOccurrencesAsync(
            null, Utc(2026, 9, 9, 0, 0), Utc(2026, 9, 10, 0, 0), ct: CancellationToken.None);

        var occ = Assert.Single(result.Occurrences);
        Assert.Equal("在启用日历", occ.Title);
    }

    [Fact]
    public async Task GetCalendars_ReturnsDisabledCalendarToo()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("OFF", "停用", ct: CancellationToken.None);
        await host.Manager.UpdateCalendarAsync(cal.Id, isEnabled: false, ct: CancellationToken.None);

        var all = await host.Manager.GetCalendarsAsync(CancellationToken.None);

        Assert.Single(all);
        Assert.False(all[0].IsEnabled);
    }

    // ── D19 重复事件更新：改规则/时间 → RecurrenceEndUtc 重算；改 calendarId → 引用守卫 ──

    [Fact]
    public async Task UpdateEvent_ChangeRule_RecalculatesRecurrenceEndUtc()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        var evt = await host.Manager.CreateEventAsync(
            cal.Id, "巡检", Utc(2026, 9, 9, 9), recurrenceRule: "FREQ=DAILY;COUNT=5", ct: CancellationToken.None);
        UtcAssert.Equal(Utc(2026, 9, 13, 9, 0), evt.RecurrenceEndUtc!.Value);   // dtStart + 4 天

        // 改规则 → RecurrenceEndUtc 同源重算（COUNT=5 → COUNT=10，最后 occurrence = dtStart + 9 天）
        await host.Manager.UpdateEventAsync(evt.Id, recurrenceRule: "FREQ=DAILY;COUNT=10", ct: CancellationToken.None);

        var reloaded = await host.EventDataService.EntityGetAsync(e => e.Id == evt.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        Assert.Equal("FREQ=DAILY;INTERVAL=1;COUNT=10", reloaded!.RecurrenceRule);
        UtcAssert.Equal(Utc(2026, 9, 18, 9, 0), reloaded.RecurrenceEndUtc!.Value);
    }

    [Fact]
    public async Task UpdateEvent_ChangeStartUtc_RecalculatesRecurrenceEndUtc()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        var evt = await host.Manager.CreateEventAsync(
            cal.Id, "巡检", Utc(2026, 9, 9, 9), recurrenceRule: "FREQ=DAILY;COUNT=5", ct: CancellationToken.None);

        // 改 StartUtc → 最后 occurrence 同源平移（新 dtStart + 4 天）
        await host.Manager.UpdateEventAsync(evt.Id, startUtc: Utc(2026, 9, 20, 9), ct: CancellationToken.None);

        var reloaded = await host.EventDataService.EntityGetAsync(e => e.Id == evt.Id, CancellationToken.None);
        Assert.NotNull(reloaded);
        UtcAssert.Equal(Utc(2026, 9, 24, 9, 0), reloaded!.RecurrenceEndUtc!.Value);
    }

    [Fact]
    public async Task UpdateEvent_ChangeCalendarId_ToMissingCalendar_Throws()
    {
        using var host = NewHost();
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        var evt = await host.Manager.CreateEventAsync(cal.Id, "E", Utc(2026, 9, 9, 9), ct: CancellationToken.None);

        // 引用守卫：迁移到不存在的日历 → 拒绝
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Manager.UpdateEventAsync(evt.Id, calendarId: 9999, ct: CancellationToken.None));
    }
}
