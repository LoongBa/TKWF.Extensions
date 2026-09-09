using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.Calendar;

namespace TKWF.Ext.Calendar.Tests;

/// <summary>
/// ICalendarStore 委托正确性测试——经 Store 写入 → 经 DataService 可查（红线合规：Store 仅委托 SG1 DataService，不触碰 ORM/IEntityDAC）
/// + 分路谓词正确性（C1：单次分支不含重复事件行；重复分支不含单次事件行）。
/// </summary>
public class CalendarStoreTests
{
    private static CalendarTestHost NewHost() => CalendarTestHost.Create();

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0)
        => new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    /// <summary>经 Store 直接创建日历（Store 层委托 DataService 的写路径）。</summary>
    private static async Task<CalendarEntity> CreateCalendarViaStore(ICalendarStore store, string code, CancellationToken ct = default)
    {
        var entity = new CalendarEntity { Code = code, Name = code, IsEnabled = true };
        await store.CreateAsync(entity, ct);
        return entity;
    }

    /// <summary>经 Store 直接创建事件行（含重复规则——规则/RecurrenceEndUtc 手动填充，Store 不推导）。</summary>
    private static async Task<CalendarEventEntity> CreateEventViaStore(
        ICalendarStore store, long calendarId, string title, DateTime startUtc, DateTime? endUtc,
        string? recurrenceRule = null, DateTime? recurrenceEndUtc = null, CancellationToken ct = default)
    {
        var entity = new CalendarEventEntity
        {
            CalendarId = calendarId,
            Title = title,
            StartUtc = startUtc,
            EndUtc = endUtc,
            AllDay = false,
            RecurrenceRule = recurrenceRule,
            RecurrenceEndUtc = recurrenceEndUtc
        };
        await store.CreateEventAsync(entity, ct);
        return entity;
    }

    [Fact]
    public async Task StoreCreate_DelegatesToDataService_AndDataServiceSeesIt()
    {
        using var host = NewHost();

        // 经 Store 创建日历
        var cal = await CreateCalendarViaStore(host.Store, "CAL");
        Assert.True(cal.Id > 0);

        // 经 Store 创建事件（Store 委托 DataService 写路径，不触碰 ORM/IEntityDAC）
        var evt = await CreateEventViaStore(host.Store, cal.Id, "E1", Utc(2026, 9, 9, 9), Utc(2026, 9, 9, 10));

        // 经 DataService 直查能读到（委托转发正确）
        var viaCalendarDs = await host.CalendarDataService.EntityGetAsync(c => c.Id == cal.Id, CancellationToken.None);
        Assert.NotNull(viaCalendarDs);
        Assert.Equal("CAL", viaCalendarDs!.Code);

        var viaEventDs = await host.EventDataService.EntityGetAsync(e => e.Id == evt.Id, CancellationToken.None);
        Assert.NotNull(viaEventDs);
        Assert.Equal("E1", viaEventDs!.Title);
        Assert.Equal(cal.Id, viaEventDs.CalendarId);
    }

    [Fact]
    public async Task StoreCreateCalendar_GetByCode_AndGetAll_Forward()
    {
        using var host = NewHost();
        await CreateCalendarViaStore(host.Store, "A");
        await CreateCalendarViaStore(host.Store, "B");

        var byCode = await host.Store.GetByCodeAsync("B", CancellationToken.None);
        Assert.NotNull(byCode);
        Assert.Equal("B", byCode!.Code);

        var all = await host.Store.GetAllAsync(CancellationToken.None);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, e => e.Code == "A");
        Assert.Contains(all, e => e.Code == "B");
    }

    [Fact]
    public async Task GetSingleByRangeAsync_ExcludesRecurringRows()
    {
        using var host = NewHost();
        var cal = await CreateCalendarViaStore(host.Store, "C");
        // 单次 + 重复事件混建（经 Manager 走真实 RecurrenceEndUtc 推导；Store/DataService 谓词按 RecurrenceRule 分路）
        var single = await host.Manager.CreateEventAsync(cal.Id, "单次", Utc(2026, 9, 9, 9), Utc(2026, 9, 9, 10), ct: CancellationToken.None);
        await host.Manager.CreateEventAsync(cal.Id, "重复", Utc(2026, 9, 9, 9), Utc(2026, 9, 9, 10),
            recurrenceRule: "FREQ=DAILY;COUNT=3", ct: CancellationToken.None);

        // C1 分路谓词①：单次分支（RecurrenceRule == null）——不得混入重复事件行
        var singles = await host.EventDataService.GetSingleByRangeAsync(
            cal.Id, Utc(2026, 9, 1, 0), Utc(2026, 12, 31, 0), 0, 100, CancellationToken.None);

        var singleRow = Assert.Single(singles);
        Assert.Equal(single.Id, singleRow.Id);
        Assert.Null(singleRow.RecurrenceRule);
    }

    [Fact]
    public async Task GetRecurringByRangeAsync_ExcludesSingleRows()
    {
        using var host = NewHost();
        var cal = await CreateCalendarViaStore(host.Store, "C");
        var recurring = await host.Manager.CreateEventAsync(cal.Id, "重复", Utc(2026, 9, 9, 9), Utc(2026, 9, 9, 10),
            recurrenceRule: "FREQ=DAILY;COUNT=3", ct: CancellationToken.None);
        await host.Manager.CreateEventAsync(cal.Id, "单次", Utc(2026, 9, 9, 9), Utc(2026, 9, 9, 10), ct: CancellationToken.None);

        // C1 分路谓词②：重复分支（RecurrenceRule != null，按 RecurrenceEndUtc 预筛）——不得混入单次事件行
        var recurringRows = await host.EventDataService.GetRecurringByRangeAsync(
            cal.Id, Utc(2026, 9, 1, 0), Utc(2026, 12, 31, 0), 0, 100, CancellationToken.None);

        var recurringRow = Assert.Single(recurringRows);
        Assert.Equal(recurring.Id, recurringRow.Id);
        Assert.NotNull(recurringRow.RecurrenceRule);
    }

    [Fact]
    public async Task CountByCalendarIdAsync_CountsEventRows()
    {
        using var host = NewHost();
        var cal1 = await CreateCalendarViaStore(host.Store, "A");
        var cal2 = await CreateCalendarViaStore(host.Store, "B");
        await CreateEventViaStore(host.Store, cal1.Id, "E1", Utc(2026, 9, 9, 9), null);
        await CreateEventViaStore(host.Store, cal1.Id, "E2", Utc(2026, 9, 10, 9), null);
        await CreateEventViaStore(host.Store, cal2.Id, "E3", Utc(2026, 9, 11, 9), null);

        Assert.Equal(2, await host.Store.CountByCalendarIdAsync(cal1.Id, CancellationToken.None));
        Assert.Equal(1, await host.Store.CountByCalendarIdAsync(cal2.Id, CancellationToken.None));
        Assert.Equal(0, await host.Store.CountByCalendarIdAsync(9999, CancellationToken.None));
    }
}
