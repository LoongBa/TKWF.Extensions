using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// AuditLoggingOptions 测试——覆盖默认值验证 + Options 绑定生效（V0.2.0）。
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

    // ── V0.2.0 Options 绑定测试 ──

    [Fact]
    public void OptionsBinding_FromConfiguration_BindsValues()
    {
        // Arrange — 模拟 appsettings.json 结构
        var configValues = new Dictionary<string, string?>
        {
            ["TKWF:AuditLogging:IsEnabled"] = "false",
            ["TKWF:AuditLogging:LogAnonymous"] = "true",
            ["TKWF:AuditLogging:SaveReturnValues"] = "true",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.Configure<AuditLoggingOptions>(configuration.GetSection("TKWF:AuditLogging"));
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<AuditLoggingOptions>>().Value;

        // Assert — 配置值绑定生效
        Assert.False(options.IsEnabled);
        Assert.True(options.LogAnonymous);
        Assert.True(options.SaveReturnValues);
    }

    [Fact]
    public void OptionsBinding_PartialConfiguration_KeepsDefaultsForUnset()
    {
        // Arrange — 只设置 IsEnabled，其余保持默认
        var configValues = new Dictionary<string, string?>
        {
            ["TKWF:AuditLogging:IsEnabled"] = "false",
        };

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build();

        var services = new ServiceCollection();
        services.Configure<AuditLoggingOptions>(configuration.GetSection("TKWF:AuditLogging"));
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<AuditLoggingOptions>>().Value;

        // Assert — IsEnabled 被绑定为 false，其余保持默认
        Assert.False(options.IsEnabled);
        Assert.False(options.LogAnonymous);       // 默认值
        Assert.False(options.SaveReturnValues);    // 默认值
    }

    [Fact]
    public void OptionsBinding_EmptySection_KeepsAllDefaults()
    {
        // Arrange — 空配置节
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();

        var services = new ServiceCollection();
        services.Configure<AuditLoggingOptions>(configuration.GetSection("TKWF:AuditLogging"));
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<AuditLoggingOptions>>().Value;

        // Assert — 全部默认值
        Assert.True(options.IsEnabled);
        Assert.False(options.LogAnonymous);
        Assert.False(options.SaveReturnValues);
        Assert.Empty(options.AdditionalSensitiveFields);
    }

    [Fact]
    public void OptionsBinding_ViaExtensionInitializer_RegistersOptions()
    {
        // Arrange — 验证 AuditLoggingExtensionInitializer.ConfigureServices 注册了 Options
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        // Act
        var options = provider.GetRequiredService<IOptions<AuditLoggingOptions>>().Value;

        // Assert — Options 可解析，值为默认
        Assert.NotNull(options);
        Assert.True(options.IsEnabled);
    }
}
