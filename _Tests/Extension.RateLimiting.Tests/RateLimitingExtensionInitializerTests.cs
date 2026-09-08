using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKWF.Ext.RateLimiting;

namespace TKWF.Ext.RateLimiting.Tests;

/// <summary>
/// RateLimitingExtensionInitializer 测试——DI 注册（Options 绑定）+ 扩展标记 + 消费方宿主启用。
/// </summary>
public class RateLimitingExtensionInitializerTests
{
    [Fact]
    public void ConfigureServices_RegistersOptionsWithDefaults()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        new RateLimitingExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<RateLimitingOptions>>().Value;

        // 默认值（配置节未提供时兜底）
        Assert.Equal(RateLimitPartition.Ip, options.Partition);
        Assert.Equal(429, options.RejectionStatusCode);
        Assert.Equal(RateLimitAlgorithm.FixedWindow, options.Global.Algorithm);
        Assert.Equal(5, options.Global.PermitLimit);
    }

    [Fact]
    public void ConfigureServices_IsIdempotentWithAddTkfwRateLimiting()
    {
        // Initializer Options 绑定 + AddTkfwRateLimiting 内部绑定重复调用——幂等无害（PostConfigure 单实例）
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        new RateLimitingExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddTkfwRateLimiting(o => o.RejectionStatusCode = 400);

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<RateLimitingOptions>>().Value;

        Assert.Equal(400, options.RejectionStatusCode);
    }

    [Fact]
    public void ExtensionInitializer_IsMarkedWithTkwfExtension()
    {
        var attribute = typeof(RateLimitingExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKW.Framework.Domain.TKWFExtensionAttribute), inherit: false)
            .Cast<TKW.Framework.Domain.TKWFExtensionAttribute>()
            .Single();

        Assert.Equal("RateLimiting", attribute.Name);
    }

    [Fact]
    public void AddTkfwRateLimiting_RegistersRateLimiterOptions()
    {
        // AddRateLimiter 展开：RateLimiterOptions 可解析且 RejectionStatusCode 编程式覆盖生效
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddTkfwRateLimiting(o =>
        {
            o.RejectionStatusCode = 423;
            o.RetryAfter = false;
        });

        var sp = services.BuildServiceProvider();
        var rateLimiterOptions = sp
            .GetRequiredService<IOptions<Microsoft.AspNetCore.RateLimiting.RateLimiterOptions>>().Value;

        Assert.Equal(423, rateLimiterOptions.RejectionStatusCode);
    }
}