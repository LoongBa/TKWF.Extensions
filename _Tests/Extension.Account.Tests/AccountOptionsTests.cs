using System;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// AccountOptions 默认值测试。
/// </summary>
public class AccountOptionsTests
{
    [Fact]
    public void Defaults_MatchSpec()
    {
        var options = new AccountOptions();

        Assert.Equal(5, options.MaxFailedAttempts);
        Assert.Equal(15, options.DefaultLockoutMinutes);
        Assert.Equal(30, options.ResetCodeValidityMinutes);
        Assert.True(options.IsEnabled);
    }
}