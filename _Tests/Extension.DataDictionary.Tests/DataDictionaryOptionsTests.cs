using System;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// DataDictionaryOptions 默认值测试（V0.2.0 扩展）。
/// </summary>
public class DataDictionaryOptionsTests
{
    [Fact]
    public void Default_IsEnabled_IsTrue()
    {
        var options = new DataDictionaryOptions();
        Assert.True(options.IsEnabled);
    }

    [Fact]
    public void Default_EnableCache_IsTrue()
    {
        var options = new DataDictionaryOptions();
        Assert.True(options.EnableCache);
    }

    [Fact]
    public void Default_CacheExpirationSeconds_Is300()
    {
        var options = new DataDictionaryOptions();
        Assert.Equal(300, options.CacheExpirationSeconds);
    }

    [Fact]
    public void Default_EnableTreeMode_IsFalse()
    {
        var options = new DataDictionaryOptions();
        Assert.False(options.EnableTreeMode);
    }

    [Fact]
    public void Custom_Options_CanBeSet()
    {
        var options = new DataDictionaryOptions
        {
            IsEnabled = false,
            EnableCache = false,
            CacheExpirationSeconds = 60,
            EnableTreeMode = true
        };

        Assert.False(options.IsEnabled);
        Assert.False(options.EnableCache);
        Assert.Equal(60, options.CacheExpirationSeconds);
        Assert.True(options.EnableTreeMode);
    }
}