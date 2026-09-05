namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// MetricsEngine 测试——主流程、失败行为、多切片展开、取消、超时。
/// </summary>
public class MetricsEngineTests
{
    private static readonly TestPaymentRow[] s_rows =
    [
        TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 100),
        TestPaymentRow.Order(2, new DateTime(2026, 8, 1), 150),
        TestPaymentRow.Order(1, new DateTime(2026, 8, 2), 50),
    ];

    private static MetricDefinition RatioDef(string name = "aov")
        => TestHelper.Def(name, "ratio",
            ("numeratorField", "TotalAmount"), ("numeratorAggregate", "sum"),
            ("denominatorField", "PaidCount"), ("denominatorAggregate", "count"));

    [Fact]
    public async Task CalculateAsync_EmptyData_ReturnsEmpty()
    {
        var engine = TestHelper.Engine();

        var results = await engine.CalculateAsync<TestPaymentRow>(
            Array.Empty<TestPaymentRow>(), new[] { RatioDef() }, TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task CalculateAsync_EmptyDefinitions_ReturnsEmpty()
    {
        var engine = TestHelper.Engine();

        var results = await engine.CalculateAsync(
            s_rows, Array.Empty<MetricDefinition>(), TestContext.Current.CancellationToken);

        Assert.Empty(results);
    }

    [Fact]
    public async Task CalculateAsync_Sequential_ProducesResultPerDefinition()
    {
        var engine = TestHelper.Engine();
        var definitions = new[] { RatioDef("aov-1"), RatioDef("aov-2") };

        var results = await engine.CalculateAsync(s_rows, definitions, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count);
        Assert.Equal("aov-1", results[0].Name);
        Assert.Equal("aov-2", results[1].Name);
    }

    [Fact]
    public async Task CalculateAsync_UnknownCalculator_Throw_Throws()
    {
        var engine = TestHelper.Engine();
        var definitions = new[] { TestHelper.Def("ghost", "no-such-calculator", ("userIdField", "MemberId")) };

        var ex = await Assert.ThrowsAsync<MetricDefinitionException>(() =>
            engine.CalculateAsync(s_rows, definitions, TestContext.Current.CancellationToken));

        Assert.Contains("no-such-calculator", ex.Message);
    }

    [Fact]
    public async Task CalculateAsync_UnknownCalculator_Warn_SkipsResult()
    {
        var engine = TestHelper.Engine(calculator: MissingCalculatorBehavior.Warn);
        var definitions = new[] { RatioDef("ok"), TestHelper.Def("ghost", "no-such-calculator", ("userIdField", "MemberId")) };

        var results = await engine.CalculateAsync(s_rows, definitions, TestContext.Current.CancellationToken);

        var single = Assert.Single(results);
        Assert.Equal("ok", single.Name); // 幽灵定义被省略，列表长度 < 定义数
    }

    [Fact]
    public async Task CalculateAsync_MissingField_Throw_Throws()
    {
        var engine = TestHelper.Engine();
        var definitions = new[] { TestHelper.Def("bad", "ratio",
            ("numeratorField", "NoSuchField"), ("numeratorAggregate", "sum"),
            ("denominatorField", "PaidCount"), ("denominatorAggregate", "count")) };

        await Assert.ThrowsAsync<MetricDefinitionException>(() =>
            engine.CalculateAsync(s_rows, definitions, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task CalculateAsync_MissingField_Null_RatioReturnsNull()
    {
        var engine = TestHelper.Engine(field: MissingFieldBehavior.Null);
        var definitions = new[] { TestHelper.Def("bad", "ratio",
            ("numeratorField", "TotalAmount"), ("numeratorAggregate", "sum"),
            ("denominatorField", "NoSuchField"), ("denominatorAggregate", "count")) };

        var results = await engine.CalculateAsync(s_rows, definitions, TestContext.Current.CancellationToken);

        Assert.Null(Assert.Single(results).Value);
    }

    [Fact]
    public async Task CalculateAsync_MultiSlice_ExpandsToMultipleResults()
    {
        var engine = TestHelper.Engine();
        var definitions = new[] { TestHelper.Def("sales-by-day", "time-bucket",
            ("timeField", "BizDate"), ("bucket", "day"),
            ("valueField", "TotalAmount"), ("aggregate", "sum")) };

        var results = await engine.CalculateAsync(s_rows, definitions, TestContext.Current.CancellationToken);

        Assert.Equal(2, results.Count); // 8/1 与 8/2 两个切片
        Assert.All(results, r => Assert.Equal("sales-by-day", r.Name));
        Assert.Equal("2026-08-01", results[0].Dimensions!["bucket"]);
        Assert.Equal("2026-08-02", results[1].Dimensions!["bucket"]);
    }

    [Fact]
    public async Task CalculateAsync_CancellationRequested_Throws()
    {
        var engine = TestHelper.Engine();
        using var cts = new CancellationTokenSource();
        cts.Cancel();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            engine.CalculateAsync(s_rows, new[] { RatioDef() }, cts.Token));
    }

    [Fact]
    public async Task CalculateAsync_Timeout_ThrowsTimeout()
    {
        var engine = new MetricsEngine(
            new MetricsEngineOptions { CalculateTimeout = TimeSpan.FromTicks(1) },
            new CalculatorFactory());
        var definitions = Enumerable.Range(0, 100).Select(i => RatioDef($"aov-{i}")).ToArray();

        await Assert.ThrowsAsync<TimeoutException>(() =>
            engine.CalculateAsync(s_rows, definitions, TestContext.Current.CancellationToken));
    }
}
