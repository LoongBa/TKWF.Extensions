namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// AuditLoggingOptions 测试——覆盖默认值验证。
/// </summary>
public class AuditLoggingOptionsTests
{
    [Fact]
    public void Default_IsEnabled_IsTrue()
    {
        var options = new AuditLoggingOptions();
        Assert.True(options.IsEnabled);
    }

    [Fact]
    public void Default_LogAnonymous_IsFalse()
    {
        var options = new AuditLoggingOptions();
        Assert.False(options.LogAnonymous);
    }

    [Fact]
    public void Default_SaveReturnValues_IsFalse()
    {
        var options = new AuditLoggingOptions();
        Assert.False(options.SaveReturnValues);
    }

    [Fact]
    public void Default_AdditionalSensitiveFields_IsNotEmpty()
    {
        var options = new AuditLoggingOptions();
        Assert.NotNull(options.AdditionalSensitiveFields);
        Assert.Empty(options.AdditionalSensitiveFields);
    }

    [Fact]
    public void AdditionalSensitiveFields_IsCaseInsensitive()
    {
        var options = new AuditLoggingOptions();
        options.AdditionalSensitiveFields.Add("Password");
        Assert.Contains("password", options.AdditionalSensitiveFields);
        Assert.Contains("PASSWORD", options.AdditionalSensitiveFields);
    }
}
