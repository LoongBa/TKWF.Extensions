using System;

namespace TKWF.Ext.DataDictionary.Tests;

/// <summary>
/// DataDictionaryOptions 默认值测试。
/// </summary>
public class DataDictionaryOptionsTests
{
    [Fact]
    public void Default_IsEnabled_IsTrue()
    {
        var options = new DataDictionaryOptions();
        Assert.True(options.IsEnabled);
    }
}