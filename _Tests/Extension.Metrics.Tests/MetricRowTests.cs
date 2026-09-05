namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// MetricRow 测试——委托字段访问、类型转换、null 处理。
/// </summary>
public class MetricRowTests
{
    private static MetricRow Row(TestPaymentRow source)
        => new(source, (o, name) => o?.GetType().GetProperty(name)?.GetValue(o));

    [Fact]
    public void Get_AccessesPropertyViaDelegate()
    {
        var row = Row(TestPaymentRow.Order(1001, new DateTime(2026, 8, 1), 199.5m));

        var memberId = row.Get<long?>("MemberId");

        Assert.Equal(1001, memberId);
    }

    [Fact]
    public void Get_ConvertsBoxedValueType()
    {
        var row = Row(TestPaymentRow.Order(1, new DateTime(2026, 8, 1), 99.9m));

        var amount = row.Get<decimal?>("TotalAmount");
        var date = row.Get<DateTime?>("BizDate");

        Assert.Equal(99.9m, amount);
        Assert.Equal(new DateTime(2026, 8, 1), date);
    }

    [Fact]
    public void Get_NullValue_ReturnsDefault()
    {
        var row = Row(new TestPaymentRow { MemberId = null });

        Assert.Null(row.Get<long?>("MemberId"));
    }
}
