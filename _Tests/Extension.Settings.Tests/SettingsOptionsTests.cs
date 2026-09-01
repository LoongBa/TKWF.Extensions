namespace TKWF.Ext.Settings.Tests;

/// <summary>
/// SettingsOptions 测试——覆盖默认值验证。
/// </summary>
public class SettingsOptionsTests
{
    [Fact]
    public void Default_DefaultSettingValueProvider_IsGlobal()
    {
        var options = new SettingsOptions();
        Assert.Equal("Global", options.DefaultSettingValueProvider);
    }

    [Fact]
    public void Default_IsEnabled_IsTrue()
    {
        var options = new SettingsOptions();
        Assert.True(options.IsEnabled);
    }

    [Fact]
    public void Custom_DefaultSettingValueProvider_IsRespected()
    {
        var options = new SettingsOptions { DefaultSettingValueProvider = "Tenant" };
        Assert.Equal("Tenant", options.DefaultSettingValueProvider);
    }

    [Fact]
    public void Custom_IsEnabled_CanBeDisabled()
    {
        var options = new SettingsOptions { IsEnabled = false };
        Assert.False(options.IsEnabled);
    }
}
