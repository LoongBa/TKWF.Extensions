using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="WeChatMessageGatewayOptions"/> 配置断言（V0.2.0 增网关模块——批次 3）：
/// <c>[Options("TKWF:WeChatOperations:Gateway")]</c> 配置节声明 + 默认值（GatewayEnabled=false fail-closed /
/// IdempotencyRetentionDays=7 / PendingTimeoutMinutes=5 / AsyncForwardRetryCount=3 / BackoffSeconds=2 /
/// DefaultSyncTimeoutSeconds=2）+ IConfiguration 绑定（完整/部分/空节）+ Initializer AddOptions 可解析。
/// </summary>
public class MessageGatewayOptionsTests
{
    // ═══════════════════ [Options] 配置节声明 ═══════════════════

    /// <summary>[Options("TKWF:WeChatOperations:Gateway")] 声明——SG1 消费方自动绑定（独立子节，配置分层 §十·六 ②）。</summary>
    [Fact]
    public void OptionsAttribute_DeclaresGatewaySection()
    {
        var attr = typeof(WeChatMessageGatewayOptions).GetCustomAttributes(typeof(OptionsAttribute), false)
            .Cast<OptionsAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("TKWF:WeChatOperations:Gateway", attr!.SectionPath);
    }

    // ═══════════════════ 默认值 ═══════════════════

    /// <summary>GatewayEnabled 默认 false（fail-closed 默认关——未启用时既有 handler 零回归）。</summary>
    [Fact]
    public void Default_GatewayEnabled_IsFalse()
    {
        var options = new WeChatMessageGatewayOptions();
        Assert.False(options.GatewayEnabled);
    }

    /// <summary>IdempotencyRetentionDays 默认 7（覆盖微信全部重试窗口 + 对账余量）。</summary>
    [Fact]
    public void Default_IdempotencyRetentionDays_Is7()
    {
        var options = new WeChatMessageGatewayOptions();
        Assert.Equal(7, options.IdempotencyRetentionDays);
    }

    /// <summary>PendingTimeoutMinutes 默认 5（陈旧 Pending 复用键超时窗口）。</summary>
    [Fact]
    public void Default_PendingTimeoutMinutes_Is5()
    {
        var options = new WeChatMessageGatewayOptions();
        Assert.Equal(5, options.PendingTimeoutMinutes);
    }

    /// <summary>AsyncForwardRetryCount 默认 3（BackgroundJob 队列 MaxRetryCount）。</summary>
    [Fact]
    public void Default_AsyncForwardRetryCount_Is3()
    {
        var options = new WeChatMessageGatewayOptions();
        Assert.Equal(3, options.AsyncForwardRetryCount);
    }

    /// <summary>BackoffSeconds 默认 2（指数退避基础值）。</summary>
    [Fact]
    public void Default_BackoffSeconds_Is2()
    {
        var options = new WeChatMessageGatewayOptions();
        Assert.Equal(2, options.BackoffSeconds);
    }

    /// <summary>DefaultSyncTimeoutSeconds 默认 2（微信 5 秒时限留 1 秒本地兜底余量）。</summary>
    [Fact]
    public void Default_DefaultSyncTimeoutSeconds_Is2()
    {
        var options = new WeChatMessageGatewayOptions();
        Assert.Equal(2, options.DefaultSyncTimeoutSeconds);
    }

    // ═══════════════════ IConfiguration 绑定 ═══════════════════

    /// <summary>完整配置节绑定生效——TKWF:WeChatOperations:Gateway 各键映射（Options 模式——services.Configure）。</summary>
    [Fact]
    public void OptionsBinding_FromConfiguration_BindsValues()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["TKWF:WeChatOperations:Gateway:GatewayEnabled"] = "true",
            ["TKWF:WeChatOperations:Gateway:IdempotencyRetentionDays"] = "30",
            ["TKWF:WeChatOperations:Gateway:PendingTimeoutMinutes"] = "10",
            ["TKWF:WeChatOperations:Gateway:AsyncForwardRetryCount"] = "5",
            ["TKWF:WeChatOperations:Gateway:BackoffSeconds"] = "4",
            ["TKWF:WeChatOperations:Gateway:DefaultSyncTimeoutSeconds"] = "3",
        };

        var services = new ServiceCollection();
        services.Configure<WeChatMessageGatewayOptions>(new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build()
            .GetSection("TKWF:WeChatOperations:Gateway"));
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<WeChatMessageGatewayOptions>>().Value;

        Assert.True(options.GatewayEnabled);
        Assert.Equal(30, options.IdempotencyRetentionDays);
        Assert.Equal(10, options.PendingTimeoutMinutes);
        Assert.Equal(5, options.AsyncForwardRetryCount);
        Assert.Equal(4, options.BackoffSeconds);
        Assert.Equal(3, options.DefaultSyncTimeoutSeconds);
    }

    /// <summary>部分配置——未设置键保持默认（fail-closed 语义：只开 GatewayEnabled 其余保持默认）。</summary>
    [Fact]
    public void OptionsBinding_PartialConfiguration_KeepsDefaultsForUnset()
    {
        var configValues = new Dictionary<string, string?>
        {
            ["TKWF:WeChatOperations:Gateway:GatewayEnabled"] = "true",
        };

        var services = new ServiceCollection();
        services.Configure<WeChatMessageGatewayOptions>(new ConfigurationBuilder()
            .AddInMemoryCollection(configValues)
            .Build()
            .GetSection("TKWF:WeChatOperations:Gateway"));
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<WeChatMessageGatewayOptions>>().Value;

        Assert.True(options.GatewayEnabled);
        Assert.Equal(7, options.IdempotencyRetentionDays);      // 默认值
        Assert.Equal(5, options.PendingTimeoutMinutes);         // 默认值
        Assert.Equal(3, options.AsyncForwardRetryCount);        // 默认值
        Assert.Equal(2, options.BackoffSeconds);                // 默认值
        Assert.Equal(2, options.DefaultSyncTimeoutSeconds);     // 默认值
    }

    /// <summary>空配置节——全部默认值（fail-closed：GatewayEnabled=false）。</summary>
    [Fact]
    public void OptionsBinding_EmptySection_KeepsAllDefaults()
    {
        var services = new ServiceCollection();
        services.Configure<WeChatMessageGatewayOptions>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build()
            .GetSection("TKWF:WeChatOperations:Gateway"));
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<WeChatMessageGatewayOptions>>().Value;

        Assert.False(options.GatewayEnabled);
        Assert.Equal(7, options.IdempotencyRetentionDays);
        Assert.Equal(5, options.PendingTimeoutMinutes);
        Assert.Equal(3, options.AsyncForwardRetryCount);
        Assert.Equal(2, options.BackoffSeconds);
        Assert.Equal(2, options.DefaultSyncTimeoutSeconds);
    }

    // ═══════════════════ Initializer AddOptions ═══════════════════

    /// <summary>ExtensionInitializer.ConfigureServices 注册 AddOptions&lt;WeChatMessageGatewayOptions&gt;——
    /// 守卫工厂 ActivatorUtilities 解析剩余参数需 IOptions 可解析（Handler/MessageGatewayService ctor 依赖）。</summary>
    [Fact]
    public void OptionsBinding_ViaExtensionInitializer_RegistersOptions()
    {
        var services = new ServiceCollection();
        new WeChatOperationsExtensionInitializer<WeChatOperationsUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<WeChatMessageGatewayOptions>>().Value;

        Assert.NotNull(options);
        Assert.False(options.GatewayEnabled);   // 默认 fail-closed
        Assert.Equal(7, options.IdempotencyRetentionDays);
    }
}
