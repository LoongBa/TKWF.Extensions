namespace TKWF.Ext.Emailing.Tests;

/// <summary>
/// EmailingOptions 测试——覆盖默认值验证。
/// </summary>
public class EmailingOptionsTests
{
    [Fact]
    public void Default_SmtpHost_IsEmpty()
    {
        var options = new EmailingOptions();
        Assert.Equal("", options.SmtpHost);
    }

    [Fact]
    public void Default_SmtpPort_Is587()
    {
        var options = new EmailingOptions();
        Assert.Equal(587, options.SmtpPort);
    }

    [Fact]
    public void Default_DefaultFrom_IsEmpty()
    {
        var options = new EmailingOptions();
        Assert.Equal("", options.DefaultFrom);
    }

    [Fact]
    public void Default_IsEnabled_IsTrue()
    {
        var options = new EmailingOptions();
        Assert.True(options.IsEnabled);
    }
}
