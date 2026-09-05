namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// CohortRetentionCalculator 测试——分群、偏移交集、矩阵 Dimensions。
/// </summary>
public class CohortRetentionCalculatorTests
{
    private readonly CohortRetentionCalculator _calculator = new();

    private static object? Accessor(object source, string fieldName)
        => source?.GetType().GetProperty(fieldName)?.GetValue(source);

    private IReadOnlyList<MetricResult> Calculate(IReadOnlyList<TestPaymentRow> rows,
        string cohortUnit = "day", string offsets = "0,30")
    {
        var definition = TestHelper.Def("cohort-retention", "cohort-retention",
            ("userIdField", "MemberId"), ("dateField", "BizDate"),
            ("cohortUnit", cohortUnit), ("retentionOffsets", offsets));
        var result = _calculator.Calculate(rows.Select(r => new MetricRow(r, Accessor)).ToList(), definition);
        // 多切片展开（对齐引擎行为）
        return result.Value as IReadOnlyList<MetricSlice> is { } slices
            ? slices.Select(s => new MetricResult(result.Name, s.Value, null, s.Dimensions)).ToList()
            : new List<MetricResult> { result };
    }

    [Fact]
    public void Calculate_DayCohort_GroupsByFirstSeenDay()
    {
        // A/B 首见 8/1（B 8/5 为后续活跃）；C 首见 9/1 → 2 个 cohort
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 5), 100),
            TestPaymentRow.Order(3, new DateTime(2026, 9, 1), 100),
        };

        var results = Calculate(rows, offsets: "0");

        var cohorts = results
            .Select(r => r.Dimensions!["cohort"]!.ToString()!)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(2, cohorts.Count);
        Assert.Equal(new List<string> { "2026-08-01", "2026-09-01" }, cohorts);
    }

    [Fact]
    public void Calculate_OffsetIntersection_ReturnsRetainedOverCohort()
    {
        // cohort 8/1：A、B；offset 30 → target 8/31：A 活跃、B 不活跃 → 1/2 = 0.5
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(1, new DateTime(2026, 8, 31), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 100),
        };

        var results = Calculate(rows, offsets: "30");

        var slice = Assert.Single(results);
        Assert.Equal(0.5, Assert.IsType<double>(slice.Value), 6);
    }

    [Fact]
    public void Calculate_Slices_CarryCohortAndOffsetDimensions()
    {
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
        };

        var results = Calculate(rows, offsets: "0,30");

        Assert.Equal(2, results.Count);
        Assert.Equal("2026-08-01", results[0].Dimensions!["cohort"]);
        Assert.Equal(0, results[0].Dimensions!["offset"]);
        Assert.Equal(30, results[1].Dimensions!["offset"]);
    }

    [Fact]
    public void Calculate_EmptyData_ReturnsEmptySlices()
    {
        var results = Calculate([], offsets: "0");

        Assert.Empty(results);
    }

    [Fact]
    public void Calculate_MonthCohort_GroupsByFirstSeenMonth()
    {
        // A 首见 8/1、B 首见 8/20 → 同 8 月 cohort；C 首见 9/1 → 独立
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 20), 100),
            TestPaymentRow.Order(3, new DateTime(2026, 9, 1), 100),
        };

        var results = Calculate(rows, cohortUnit: "month", offsets: "0");

        var cohorts = results
            .Select(r => r.Dimensions!["cohort"]!.ToString()!)
            .Distinct()
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        Assert.Equal(new List<string> { "2026-08", "2026-09" }, cohorts);
    }

    [Fact]
    public void Calculate_WeekCohort_IsoWeekGroups()
    {
        // 2026-01-05 为周一：A 首见 1/5、B 首见 1/7 → 同 ISO 周；C 首见 1/12（下周一）→ 独立
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 1, 5), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 1, 7), 100),
            TestPaymentRow.Order(3, new DateTime(2026, 1, 12), 100),
        };

        var results = Calculate(rows, cohortUnit: "week", offsets: "0");

        Assert.Equal(2, results.Count);
    }

    [Fact]
    public void Calculate_InvalidOffset_Throws()
    {
        var rows = new List<TestPaymentRow> { TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100) };
        var definition = TestHelper.Def("cohort-retention", "cohort-retention",
            ("userIdField", "MemberId"), ("dateField", "BizDate"),
            ("cohortUnit", "day"), ("retentionOffsets", "abc"));

        Assert.Throws<MetricDefinitionException>(() =>
            _calculator.Calculate(rows.Select(r => new MetricRow(r, Accessor)).ToList(), definition));
    }
}
