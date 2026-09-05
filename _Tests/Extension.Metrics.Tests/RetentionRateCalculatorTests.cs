namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// RetentionRateCalculator 测试——锚定首日单周期留存、边界。
/// </summary>
public class RetentionRateCalculatorTests
{
    private readonly RetentionRateCalculator _calculator = new();

    private static object? Accessor(object source, string fieldName)
        => source?.GetType().GetProperty(fieldName)?.GetValue(source);

    private MetricResult Calculate(IReadOnlyList<TestPaymentRow> rows, string retentionDays)
    {
        var definition = TestHelper.Def("retention", "retention",
            ("userIdField", "MemberId"), ("dateField", "BizDate"), ("retentionDays", retentionDays));
        return _calculator.Calculate(rows.Select(r => new MetricRow(r, Accessor)).ToList(), definition);
    }

    [Fact]
    public void Calculate_SinglePeriod_ReturnsRetainedOverInitial()
    {
        // 首日 8/1：3 用户；8/31（D+30）：其中 2 活跃 → 2/3
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(3, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(1, new DateTime(2026, 8, 31), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 31), 100),
        };

        var result = Calculate(rows, "30");

        Assert.Equal(2.0 / 3.0, Assert.IsType<double>(result.Value), 6);
    }

    [Fact]
    public void Calculate_NoRetention_ReturnsZero()
    {
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 100),
        };

        var result = Calculate(rows, "30");

        Assert.Equal(0.0, Assert.IsType<double>(result.Value), 6);
    }

    [Fact]
    public void Calculate_AllRetained_ReturnsOne()
    {
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(1, new DateTime(2026, 8, 31), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 31), 100),
        };

        var result = Calculate(rows, "30");

        Assert.Equal(1.0, Assert.IsType<double>(result.Value), 6);
    }

    [Fact]
    public void Calculate_ZeroDays_ReturnsOne()
    {
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 100),
        };

        var result = Calculate(rows, "0");

        Assert.Equal(1.0, Assert.IsType<double>(result.Value), 6);
    }

    [Fact]
    public void Calculate_InvalidRetentionDays_Throws()
    {
        var rows = new List<TestPaymentRow> { TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100) };

        Assert.Throws<MetricDefinitionException>(() => Calculate(rows, "abc"));
    }
}
