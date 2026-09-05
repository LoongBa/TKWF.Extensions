namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// CalculatorFactory 测试——静态注册表零反射查找。
/// </summary>
public class CalculatorFactoryTests
{
    private readonly CalculatorFactory _factory = new();

    [Fact]
    public void TryCreate_KnownCalculator_ReturnsInstance()
    {
        var calculator = _factory.TryCreate("repurchase-rate");

        Assert.NotNull(calculator);
        Assert.Equal("repurchase-rate", calculator!.Name);
    }

    [Fact]
    public void TryCreate_UnknownCalculator_ReturnsNull()
    {
        Assert.Null(_factory.TryCreate("no-such-calculator"));
    }

    [Fact]
    public void TryCreate_EmptyName_ReturnsNull()
    {
        Assert.Null(_factory.TryCreate(string.Empty));
    }
}
