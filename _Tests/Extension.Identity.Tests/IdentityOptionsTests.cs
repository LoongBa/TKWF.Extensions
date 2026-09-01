using System;

namespace TKWF.Ext.Identity.Tests;

/// <summary>
/// IdentityOptions 默认值测试。
/// </summary>
public class IdentityOptionsTests
{
    [Fact]
    public void Default_PasswordMinLength_Is6()
    {
        var options = new IdentityOptions();
        Assert.Equal(6, options.PasswordMinLength);
    }

    [Fact]
    public void Default_IsEnabled_IsTrue()
    {
        var options = new IdentityOptions();
        Assert.True(options.IsEnabled);
    }
}