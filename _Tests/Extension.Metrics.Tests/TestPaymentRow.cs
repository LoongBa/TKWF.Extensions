namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// 测试数据行——模拟 DMP-Lite 支付数据（PaymentLogStatView 字段子集），供引擎/计算器测试。
/// </summary>
public class TestPaymentRow
{
    public long? MemberId { get; set; }
    public DateTime? BizDate { get; set; }
    public decimal? TotalAmount { get; set; }
    public int? PaidCount { get; set; }
    public string? Stage { get; set; }
    public string? Category { get; set; }

    public static TestPaymentRow Order(long memberId, DateTime bizDate, decimal amount, int paidCount = 1)
        => new() { MemberId = memberId, BizDate = bizDate, TotalAmount = amount, PaidCount = paidCount };
}

/// <summary>测试辅助——构造定义/选项。</summary>
internal static class TestHelper
{
    public static MetricDefinition Def(string name, string calculator, params (string Key, string Value)[] parameters)
    {
        var dict = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (key, value) in parameters)
            dict[key] = value;
        return new MetricDefinition(name, calculator, dict);
    }

    public static MetricsEngineOptions Options(
        MissingCalculatorBehavior calculator = MissingCalculatorBehavior.Throw,
        MissingFieldBehavior field = MissingFieldBehavior.Throw)
        => new()
        {
            CalculateTimeout = TimeSpan.FromSeconds(30),
            MissingCalculatorBehavior = calculator,
            MissingFieldBehavior = field,
        };

    public static IMetricsEngine Engine(
        MissingCalculatorBehavior? calculator = null,
        MissingFieldBehavior? field = null)
        => new MetricsEngine(
            Options(calculator ?? MissingCalculatorBehavior.Throw, field ?? MissingFieldBehavior.Throw),
            new CalculatorFactory());
}
