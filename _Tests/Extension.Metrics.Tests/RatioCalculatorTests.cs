namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// RatioCalculator 测试——两聚合之比、分母零、行为开关。
/// </summary>
public class RatioCalculatorTests
{
    private readonly RatioCalculator _calculator = new();

    private static object? Accessor(object source, string fieldName)
        => source?.GetType().GetProperty(fieldName)?.GetValue(source);

    private MetricResult Calculate(IReadOnlyList<TestPaymentRow> rows)
    {
        var definition = TestHelper.Def("aov-cny", "ratio",
            ("numeratorField", "TotalAmount"), ("numeratorAggregate", "sum"),
            ("denominatorField", "PaidCount"), ("denominatorAggregate", "count"));
        return _calculator.Calculate(rows.Select(r => new MetricRow(r, Accessor)).ToList(), definition);
    }

    [Fact]
    public void Calculate_Aov_ReturnsSumOverCount()
    {
        // sum(TotalAmount)=300, count(PaidCount)=3 → 100
        var rows = new List<TestPaymentRow>
        {
            TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
            TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 150),
            TestPaymentRow.Order(3, new DateTime(2026, 8, 1), 50),
        };

        var result = Calculate(rows);

        Assert.Equal(100m, Assert.IsType<decimal>(result.Value));
    }

    [Fact]
    public void Calculate_ZeroDenominator_ReturnsNull()
    {
        // 分母字段全 null → count=0 → Value null
        var rows = new List<TestPaymentRow>
        {
            new() { TotalAmount = 100, PaidCount = null },
        };

        var result = Calculate(rows);

        Assert.Null(result.Value);
    }

    [Fact]
    public void Calculate_InvalidAggregate_Throws()
    {
        var definition = TestHelper.Def("bad", "ratio",
            ("numeratorField", "TotalAmount"), ("numeratorAggregate", "median"),
            ("denominatorField", "PaidCount"), ("denominatorAggregate", "count"));
        var rows = new List<TestPaymentRow> { TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100) };

        Assert.Throws<MetricDefinitionException>(() =>
            _calculator.Calculate(rows.Select(r => new MetricRow(r, Accessor)).ToList(), definition));
    }

    [Fact]
    public async Task Calculate_EngineMissingField_Throw_Throws()
    {
        // 字段 PaidCount 不存在于类型 → MissingFieldBehavior.Throw
        var definition = TestHelper.Def("aov-cny", "ratio",
            ("numeratorField", "TotalAmount"), ("numeratorAggregate", "sum"),
            ("denominatorField", "NoSuchField"), ("denominatorAggregate", "count"));
        var engine = TestHelper.Engine();
        var rows = new List<TestPaymentRow> { TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100) };

        await Assert.ThrowsAsync<MetricDefinitionException>(() =>
            engine.CalculateAsync(rows, new[] { definition }, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Calculate_EngineMissingField_Null_ReturnsNull()
    {
        // 字段缺失 → MissingFieldBehavior.Null → 分母 count=0 → Value null
        var definition = TestHelper.Def("aov-cny", "ratio",
            ("numeratorField", "TotalAmount"), ("numeratorAggregate", "sum"),
            ("denominatorField", "NoSuchField"), ("denominatorAggregate", "count"));
        var engine = TestHelper.Engine(field: MissingFieldBehavior.Null);
        var rows = new List<TestPaymentRow> { TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100) };

        var results = await engine.CalculateAsync(rows, new[] { definition }, TestContext.Current.CancellationToken);

        var result = Assert.Single(results);
        Assert.Null(result.Value);
    }
}
