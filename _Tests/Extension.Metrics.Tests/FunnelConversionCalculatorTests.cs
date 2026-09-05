namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// FunnelConversionCalculator 测试——有序子序列匹配、乱序/跳步、转化率。
/// </summary>
public class FunnelConversionCalculatorTests
{
    private readonly FunnelConversionCalculator _calculator = new();

    private static object? Accessor(object source, string fieldName)
        => source?.GetType().GetProperty(fieldName)?.GetValue(source);

    private IReadOnlyList<MetricResult> Calculate(IReadOnlyList<TestPaymentRow> rows,
        string steps = "view,cart,pay", bool withTimeField = false)
    {
        var parameters = new List<(string, string)>
        {
            ("userIdField", "MemberId"),
            ("stepField", "Stage"),
            ("steps", steps),
        };
        if (withTimeField)
            parameters.Add(("timeField", "BizDate"));
        var definition = TestHelper.Def("funnel", "funnel", parameters.ToArray());
        var result = _calculator.Calculate(rows.Select(r => new MetricRow(r, Accessor)).ToList(), definition);
        return result.Value as IReadOnlyList<MetricSlice> is { } slices
            ? slices.Select(s => new MetricResult(result.Name, s.Value, null, s.Dimensions)).ToList()
            : new List<MetricResult> { result };
    }

    private static TestPaymentRow Event(long memberId, string stage, DateTime? time = null)
        => new() { MemberId = memberId, Stage = stage, BizDate = time };

    [Fact]
    public void Calculate_OrderedSequence_CompletesAllSteps()
    {
        var rows = new List<TestPaymentRow>
        {
            Event(1, "view"), Event(1, "cart"), Event(1, "pay"),
        };

        var results = Calculate(rows);

        Assert.Equal(3, results.Count);
        Assert.Equal(1.0, Assert.IsType<double>(results[0].Value), 6); // view
        Assert.Equal(1.0, Assert.IsType<double>(results[1].Value), 6); // cart
        Assert.Equal(1.0, Assert.IsType<double>(results[2].Value), 6); // pay
    }

    [Fact]
    public void Calculate_OutOfOrder_OnlyCompletesFirstStep()
    {
        // cart → view → pay：view 之后无 cart → 仅完成 steps[0]
        var rows = new List<TestPaymentRow>
        {
            Event(1, "cart"), Event(1, "view"), Event(1, "pay"),
        };

        var results = Calculate(rows);

        Assert.Equal(1.0, Assert.IsType<double>(results[0].Value), 6);
        Assert.Equal(0.0, Assert.IsType<double>(results[1].Value), 6);
        Assert.Equal(0.0, Assert.IsType<double>(results[2].Value), 6);
    }

    [Fact]
    public void Calculate_SkippedStep_NotCountedAsCompleted()
    {
        // view → pay（无 cart）→ 仅完成 steps[0]
        var rows = new List<TestPaymentRow>
        {
            Event(1, "view"), Event(1, "pay"),
        };

        var results = Calculate(rows);

        Assert.Equal(1.0, Assert.IsType<double>(results[0].Value), 6);
        Assert.Equal(0.0, Assert.IsType<double>(results[1].Value), 6);
    }

    [Fact]
    public void Calculate_MultipleUsers_ComputesCumulativeRates()
    {
        // 10 进 view，6 完成 cart，4 完成 pay
        var rows = new List<TestPaymentRow>();
        for (var i = 1; i <= 10; i++)
        {
            rows.Add(Event(i, "view"));
            if (i <= 6)
                rows.Add(Event(i, "cart"));
            if (i <= 4)
                rows.Add(Event(i, "pay"));
        }

        var results = Calculate(rows);

        Assert.Equal(1.0, Assert.IsType<double>(results[0].Value), 6);
        Assert.Equal(0.6, Assert.IsType<double>(results[1].Value), 6);
        Assert.Equal(0.4, Assert.IsType<double>(results[2].Value), 6);
    }

    [Fact]
    public void Calculate_NoOneEntered_AllSlicesNull()
    {
        // 事件均为非漏斗步骤（不进入 steps[0]）
        var rows = new List<TestPaymentRow> { Event(1, "browse") };

        var results = Calculate(rows);

        Assert.Equal(3, results.Count);
        Assert.All(results, r => Assert.Null(r.Value));
    }

    [Fact]
    public void Calculate_WithTimeField_SortsByTimeBeforeMatching()
    {
        // C-1：行序 pay→view→cart，但时间序 view(09:00)→cart(10:00)→pay(11:00)——时间排序后匹配全部 3 步
        var rows = new List<TestPaymentRow>
        {
            Event(1, "pay", new DateTime(2026, 8, 1, 11, 0, 0)),
            Event(1, "view", new DateTime(2026, 8, 1, 9, 0, 0)),
            Event(1, "cart", new DateTime(2026, 8, 1, 10, 0, 0)),
        };

        var results = Calculate(rows, withTimeField: true);

        Assert.Equal(1.0, Assert.IsType<double>(results[0].Value), 6); // view
        Assert.Equal(1.0, Assert.IsType<double>(results[1].Value), 6); // cart
        Assert.Equal(1.0, Assert.IsType<double>(results[2].Value), 6); // pay
    }

    [Fact]
    public void Calculate_EmptySteps_Throws()
    {
        Assert.Throws<MetricDefinitionException>(() => Calculate([], steps: ""));
    }
}
