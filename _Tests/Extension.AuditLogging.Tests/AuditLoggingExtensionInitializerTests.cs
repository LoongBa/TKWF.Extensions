using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Auditing;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// AuditLoggingExtensionInitializer 测试——覆盖 [TKWFExtension] 特性声明、DI 注册形态（三态）、守卫/throw-factory 语义。
/// <para>V4.10.53（领域自治根治，ADR90，正确路线）注册形态：</para>
/// <list type="bullet">
/// <item><see cref="IAuditLogStore"/>（主框架契约，非 IDomainService——不可修改主框架；AuditLogFilterAttribute 经
///     <c>context.ServiceProvider.GetService&lt;IAuditLogStore&gt;()</c> 普通 DI 解析）→ <b>接线型 TryAddScoped</b>
///     （ImplementationType = AuditLogStore，普通 DI 可解析）；</item>
/// <item><see cref="IAuditLogQueryService"/> / <see cref="IAuditLogAnalyticsService"/>（接口 : IDomainService）→
///     <b>AddConstructibleService</b>：接口 = 可构造守卫工厂（非实现映射）+ 实现类 = throw-factory；
///     域作用域外直接 DI 解析接口必抛领域架构守卫。</item>
/// </list>
/// </summary>
public class AuditLoggingExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(AuditLoggingExtensionInitializer<AuditLoggingUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("AuditLogging", attr.Name);
    }

    // ── 类1：IAuditLogStore 接线型（TryAddScoped 普通 DI）──

    [Fact]
    public void ConfigureServices_Registers_IAuditLogStore_Descriptor()
    {
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditLogStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(AuditLogStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStore()
    {
        var services = new ServiceCollection();
        // 消费方先注册自定义 IAuditLogStore → TryAddScoped 不应覆盖
        services.AddScoped<IAuditLogStore, ConsumerAuditLogStore>();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);

        // TryAddScoped 语义：只有 1 个 IAuditLogStore descriptor（消费方的）
        var storeDescriptors = services.Where(d => d.ServiceType == typeof(IAuditLogStore)).ToList();
        Assert.Single(storeDescriptors);
        Assert.Equal(typeof(ConsumerAuditLogStore), storeDescriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_Registers_ScopedLifecycle()
    {
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(IAuditLogStore));
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_ImplementationType()
    {
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);

        var descriptor = services.First(d => d.ServiceType == typeof(IAuditLogStore));
        Assert.Equal(typeof(AuditLogStore), descriptor.ImplementationType);
    }

    [Fact]
    public void ConfigureServices_DescriptorCount_IsExactlyOne()
    {
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);

        var count = services.Count(d => d.ServiceType == typeof(IAuditLogStore));
        Assert.Equal(1, count);
    }

    /// <summary>接线型生产路径哨兵：Store 经普通 DI 可解析（主框架 AuditLogFilter 经
    /// <c>context.ServiceProvider.GetService&lt;IAuditLogStore&gt;()</c> 解析——旧 ctor 注入 IDomainUser
    /// 永不注册 DI → GetService 构造失败、审计落库静默失效；改后 ctor(IServiceProvider, ILogger) 全可解析）。</summary>
    [Fact]
    public void Store_Resolvable_From_PlainDi()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var store = provider.GetRequiredService<IAuditLogStore>();
        Assert.IsType<AuditLogStore>(store);
    }

    // ── 类2：IAuditLogQueryService AddConstructibleService（标准门面）──

    [Fact]
    public void ConfigureServices_Registers_IAuditLogQueryService_GuardFactory()
    {
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);

        // V4.10.53：AddConstructibleService——接口注册为可构造守卫工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditLogQueryService));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_QueryService_ThrowFactory()
    {
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<IAuditLogQueryService>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(AuditLogQueryService));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<AuditLogQueryService>());
    }

    [Fact]
    public void QueryServiceInterface_OutsideUseScope_Throws()
    {
        // AddConstructibleService 的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IAuditLogQueryService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IAuditLogQueryService", ex.Message);
    }

    // ── 类3：IAuditLogAnalyticsService AddConstructibleService（标准门面）──

    [Fact]
    public void ConfigureServices_Registers_IAuditLogAnalyticsService_GuardFactory()
    {
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);

        // V4.10.53：AddConstructibleService——接口注册为可构造守卫工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditLogAnalyticsService));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_AnalyticsService_ThrowFactory()
    {
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<IAuditLogAnalyticsService>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(AuditLogAnalyticsService));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<AuditLogAnalyticsService>());
    }

    [Fact]
    public void AnalyticsServiceInterface_OutsideUseScope_Throws()
    {
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IAuditLogAnalyticsService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IAuditLogAnalyticsService", ex.Message);
    }

    // ── Options ──

    [Fact]
    public void ConfigureServices_Registers_AuditLoggingOptions()
    {
        // V4.10.53（skill §4.8-10 门面 AddOptions 补齐）：AnalyticsService ctor 依赖 IOptions<AuditLoggingOptions>——
        // AddConstructibleService 守卫工厂经 ActivatorUtilities 解析需其可解析 → AddOptions<AuditLoggingOptions>() 注册
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<AuditLoggingOptions>>();
        Assert.NotNull(options);
        Assert.Equal(90, options.Value.RetentionDays);
        Assert.Equal(500, options.Value.CleanupBatchSize);
    }

    /// <summary>测试专用 IAuditLogStore：标记消费方自定义实现。</summary>
    private sealed class ConsumerAuditLogStore : IAuditLogStore
    {
        public Task SaveAsync(AuditLogEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
