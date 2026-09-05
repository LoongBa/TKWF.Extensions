namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// TimeBucketAggregateCalculator 测试——桶截断、分组聚合、Dimensions。
/// </summary>
public class TimeBucketAggregateCalculatorTests
{
    private readonly TimeBucketAggregateCalculator _calculator = new();

    private static object? Accessor(object source, string fieldName)
        => source?.GetType().GetProperty(fieldName)?.GetValue(source);

    private IReadOnlyList<MetricResult> Calculate(IReadOnlyList<TestPaymentRow> rows,
        string bucket, string aggregate = "sum", string? groupField = null)
    {
        var parameters = new List<(string, string)>
        {
            ("timeField", "BizDate"),
            ("bucket", bucket),
            ("valueField", "TotalAmount"),
            ("aggregate", aggregate),
        };
        if (groupField is not null)
            parameters.Add(("groupField", groupField));
        var definition = TestHelper.Def("time-bucket", "time-bucket", parameters.ToArray());
        var result = _calculator.Calculate(rows.Select(r => new MetricRow(r, Accessor)).ToList(), definition);
        return result.Value as IReadOnlyList<MetricSlice> is { } slices
            ? slices.Select(s => new MetricResult(result.Name, s.Value, null, s.Dimensions)).ToList()
            : new List<MetricResult> { result };
    }

    [Fact]
    public void Calculate_DayBucket_SumAggregatesPerDay()
    {
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 150),
            TestPaymentRow.Order(3, new DateTime(2026, 8, 2), 50),
        };

        var results = Calculate(rows, "day");

        Assert.Equal(2, results.Count);
        Assert.Equal(250m, Assert.IsType<decimal>(results[0].Value));
        Assert.Equal("2026-08-01", results[0].Dimensions!["bucket"]);
        Assert.Equal(50m, Assert.IsType<decimal>(results[1].Value));
    }

    [Fact]
    public void Calculate_WeekBucket_IsoMondayGroupsSameWeek()
    {
        // 2026-01-05 为周一；1/5 与 1/7 同周，1/12 为下一周周一
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 1, 5), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 1, 7), 50),
            TestPaymentRow.Order(3, new DateTime(2026, 1, 12), 30),
        };

        var results = Calculate(rows, "week");

        Assert.Equal(2, results.Count);
        Assert.Equal(150m, Assert.IsType<decimal>(results[0].Value));
        Assert.Equal(30m, Assert.IsType<decimal>(results[1].Value));
    }

    [Fact]
    public void Calculate_MonthBucket_AveragePerMonth()
    {
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 15), 200),
            TestPaymentRow.Order(3, new DateTime(2026, 9, 1), 300),
        };

        var results = Calculate(rows, "month", aggregate: "avg");

        Assert.Equal(2, results.Count);
        Assert.Equal(150m, Assert.IsType<decimal>(results[0].Value));
        Assert.Equal("2026-08", results[0].Dimensions!["bucket"]);
        Assert.Equal(300m, Assert.IsType<decimal>(results[1].Value));
    }

    [Fact]
    public void Calculate_HourBucket_CountAggregates()
    {
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1, 10, 15, 0), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1, 10, 45, 0), 100),
            TestPaymentRow.Order(3, new DateTime(2026, 8, 1, 11, 5, 0), 100),
        };

        var results = Calculate(rows, "hour", aggregate: "count");

        Assert.Equal(2, results.Count);
        Assert.Equal(2, Assert.IsType<int>(results[0].Value));
        Assert.Equal("2026-08-01 10:00", results[0].Dimensions!["bucket"]);
        Assert.Equal(1, Assert.IsType<int>(results[1].Value));
    }

    [Fact]
    public void Calculate_GroupField_SplitsByGroup()
    {
        var rows = new List<TestPaymentRow>
        {
            new() { BizDate = new DateTime(2026, 8, 1), TotalAmount = 100, Category = "A" },
            new() { BizDate = new DateTime(2026, 8, 1), TotalAmount = 50, Category = "B" },
        };

        var results = Calculate(rows, "day", groupField: "Category");

        Assert.Equal(2, results.Count);
        Assert.Equal(100m, Assert.IsType<decimal>(results[0].Value));
        Assert.Equal("A", results[0].Dimensions!["Category"]);
        Assert.Equal(50m, Assert.IsType<decimal>(results[1].Value));
    }

    [Fact]
    public void Calculate_AvgAllNullValuesInBucket_ReturnsNullSlice()
    {
        // Issue#5：某桶时间有效但 valueField 全 null → Count=0 → avg 返回 null
        var rows = new List<TestPaymentRow>
        {
            new() { BizDate = new DateTime(2026, 8, 1), TotalAmount = null },
            new() { BizDate = new DateTime(2026, 8, 2), TotalAmount = 100 },
        };

        var results = Calculate(rows, "day", aggregate: "avg");

        Assert.Equal(2, results.Count);
        Assert.Null(results[0].Value);           // 8/1 桶全 null → null
        Assert.Equal(100m, Assert.IsType<decimal>(results[1].Value)); // 8/2 → 100
    }

    [Fact]
    public void Calculate_InvalidBucket_Throws()
    {
        var rows = new List<TestPaymentRow> { TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100) };

        Assert.Throws<MetricDefinitionException>(() => Calculate(rows, "year"));
    }
}
