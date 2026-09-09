using System;
using System.Collections.Generic;
using System.Linq;
using TKW.Framework.Utility.Calendar.Recurrence;
using Xunit;

namespace TKWF.Ext.Calendar.Tests;

/// <summary>
/// RecurrenceExpander / RecurrenceRule 纯算法测试——只依赖 TKW.Framework.Utility，不依赖扩展主体。
/// 覆盖：DAILY/WEEKLY/MONTHLY/YEARLY 展开、绝对索引（无漂移）、月末钳制、COUNT/UNTIL 边界、
/// 半开区间、BYDAY(Weekly)、绝对定位首发生、maxCount 截断区分、Parse fail-fast 全矩阵、溢出防护。
/// </summary>
public class RecurrenceExpanderTests
{
    // ---------- 工具 ----------

    private static DateTime Utc(int y, int mo, int d, int h = 0, int mi = 0, int s = 0)
        => new(y, mo, d, h, mi, s, DateTimeKind.Utc);

    private static void AssertRule(RecurrenceRule actual,
        RecurrenceFrequency freq, int interval, int? count, DateTime? until, params DayOfWeek[]? byDay)
    {
        Assert.Equal(freq, actual.Frequency);
        Assert.Equal(interval, actual.Interval);
        Assert.Equal(count, actual.Count);
        Assert.Equal(until, actual.UntilUtc);
        if (byDay is null || byDay.Length == 0)
            Assert.Null(actual.ByDay);   // 无 BYDAY → null（契约可空；params 不传参 = 空数组）
        else
            Assert.Equal(byDay, actual.ByDay);
    }

    private static OccurrenceExpansionResult Expand(string rule, DateTime dtStart, DateTime from, DateTime to, int maxCount = 1000)
        => RecurrenceExpander.GetOccurrences(RecurrenceRule.Parse(rule), dtStart, from, to, maxCount);

    // ---------- DAILY ----------

    [Fact]
    public void Daily_Interval1_EveryDay()
    {
        var r = Expand("FREQ=DAILY", Utc(2024, 1, 1, 9, 0), Utc(2024, 1, 1), Utc(2024, 1, 5));
        Assert.Equal(new[]
        {
            Utc(2024, 1, 1, 9, 0), Utc(2024, 1, 2, 9, 0), Utc(2024, 1, 3, 9, 0), Utc(2024, 1, 4, 9, 0),
        }, r.Occurrences);
        Assert.False(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.Normal, r.EndReason);
    }

    [Fact]
    public void Daily_Interval2_EveryOtherDay()
    {
        var r = Expand("FREQ=DAILY;INTERVAL=2", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2024, 1, 8));
        Assert.Equal(new[]
        {
            Utc(2024, 1, 1), Utc(2024, 1, 3), Utc(2024, 1, 5), Utc(2024, 1, 7),
        }, r.Occurrences);
        Assert.Equal(OccurrenceExpansionEnd.Normal, r.EndReason);
    }

    [Fact]
    public void Daily_Count5_StopsAtCount()
    {
        var r = Expand("FREQ=DAILY;COUNT=5", Utc(2024, 1, 1, 8, 0), Utc(2024, 1, 1), Utc(2030, 1, 1));
        Assert.Equal(5, r.Occurrences.Count);
        Assert.Equal(Utc(2024, 1, 5, 8, 0), r.Occurrences[^1]);
        Assert.False(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.CountExhausted, r.EndReason);
    }

    [Fact]
    public void Daily_Until_InclusiveBoundary()
    {
        // UNTIL 恰等于 occurrence 时刻 → 计入（含边界）
        var r = Expand("FREQ=DAILY;UNTIL=20240105T000000Z", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2024, 2, 1));
        Assert.Equal(new[]
        {
            Utc(2024, 1, 1), Utc(2024, 1, 2), Utc(2024, 1, 3), Utc(2024, 1, 4), Utc(2024, 1, 5),
        }, r.Occurrences);
        Assert.Equal(OccurrenceExpansionEnd.UntilReached, r.EndReason);
        Assert.False(r.Truncated);
    }

    [Fact]
    public void Daily_UntilEqualsRangeEnd_ExcludedByHalfOpen()
    {
        // occurrence == UNTIL == rangeEnd：UNTIL 含但半开区间排 to → 不计入；rangeEnd 边界先行 → Normal
        var r = Expand("FREQ=DAILY;UNTIL=20240105T000000Z", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2024, 1, 5));
        Assert.Equal(new[]
        {
            Utc(2024, 1, 1), Utc(2024, 1, 2), Utc(2024, 1, 3), Utc(2024, 1, 4),
        }, r.Occurrences);
        Assert.Equal(OccurrenceExpansionEnd.Normal, r.EndReason);
    }

    [Fact]
    public void Daily_HalfOpenRange_OccurrenceEqualsFromIncluded_ToExcluded()
    {
        var dtStart = Utc(2024, 3, 1);
        var r = Expand("FREQ=DAILY", dtStart, Utc(2024, 3, 1), Utc(2024, 3, 3));
        Assert.Equal(new[] { Utc(2024, 3, 1), Utc(2024, 3, 2) }, r.Occurrences); // ==from 含入、==to 排除
        Assert.Equal(OccurrenceExpansionEnd.Normal, r.EndReason);
    }

    [Fact]
    public void Daily_Count1_SingleOccurrence()
    {
        var r = Expand("FREQ=DAILY;COUNT=1", Utc(2024, 6, 1), Utc(2024, 6, 1), Utc(2024, 12, 31));
        Assert.Equal(new[] { Utc(2024, 6, 1) }, r.Occurrences);
        Assert.Equal(OccurrenceExpansionEnd.CountExhausted, r.EndReason);
        Assert.False(r.Truncated);
    }

    [Fact]
    public void Daily_UntilBeforeDtStart_EmptyNormal()
    {
        var r = Expand("FREQ=DAILY;UNTIL=20200101T000000Z", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2025, 1, 1));
        Assert.Empty(r.Occurrences);
        Assert.False(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.Normal, r.EndReason);
    }

    // ---------- WEEKLY ----------

    [Fact]
    public void Weekly_NoByDay_SameWeekday()
    {
        // 2024-01-01 是周一
        var r = Expand("FREQ=WEEKLY", Utc(2024, 1, 1, 10, 0), Utc(2024, 1, 1), Utc(2024, 1, 22));
        Assert.Equal(new[]
        {
            Utc(2024, 1, 1, 10, 0), Utc(2024, 1, 8, 10, 0), Utc(2024, 1, 15, 10, 0),
        }, r.Occurrences);
        Assert.Equal(OccurrenceExpansionEnd.Normal, r.EndReason);
    }

    [Fact]
    public void Weekly_ByDay_MultipleDays()
    {
        var r = Expand("FREQ=WEEKLY;BYDAY=MO,WE,FR", Utc(2024, 1, 1, 9, 0), Utc(2024, 1, 1), Utc(2024, 1, 8));
        Assert.Equal(new[]
        {
            Utc(2024, 1, 1, 9, 0), Utc(2024, 1, 3, 9, 0), Utc(2024, 1, 5, 9, 0),
        }, r.Occurrences);
        Assert.Equal(OccurrenceExpansionEnd.Normal, r.EndReason);
    }

    [Fact]
    public void Weekly_ByDay_NotContainingDtStartWeekday_DtStartStillFirst()
    {
        // dtStart 周一不在 BYDAY(WE,FR) 中 → dtStart 本身仍计为第一 occurrence（RFC）
        var r = Expand("FREQ=WEEKLY;BYDAY=WE,FR", Utc(2024, 1, 1, 9, 0), Utc(2024, 1, 1), Utc(2024, 1, 8));
        Assert.Equal(new[]
        {
            Utc(2024, 1, 1, 9, 0), Utc(2024, 1, 3, 9, 0), Utc(2024, 1, 5, 9, 0),
        }, r.Occurrences);
    }

    [Fact]
    public void Weekly_Interval2_EveryOtherWeek()
    {
        var r = Expand("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2024, 1, 29));
        Assert.Equal(new[] { Utc(2024, 1, 1), Utc(2024, 1, 15) }, r.Occurrences); // 1/8 跨周跳过；1/29 == to 排除
        Assert.Equal(OccurrenceExpansionEnd.Normal, r.EndReason);
    }

    [Fact]
    public void Weekly_Interval2_NoByDay()
    {
        // dtStart 周二，每 2 周同日
        var r = Expand("FREQ=WEEKLY;INTERVAL=2", Utc(2024, 1, 2), Utc(2024, 1, 2), Utc(2024, 1, 30));
        Assert.Equal(new[] { Utc(2024, 1, 2), Utc(2024, 1, 16) }, r.Occurrences); // 1/30 == to 排除
    }

    [Fact]
    public void Weekly_ByDay_Count_CountsEachOccurrenceIncludingDtStart()
    {
        var r = Expand("FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=4", Utc(2024, 1, 1, 9, 0), Utc(2024, 1, 1), Utc(2024, 2, 1));
        Assert.Equal(new[]
        {
            Utc(2024, 1, 1, 9, 0), Utc(2024, 1, 3, 9, 0), Utc(2024, 1, 5, 9, 0), Utc(2024, 1, 8, 9, 0),
        }, r.Occurrences);
        Assert.Equal(OccurrenceExpansionEnd.CountExhausted, r.EndReason);
    }

    [Fact]
    public void Weekly_ByDay_Until_MidWeekStops()
    {
        var r = Expand("FREQ=WEEKLY;BYDAY=MO,WE,FR;UNTIL=20240104T000000Z", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2025, 1, 1));
        Assert.Equal(new[] { Utc(2024, 1, 1), Utc(2024, 1, 3) }, r.Occurrences); // 1/5 > UNTIL → 停
        Assert.Equal(OccurrenceExpansionEnd.UntilReached, r.EndReason);
    }

    // ── C1 回归：WEEKLY+COUNT 结果数恰 == maxCount 时不得误标截断（对齐 ExpandLinear 前探） ──

    [Fact]
    public void Weekly_Count_ResultsEqualMax_NaturalCountExhausted()
    {
        // maxCount = COUNT：展开恰好耗尽 → 非截断（此前误标 Truncated=true 导致 DeriveRecurrenceEndUtc 抛异常）
        var r = Expand("FREQ=WEEKLY;COUNT=5", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2024, 3, 1), maxCount: 5);
        Assert.Equal(5, r.Occurrences.Count);
        Assert.False(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.CountExhausted, r.EndReason);
    }

    [Fact]
    public void Weekly_ByDay_Count_ResultsEqualMax_NaturalCountExhausted()
    {
        var r = Expand("FREQ=WEEKLY;BYDAY=MO,WE,FR;COUNT=4", Utc(2024, 1, 1, 9, 0), Utc(2024, 1, 1), Utc(2024, 2, 1), maxCount: 4);
        Assert.Equal(4, r.Occurrences.Count);
        Assert.False(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.CountExhausted, r.EndReason);
    }

    [Fact]
    public void Weekly_ByDay_Until_ResultsEqualMax_NaturalUntilReached()
    {
        // 周 0（1/1,1/3,1/5）+ 周 1 首日 1/8 = 4 个恰达 maxCount；下一 occurrence 1/10 > UNTIL(1/9 23:59) → 自然耗尽非截断
        var r = Expand("FREQ=WEEKLY;BYDAY=MO,WE,FR;UNTIL=20240109T235959Z", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2025, 1, 1), maxCount: 4);
        Assert.Equal(new[] { Utc(2024, 1, 1), Utc(2024, 1, 3), Utc(2024, 1, 5), Utc(2024, 1, 8) }, r.Occurrences);
        Assert.False(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.UntilReached, r.EndReason);
    }

    [Fact]
    public void Weekly_ResultsEqualMax_NextDayBeyondRange_NaturalNormal()
    {
        // 收集满 maxCount 且下一 occurrence ≥ rangeEnd → 非截断（Normal）
        var r = Expand("FREQ=WEEKLY;BYDAY=MO,WE", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2024, 1, 5), maxCount: 2);
        Assert.Equal(2, r.Occurrences.Count);
        Assert.False(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.Normal, r.EndReason);
    }

    [Fact]
    public void Weekly_ResultsEqualMax_StillProduces_Truncated()
    {
        // 收集满 maxCount 且规则仍产出范围内 occurrence → 真截断
        var r = Expand("FREQ=WEEKLY;BYDAY=MO", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2024, 3, 1), maxCount: 2);
        Assert.Equal(2, r.Occurrences.Count);
        Assert.True(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.TruncatedByMax, r.EndReason);
    }

    // ---------- MONTHLY ----------

    [Fact]
    public void Monthly_31ToFeb28_Clamp()
    {
        // 2023 非闰年：1/31 → 2/28（钳制）
        var r = Expand("FREQ=MONTHLY", Utc(2023, 1, 31), Utc(2023, 1, 31), Utc(2023, 4, 1));
        Assert.Equal(new[] { Utc(2023, 1, 31), Utc(2023, 2, 28), Utc(2023, 3, 31) }, r.Occurrences);
    }

    [Fact]
    public void Monthly_31ToFeb29_LeapYear()
    {
        // 2024 闰年：1/31 → 2/29（不钳）
        var r = Expand("FREQ=MONTHLY", Utc(2024, 1, 31), Utc(2024, 1, 31), Utc(2024, 3, 1));
        Assert.Equal(new[] { Utc(2024, 1, 31), Utc(2024, 2, 29) }, r.Occurrences);
    }

    [Fact]
    public void Monthly_31ToApr30_Clamp()
    {
        var r = Expand("FREQ=MONTHLY", Utc(2024, 1, 31), Utc(2024, 1, 31), Utc(2024, 5, 15));
        Assert.Equal(new[]
        {
            Utc(2024, 1, 31), Utc(2024, 2, 29), Utc(2024, 3, 31), Utc(2024, 4, 30),
        }, r.Occurrences);
    }

    [Fact]
    public void Monthly_Interval2_AbsoluteIndex_NoDrift()
    {
        // 绝对索引（禁止级联累加）：1/31 + INTERVAL=2 → 3/31（而非级联漂移的 3/28）
        var r = Expand("FREQ=MONTHLY;INTERVAL=2", Utc(2024, 1, 31), Utc(2024, 1, 31), Utc(2024, 7, 1));
        Assert.Equal(new[] { Utc(2024, 1, 31), Utc(2024, 3, 31), Utc(2024, 5, 31) }, r.Occurrences);
    }

    // ---------- YEARLY ----------

    [Fact]
    public void Yearly_Feb29_ToNonLeapFeb28_Clamp()
    {
        var r = Expand("FREQ=YEARLY", Utc(2024, 2, 29), Utc(2024, 2, 29), Utc(2027, 1, 1));
        Assert.Equal(new[] { Utc(2024, 2, 29), Utc(2025, 2, 28), Utc(2026, 2, 28) }, r.Occurrences);
        Assert.Equal(OccurrenceExpansionEnd.Normal, r.EndReason);
    }

    // ---------- 绝对定位首发生（C4） ----------

    [Fact]
    public void AbsolutePositioning_Daily_Query2025From2000Anchor()
    {
        // 无限 DAILY，2000-01-01 锚定，直接查 2025 全年：~365 个，正确且不从头枚举 9000+
        var r = Expand("FREQ=DAILY", Utc(2000, 1, 1), Utc(2025, 1, 1), Utc(2026, 1, 1));
        Assert.Equal(365, r.Occurrences.Count);
        Assert.Equal(Utc(2025, 1, 1), r.Occurrences[0]);
        Assert.Equal(Utc(2025, 12, 31), r.Occurrences[^1]);
        Assert.True(r.Occurrences.All(o => o.Year == 2025));
        for (int i = 1; i < r.Occurrences.Count; i++)
            Assert.Equal(r.Occurrences[i - 1].AddDays(1), r.Occurrences[i]); // 逐日连续
        Assert.False(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.Normal, r.EndReason);
    }

    [Fact]
    public void AbsolutePositioning_Monthly_Query2025From2000Anchor()
    {
        var r = Expand("FREQ=MONTHLY", Utc(2000, 1, 15), Utc(2025, 1, 1), Utc(2026, 1, 1));
        Assert.Equal(12, r.Occurrences.Count); // 12 个月（2025）
        Assert.Equal(Utc(2025, 1, 15), r.Occurrences[0]);
        Assert.All(r.Occurrences, o => Assert.Equal(15, o.Day));
    }

    // ---------- maxCount 截断 ----------

    [Fact]
    public void MaxCount_Truncates()
    {
        var r = Expand("FREQ=DAILY", Utc(2000, 1, 1), Utc(2000, 1, 1), Utc(2030, 1, 1), maxCount: 100);
        Assert.Equal(100, r.Occurrences.Count);
        Assert.True(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.TruncatedByMax, r.EndReason);
    }

    [Fact]
    public void MaxCount_ResultsEqualMax_NaturalCountEnd_NotTruncated()
    {
        // 结果数恰 == maxCount 但 COUNT 恰好耗尽 → 非截断
        var r = Expand("FREQ=DAILY;COUNT=100", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2030, 1, 1), maxCount: 100);
        Assert.Equal(100, r.Occurrences.Count);
        Assert.False(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.CountExhausted, r.EndReason);
    }

    [Fact]
    public void MaxCount_ResultsEqualMax_TrueTruncation()
    {
        // COUNT=5000 但 maxCount=100 → 真截断
        var r = Expand("FREQ=DAILY;COUNT=5000", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2035, 1, 1), maxCount: 100);
        Assert.Equal(100, r.Occurrences.Count);
        Assert.True(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.TruncatedByMax, r.EndReason);
    }

    [Fact]
    public void MaxCount_ResultsEqualMax_UntilEndsExactly_NotTruncated()
    {
        // UNTIL 恰好末日使结果数 == maxCount；前探判定非截断（线频率）
        var r = Expand("FREQ=DAILY;UNTIL=20240409T000000Z", Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2030, 1, 1), maxCount: 100);
        Assert.Equal(100, r.Occurrences.Count);
        Assert.False(r.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.UntilReached, r.EndReason);
    }

    [Fact]
    public void MaxCount_NonPositive_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            RecurrenceExpander.GetOccurrences(RecurrenceRule.Parse("FREQ=DAILY"), Utc(2024, 1, 1), Utc(2024, 1, 1), Utc(2025, 1, 1), 0));
    }

    // ---------- 输入边界 ----------

    [Fact]
    public void RangeEnd_LessOrEqualRangeStart_EmptyNormal()
    {
        var rule = RecurrenceRule.Parse("FREQ=DAILY");
        var dtStart = Utc(2024, 1, 1);
        var equal = RecurrenceExpander.GetOccurrences(rule, dtStart, dtStart, dtStart);
        Assert.Empty(equal.Occurrences);
        Assert.False(equal.Truncated);
        Assert.Equal(OccurrenceExpansionEnd.Normal, equal.EndReason);

        var inverted = RecurrenceExpander.GetOccurrences(rule, dtStart, Utc(2025, 1, 1), Utc(2024, 1, 1));
        Assert.Empty(inverted.Occurrences);
        Assert.Equal(OccurrenceExpansionEnd.Normal, inverted.EndReason);
    }

    [Fact]
    public void Overflow_IntervalIntMax_DoesNotThrow()
    {
        // 溢出防护：极端 INTERVAL → 不裸抛，返回空 + Truncated
        var daily = Expand("FREQ=DAILY;INTERVAL=2147483647", Utc(2000, 1, 1), Utc(2025, 1, 1), Utc(2026, 1, 1));
        Assert.Empty(daily.Occurrences);
        Assert.True(daily.Truncated);

        var monthly = Expand("FREQ=MONTHLY;INTERVAL=2147483647", Utc(2000, 1, 1), Utc(2025, 1, 1), Utc(2026, 1, 1));
        Assert.Empty(monthly.Occurrences);
        Assert.True(monthly.Truncated);
    }

    // ---------- Parse fail-fast 矩阵 ----------

    [Theory]
    [InlineData("FREQ=DAILY;COUNT=3;UNTIL=20241231T000000Z")]
    [InlineData("FREQ=DAILY;UNTIL=20241231T000000Z;COUNT=3")]
    public void Parse_CountAndUntil_MutuallyExclusive_Throws(string rrule)
        => Assert.Throws<FormatException>(() => RecurrenceRule.Parse(rrule));

    [Theory]
    [InlineData("FREQ=DAILY;BYMONTH=1")]
    [InlineData("FREQ=DAILY;EXDATE=20240101T000000Z")]
    [InlineData("FREQ=DAILY;WKST=MO")]
    [InlineData("FREQ=DAILY;BYSETPOS=1")]
    [InlineData("FREQ=DAILY;BYHOUR=9")]
    public void Parse_UnknownClause_Throws(string rrule)
        => Assert.Throws<FormatException>(() => RecurrenceRule.Parse(rrule));

    [Theory]
    [InlineData("FREQ=DAILY;BYDAY=MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=1MO")]
    [InlineData("FREQ=MONTHLY;BYDAY=-1SU")]
    [InlineData("FREQ=YEARLY;BYDAY=WE")]
    [InlineData("FREQ=WEEKLY;BYDAY=1MO")] // 序数前缀即使 WEEKLY 也拒绝
    public void Parse_ByDay_NonWeeklyOrOrdinal_Throws(string rrule)
        => Assert.Throws<FormatException>(() => RecurrenceRule.Parse(rrule));

    [Theory]
    [InlineData("FREQ=DAILY;INTERVAL=0")]
    [InlineData("FREQ=DAILY;INTERVAL=-1")]
    [InlineData("FREQ=DAILY;INTERVAL=abc")]
    public void Parse_Interval_NonPositiveOrInvalid_Throws(string rrule)
        => Assert.Throws<FormatException>(() => RecurrenceRule.Parse(rrule));

    [Theory]
    [InlineData("FREQ=DAILY;COUNT=0")]
    [InlineData("FREQ=DAILY;COUNT=-5")]
    [InlineData("FREQ=DAILY;COUNT=xyz")]
    public void Parse_Count_NonPositiveOrInvalid_Throws(string rrule)
        => Assert.Throws<FormatException>(() => RecurrenceRule.Parse(rrule));

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("FREQ")]
    [InlineData("FREQ=MONTHLY2")]
    [InlineData("FREQ=DAILY;UNTIL=2024-01-01")]
    [InlineData("FREQ=DAILY;UNTIL=20240101T235959")] // 缺 Z
    [InlineData("FREQ=WEEKLY;BYDAY=MO,,FR")]
    [InlineData("FREQ=WEEKLY;BYDAY=")]
    public void Parse_InvalidFormats_Throws(string rrule)
        => Assert.Throws<FormatException>(() => RecurrenceRule.Parse(rrule));

    [Fact]
    public void Parse_MissingFrequency_Throws()
        => Assert.Throws<FormatException>(() => RecurrenceRule.Parse("INTERVAL=2"));

    // ---------- Parse 正例 ----------

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var r = RecurrenceRule.Parse("freq=daily;interval=2;count=3");
        AssertRule(r, RecurrenceFrequency.Daily, 2, 3, null);

        var weekly = RecurrenceRule.Parse("FREQ=weekly;ByDay=mo,we,fR");
        AssertRule(weekly, RecurrenceFrequency.Weekly, 1, null, null, DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday);
    }

    [Fact]
    public void Parse_DuplicateKeys_TakesLast()
    {
        var r = RecurrenceRule.Parse("FREQ=DAILY;FREQ=WEEKLY;INTERVAL=1;INTERVAL=3");
        AssertRule(r, RecurrenceFrequency.Weekly, 3, null, null);
    }

    [Fact]
    public void Parse_UntilDateOnly_IsMidnightUtc()
    {
        var r = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20241231");
        Assert.Equal(Utc(2024, 12, 31), r.UntilUtc);
        AssertRule(r, RecurrenceFrequency.Daily, 1, null, Utc(2024, 12, 31));
    }

    [Fact]
    public void Parse_ByDay_DedupePreserveOrder()
    {
        var r = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO,SU,MO,WE");
        AssertRule(r, RecurrenceFrequency.Weekly, 1, null, null, DayOfWeek.Monday, DayOfWeek.Sunday, DayOfWeek.Wednesday);
    }

    [Fact]
    public void ToString_Canonical_AndReverseLook()
    {
        var daily = RecurrenceRule.Parse("FREQ=DAILY;INTERVAL=1;COUNT=5");
        Assert.Equal("FREQ=DAILY;INTERVAL=1;COUNT=5", daily.ToString()); // INTERVAL=1 显式输出

        var weekly = RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=2;BYDAY=mo,we,fr;UNTIL=20261231t235959z");
        Assert.Equal("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR;UNTIL=20261231T235959Z", weekly.ToString());
    }

    [Theory]
    [InlineData("FREQ=DAILY")]
    [InlineData("FREQ=DAILY;INTERVAL=2;COUNT=10")]
    [InlineData("FREQ=DAILY;INTERVAL=7;UNTIL=20261231T235959Z")]
    [InlineData("FREQ=DAILY;INTERVAL=3;UNTIL=20261231")]
    [InlineData("FREQ=WEEKLY")]
    [InlineData("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR;COUNT=10")]
    [InlineData("FREQ=WEEKLY;INTERVAL=3;BYDAY=SU")]
    [InlineData("FREQ=MONTHLY;COUNT=12")]
    [InlineData("FREQ=MONTHLY;INTERVAL=2;UNTIL=20301231T000000Z")]
    [InlineData("FREQ=YEARLY;INTERVAL=1;COUNT=3")]
    public void ToString_IsInvertibleWithParse(string rrule)
    {
        var original = RecurrenceRule.Parse(rrule);
        var roundTripped = RecurrenceRule.Parse(original.ToString());

        Assert.Equal(original.Frequency, roundTripped.Frequency);
        Assert.Equal(original.Interval, roundTripped.Interval);
        Assert.Equal(original.Count, roundTripped.Count);
        Assert.Equal(original.UntilUtc, roundTripped.UntilUtc);
        if (original.ByDay is null)
            Assert.Null(roundTripped.ByDay);
        else
            Assert.Equal(original.ByDay, roundTripped.ByDay);
    }
}