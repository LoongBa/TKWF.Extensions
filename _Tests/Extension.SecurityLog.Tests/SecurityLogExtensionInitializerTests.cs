using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.SecurityLog.Tests;

/// <summary>
/// SecurityLogExtensionInitializer 测试——[TKWFExtension] 声明、DI 注册（Options/DataService/Store/QueryService）、
/// TryAddScoped 语义、过滤器入口（FilterBuilder.AddSecurityLog 扩展方法）。
/// </summary>
public class SecurityLogExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(SecurityLogExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("SecurityLog", attr.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_Store_Descriptor()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(ISecurityLogStore));

        Assert.Equal(typeof(SecurityLogStore), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_QueryService_Descriptor()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(ISecurityLogQueryService));

        Assert.Equal(typeof(SecurityLogQueryService), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_DataService_Descriptor()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(SecurityLogEntityDataService));

        Assert.Equal(typeof(SecurityLogEntityDataService), descriptor.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_Options()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        Assert.Contains(services, d => d.ServiceType == typeof(Microsoft.Extensions.Options.IOptions<SecurityLoggingOptions>)
                                       || d.ServiceType == typeof(Microsoft.Extensions.Options.IConfigureOptions<SecurityLoggingOptions>));
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStore()
    {
        var services = new ServiceCollection();
        // 消费方先注册自定义 ISecurityLogStore → TryAddScoped 不应覆盖
        services.AddScoped<ISecurityLogStore, ConsumerSecurityLogStore>();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var storeDescriptors = services.Where(d => d.ServiceType == typeof(ISecurityLogStore)).ToList();
        Assert.Single(storeDescriptors);
        Assert.Equal(typeof(ConsumerSecurityLogStore), storeDescriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerQueryService()
    {
        var services = new ServiceCollection();
        services.AddScoped<ISecurityLogQueryService, ConsumerQueryService>();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(ISecurityLogQueryService)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerQueryService), descriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureFilters_DoesNotAutoRegisterFilter()
    {
        // 过滤器不自动注册（消费方 opt-in：builder.AddSecurityLog()）——ConfigureFilters 空实现
        var services = new ServiceCollection();
        var builder = new FilterBuilder<TestUserInfo>();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureFilters(builder);

        Assert.Empty(builder.Filters);
    }

    [Fact]
    public void AddSecurityLog_ExtensionMethod_AddsGlobalFilter()
    {
        var builder = new FilterBuilder<TestUserInfo>();

        var result = builder.AddSecurityLog<TestUserInfo>();

        Assert.Same(builder, result);
        Assert.Single(builder.Filters);
        Assert.IsType<SecurityLogFilterAttribute<TestUserInfo>>(builder.Filters[0]);
    }

    [Fact]
    public async Task InitializeAsync_Completes()
    {
        var initializer = new SecurityLogExtensionInitializer<TestUserInfo>();
        await initializer.InitializeAsync();
        Assert.Equal("SecurityLog", initializer.Name);
        Assert.False(string.IsNullOrEmpty(initializer.Description));
    }

    /// <summary>测试专用 ISecurityLogStore：标记消费方自定义实现。</summary>
    private sealed class ConsumerSecurityLogStore : ISecurityLogStore
    {
        public Task SaveAsync(SecurityLogEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>测试专用 ISecurityLogQueryService：标记消费方自定义实现。</summary>
    private sealed class ConsumerQueryService : ISecurityLogQueryService
    {
        public Task<SecurityLogPagedResult> GetListAsync(SecurityLogQueryInput input, CancellationToken ct = default)
            => Task.FromResult(new SecurityLogPagedResult(0, Array.Empty<SecurityLogListItemDto>()));

        public Task<SecurityLogDetailDto?> GetDetailAsync(long id, CancellationToken ct = default)
            => Task.FromResult<SecurityLogDetailDto?>(null);

        public Task<long> CountAsync(SecurityLogQueryInput input, CancellationToken ct = default)
            => Task.FromResult(0L);
    }
}
