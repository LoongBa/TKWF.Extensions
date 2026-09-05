namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// RepurchaseRateCalculator 测试——全量/窗口复购率、边界。
/// </summary>
public class RepurchaseRateCalculatorTests
{
    private readonly RepurchaseRateCalculator _calculator = new();

    private MetricResult Calculate(IReadOnlyList<TestPaymentRow> rows,
        int? windowDays = null, string? orderTimeField = "BizDate")
    {
        var parameters = new List<(string, string)> { ("userIdField", "MemberId") };
        if (orderTimeField is { } timeField)
            parameters.Add(("orderTimeField", timeField));
        if (windowDays is not null)
            parameters.Add(("windowDays", windowDays.ToString()));
        var definition = TestHelper.Def("repurchase-rate", "repurchase-rate", parameters.ToArray());
        return _calculator.Calculate(rows.Select(r => new MetricRow(r, Accessor)).ToList(), definition);
    }

    private static object? Accessor(object source, string fieldName)
        => source?.GetType().GetProperty(fieldName)?.GetValue(source);

    [Fact]
    public void Calculate_DefaultFullScope_ReturnsRepeatUsersOverTotalUsers()
    {
        // 5 用户：3 个 1 单、2 个 2 单 → 2/5 = 0.4
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(3, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(4, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(4, new DateTime(2026, 8, 10), 100),
            TestPaymentRow.Order(5, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(5, new DateTime(2026, 8, 20), 100),
        };

        var result = Calculate(rows);

        Assert.Equal(0.4, Assert.IsType<double>(result.Value), 6);
    }

    [Fact]
    public void Calculate_WindowDays_RepurchaseWithinWindow_Counts()
    {
        // A 首购 8/1、复购 8/10（9 天内）；B 仅 1 单 → windowDays=30 → 1/2 = 0.5
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(1, new DateTime(2026, 8, 10), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 100),
        };

        var result = Calculate(rows, windowDays: 30);

        Assert.Equal(0.5, Assert.IsType<double>(result.Value), 6);
    }

    [Fact]
    public void Calculate_WindowDays_RepurchaseOutsideWindow_NotCounts()
    {
        // A 首购 8/1、复购 10/1（61 天后）→ windowDays=30 → 0
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(1, new DateTime(2026, 10, 1), 100),
        };

        var result = Calculate(rows, windowDays: 30);

        Assert.Equal(0.0, Assert.IsType<double>(result.Value), 6);
    }

    [Fact]
    public void Calculate_NoRepurchase_ReturnsZero()
    {
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(3, new DateTime(2026, 8, 1), 100),
        };

        var result = Calculate(rows);

        Assert.Equal(0.0, Assert.IsType<double>(result.Value), 6);
    }

    [Fact]
    public void Calculate_WindowDaysWithoutOrderTimeField_Throws()
    {
        // Issue#4：windowDays ≥ 0 但缺 orderTimeField → 抛 MetricDefinitionException
        var definition = TestHelper.Def("repurchase-rate", "repurchase-rate",
            ("userIdField", "MemberId"), ("windowDays", "30"));
        var rows = new List<TestPaymentRow> { TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100) };

        Assert.Throws<MetricDefinitionException>(() =>
            _calculator.Calculate(rows.Select(r => new MetricRow(r, Accessor)).ToList(), definition));
    }

    [Fact]
    public void Calculate_AllRepurchase_ReturnsOne()
    {
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(1, new DateTime(2026, 8, 5), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 9), 100),
        };

        var result = Calculate(rows);

        Assert.Equal(1.0, Assert.IsType<double>(result.Value), 6);
    }
}
