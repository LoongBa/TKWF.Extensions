using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKWF.Ext.RateLimiting;

namespace TKWF.Ext.RateLimiting.Tests;

/// <summary>
/// RateLimitingOptions 测试——默认值 + [Options] 声明 + TKWF:RateLimiting 配置节绑定。
/// </summary>
public class RateLimitingOptionsTests
{
    [Fact]
    public void Defaults_AlignWithDomainLayer()
    {
        // Oracle P2-1：默认值对齐主框架 Domain 层 RateLimitPolicyOptions（PermitLimit=5 / Window=1min / Segments=6 / QueueLimit=0）
        var options = new RateLimitingOptions();

        Assert.Equal(RateLimitPartition.Ip, options.Partition);
        Assert.Equal(429, options.RejectionStatusCode);
        Assert.True(options.RetryAfter);
        Assert.NotNull(options.EndpointPolicies);
        Assert.Empty(options.EndpointPolicies);

        Assert.Equal(RateLimitAlgorithm.FixedWindow, options.Global.Algorithm);
        Assert.Equal(5, options.Global.PermitLimit);
        Assert.Equal(60, options.Global.WindowSeconds);
        Assert.Equal(6, options.Global.SegmentsPerWindow);
        Assert.Equal(1, options.Global.ReplenishmentTokensPerSecond);
        Assert.Equal(0, options.Global.QueueLimit);
    }

    [Fact]
    public void OptionsAttribute_DeclaresTkwfRateLimitingSection()
    {
        // SG1 [Options] 声明——消费方启用该特性即自动绑定 TKWF:RateLimiting 配置节
        var attribute = typeof(RateLimitingOptions)
            .GetCustomAttributes(typeof(TKW.Framework.Domain.OptionsAttribute), inherit: false)
            .Cast<TKW.Framework.Domain.OptionsAttribute>()
            .Single();

        Assert.Equal("TKWF:RateLimiting", attribute.SectionPath);
        Assert.Equal("TKWF:RateLimiting", RateLimitingOptions.SectionName);
    }

    [Fact]
    public void BindConfiguration_BindsInMemoryConfig()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["TKWF:RateLimiting:RejectionStatusCode"] = "429",
                ["TKWF:RateLimiting:Partition"] = "User",
                ["TKWF:RateLimiting:Global:Algorithm"] = "TokenBucket",
                ["TKWF:RateLimiting:Global:PermitLimit"] = "2",
                ["TKWF:RateLimiting:Global:WindowSeconds"] = "30",
                ["TKWF:RateLimiting:Global:ReplenishmentTokensPerSecond"] = "3",
                ["TKWF:RateLimiting:Global:SegmentsPerWindow"] = "4",
                ["TKWF:RateLimiting:Global:QueueLimit"] = "1",
                ["TKWF:RateLimiting:EndpointPolicies:/api/auth/login:Algorithm"] = "FixedWindow",
                ["TKWF:RateLimiting:EndpointPolicies:/api/auth/login:PermitLimit"] = "5"
            })
            .Build());
        services.AddOptions<RateLimitingOptions>().BindConfiguration(RateLimitingOptions.SectionName);

        var options = services.BuildServiceProvider()
            .GetRequiredService<IOptions<RateLimitingOptions>>().Value;

        Assert.Equal(429, options.RejectionStatusCode);
        Assert.Equal(RateLimitPartition.User, options.Partition);
        Assert.Equal(RateLimitAlgorithm.TokenBucket, options.Global.Algorithm);
        Assert.Equal(2, options.Global.PermitLimit);
        Assert.Equal(30, options.Global.WindowSeconds);
        Assert.Equal(3, options.Global.ReplenishmentTokensPerSecond);
        Assert.Equal(4, options.Global.SegmentsPerWindow);
        Assert.Equal(1, options.Global.QueueLimit);

        var endpoint = Assert.Single(options.EndpointPolicies);
        Assert.Equal("/api/auth/login", endpoint.Key);
        Assert.Equal(RateLimitAlgorithm.FixedWindow, endpoint.Value.Algorithm);
        Assert.Equal(5, endpoint.Value.PermitLimit);
    }

    [Fact]
    public void RateLimitPolicyModel_ValidatesTokenBucketSemantics()
    {
        // Oracle P2-1：PermitLimit = TokenLimit 最大容量（突发许可），补充走 ReplenishmentTokensPerSecond
        var policy = new RateLimitPolicyModel
        {
            Algorithm = RateLimitAlgorithm.TokenBucket,
            PermitLimit = 10,
            ReplenishmentTokensPerSecond = 2
        };

        Assert.Equal(10, policy.PermitLimit);              // TokenLimit
        Assert.Equal(2, policy.ReplenishmentTokensPerSecond); // TokensPerPeriod(每秒)
    }
}