using System;
using System.Collections.Generic;

namespace TKWF.Ext.SecurityLog.Tests;

/// <summary>
/// SecurityLoggingOptions 测试——[Options] 特性声明 + 默认值（Enabled=true、EventTypes 空 = 全部）。
/// </summary>
public class SecurityLoggingOptionsTests
{
    [Fact]
    public void Defaults_EnabledTrue_EventTypesEmpty()
    {
        var options = new SecurityLoggingOptions();

        Assert.True(options.Enabled);
        Assert.NotNull(options.EventTypes);
        Assert.Empty(options.EventTypes);   // 空 = 全部事件启用
    }

    [Fact]
    public void OptionsAttribute_DeclaresSectionPath()
    {
        var attr = System.Reflection.CustomAttributeExtensions
            .GetCustomAttribute<global::TKW.Framework.Domain.OptionsAttribute>(typeof(SecurityLoggingOptions));

        Assert.NotNull(attr);
        Assert.Equal("TKWF:SecurityLog", attr!.SectionPath);
    }

    [Fact]
    public void EventTypes_IsCaseInsensitive()
    {
        var options = new SecurityLoggingOptions
        {
            EventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "login" },
        };

        Assert.Contains("Login", options.EventTypes);
        Assert.Contains("LOGIN", options.EventTypes);
    }

    [Fact]
    public void Defaults_RetentionDays90_CleanupBatchSize500()
    {
        var options = new SecurityLoggingOptions();

        Assert.Equal(90, options.RetentionDays);
        Assert.Equal(500, options.CleanupBatchSize);
    }
}
