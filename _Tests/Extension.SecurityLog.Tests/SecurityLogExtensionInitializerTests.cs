using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.SecurityLog.Tests;

/// <summary>
/// SecurityLogExtensionInitializer 测试——[TKWFExtension] 声明、AddConstructibleService 注册形态
/// （接口守卫工厂 + 实现类 throw-factory + 域作用域外解析抛）、Options/DataService 注册、过滤器入口
/// （FilterBuilder.AddSecurityLog 扩展方法）。
/// <para>V4.10.53（领域自治根治）：注册形态由 TryAddScoped 改为 <c>AddConstructibleService</c>——
/// 消费方统一经 <c>User.Use&lt;ISecurityLogStore&gt;()</c> 等解析（AOP 路径先设 CurrentAopUser 再 GetRequiredService）。</para>
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

    // ── ISecurityLogStore：接口守卫工厂 + 实现 throw-factory + 域外抛 ──

    [Fact]
    public void ConfigureServices_Registers_ISecurityLogStore_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // AddConstructibleService——接口注册为构造工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISecurityLogStore));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_SecurityLogStore_ThrowFactory()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<ISecurityLogStore>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(SecurityLogStore));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<SecurityLogStore>());
    }

    [Fact]
    public void InterfaceFactory_ISecurityLogStore_OutsideUseScope_Throws()
    {
        // 接口构造工厂的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISecurityLogStore>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("ISecurityLogStore", ex.Message);
    }

    // ── ISecurityLogQueryService：接口守卫工厂 + 实现 throw-factory + 域外抛 ──

    [Fact]
    public void ConfigureServices_Registers_ISecurityLogQueryService_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISecurityLogQueryService));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_SecurityLogQueryService_ThrowFactory()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(SecurityLogQueryService));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<SecurityLogQueryService>());
    }

    [Fact]
    public void InterfaceFactory_ISecurityLogQueryService_OutsideUseScope_Throws()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISecurityLogQueryService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("ISecurityLogQueryService", ex.Message);
    }

    // ── ISecurityLogAnalyticsService：接口守卫工厂 + 实现 throw-factory + 域外抛 ──

    [Fact]
    public void ConfigureServices_Registers_ISecurityLogAnalyticsService_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ISecurityLogAnalyticsService));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_SecurityLogAnalyticsService_ThrowFactory()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(SecurityLogAnalyticsService));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<SecurityLogAnalyticsService>());
    }

    [Fact]
    public void InterfaceFactory_ISecurityLogAnalyticsService_OutsideUseScope_Throws()
    {
        var services = new ServiceCollection();
        new SecurityLogExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ISecurityLogAnalyticsService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("ISecurityLogAnalyticsService", ex.Message);
    }

    [Fact]
    public void ConfigureServices_Registers_DataService_Descriptor()
    {
        // v4.10.8 (ADR61)：Initializer 不再手动注册 DataService——SG 基类类型判定生成，
        // 经扩展 ProjectMetaContext.GetServiceRegistrations() 暴露（消费方聚合自动注册为可构造工厂）。
        var regs = TKWF.Ext.SecurityLog.Generated.ProjectMetaContext.GetOrCreateInstance()
            .GetServiceRegistrations().ToList();

        Assert.Contains(regs, r => r.Implementation == typeof(SecurityLogEntityDataService));
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
        // V4.10.25 (ADR78)：签名带 IServiceProvider——空实现不接受 sp，传 null 验证不抛
        var initializer = new SecurityLogExtensionInitializer<TestUserInfo>();
        await initializer.InitializeAsync(null!);
        Assert.Equal("SecurityLog", initializer.Name);
        Assert.False(string.IsNullOrEmpty(initializer.Description));
    }
}
