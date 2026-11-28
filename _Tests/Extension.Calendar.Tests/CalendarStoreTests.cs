using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.Calendar;

namespace TKWF.Ext.Calendar.Tests;

/// <summary>
/// Calendar DataService 委托正确性测试（A5 Store-debt cleanup 后）——经 DataService 写入 → 可读
/// + 分路谓词正确性（C1：单次分支不含重复事件行；重复分支不含单次事件行）。
/// </summary>
public class CalendarStoreTests
{
    private static CalendarTestHost NewHost() => CalendarTestHost.Create();

    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0)
        => new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    private static async Task<CalendarEntity> CreateCalendarViaDataService(CalendarTestHost host, string code, CancellationToken ct = default)
    {
        var entity = new CalendarEntity { Code = code, Name = code, IsEnabled = true };
        await host.CalendarDataService.EntityCreateAsync(entity, ct);
        return entity;
    }

    private static async Task<CalendarEventEntity> CreateEventViaDataService(
        CalendarTestHost host, long calendarId, string title, DateTime startUtc, DateTime? endUtc,
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
        await host.EventDataService.CreateAsync(entity, ct);
        return entity;
    }

    [Fact]
    public async Task StoreCreate_DelegatesToDataService_AndDataServiceSeesIt()
    {
        using var host = NewHost();

        var cal = await CreateCalendarViaDataService(host, "CAL", ct: TestContext.Current.CancellationToken);
        Assert.True(cal.Id > 0);

        var evt = await CreateEventViaDataService(host, cal.Id, "E1", Utc(2026, 9, 9, 9), Utc(2026, 9, 9, 10), ct: TestContext.Current.CancellationToken);

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
        await CreateCalendarViaDataService(host, "A", ct: TestContext.Current.CancellationToken);
        await CreateCalendarViaDataService(host, "B", ct: TestContext.Current.CancellationToken);

        var byCode = await host.CalendarDataService.GetByCodeAsync("B", CancellationToken.None);
        Assert.NotNull(byCode);
        Assert.Equal("B", byCode!.Code);

        var all = await host.CalendarDataService.GetAllAsync(CancellationToken.None);
        Assert.Equal(2, all.Count);
        Assert.Contains(all, e => e.Code == "A");
        Assert.Contains(all, e => e.Code == "B");
    }

    [Fact]
    public async Task GetSingleByRangeAsync_ExcludesRecurringRows()
    {
        using var host = NewHost();
        var cal = await CreateCalendarViaDataService(host, "C", ct: TestContext.Current.CancellationToken);
        var single = await host.Manager.CreateEventAsync(cal.Id, "单次", Utc(2026, 9, 9, 9), Utc(2026, 9, 9, 10), ct: CancellationToken.None);
        await host.Manager.CreateEventAsync(cal.Id, "重复", Utc(2026, 9, 9, 9), Utc(2026, 9, 9, 10),
            recurrenceRule: "FREQ=DAILY;COUNT=3", ct: CancellationToken.None);

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
        var cal = await CreateCalendarViaDataService(host, "C", ct: TestContext.Current.CancellationToken);
        var recurring = await host.Manager.CreateEventAsync(cal.Id, "重复", Utc(2026, 9, 9, 9), Utc(2026, 9, 9, 10),
            recurrenceRule: "FREQ=DAILY;COUNT=3", ct: CancellationToken.None);
        await host.Manager.CreateEventAsync(cal.Id, "单次", Utc(2026, 9, 9, 9), Utc(2026, 9, 9, 10), ct: CancellationToken.None);

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
        var cal1 = await CreateCalendarViaDataService(host, "A", ct: TestContext.Current.CancellationToken);
        var cal2 = await CreateCalendarViaDataService(host, "B", ct: TestContext.Current.CancellationToken);
        await CreateEventViaDataService(host, cal1.Id, "E1", Utc(2026, 9, 9, 9), null, ct: TestContext.Current.CancellationToken);
        await CreateEventViaDataService(host, cal1.Id, "E2", Utc(2026, 9, 10, 9), null, ct: TestContext.Current.CancellationToken);
        await CreateEventViaDataService(host, cal2.Id, "E3", Utc(2026, 9, 11, 9), null, ct: TestContext.Current.CancellationToken);

        Assert.Equal(2, await host.EventDataService.CountByCalendarIdAsync(cal1.Id, CancellationToken.None));
        Assert.Equal(1, await host.EventDataService.CountByCalendarIdAsync(cal2.Id, CancellationToken.None));
        Assert.Equal(0, await host.EventDataService.CountByCalendarIdAsync(9999, CancellationToken.None));
    }
}
