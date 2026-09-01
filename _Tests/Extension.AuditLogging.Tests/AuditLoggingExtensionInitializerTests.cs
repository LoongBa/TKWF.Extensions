using System;
using System.Linq;
using System.Reflection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception.Auditing;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// AuditLoggingExtensionInitializer 测试——覆盖 [TKWFExtension] 特性声明、DI 注册、TryAddScoped 语义。
/// <para>注意：FreeSqlAuditLogStore 依赖 IFreeSql（未在测试 DI 中注册），
/// 因此 Scope 生命周期测试通过 Descriptor 验证而非 resolve。</para>
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

    [Fact]
    public void ConfigureServices_Registers_IAuditLogStore_Descriptor()
    {
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAuditLogStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(FreeSqlAuditLogStore), descriptor!.ImplementationType);
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
        Assert.Equal(typeof(FreeSqlAuditLogStore), descriptor.ImplementationType);
    }

    [Fact]
    public void ConfigureServices_DescriptorCount_IsExactlyOne()
    {
        var services = new ServiceCollection();
        new AuditLoggingExtensionInitializer<AuditLoggingUserInfo>().ConfigureServices(services);

        var count = services.Count(d => d.ServiceType == typeof(IAuditLogStore));
        Assert.Equal(1, count);
    }

    /// <summary>测试专用 IAuditLogStore：标记消费方自定义实现。</summary>
    private sealed class ConsumerAuditLogStore : IAuditLogStore
    {
        public Task SaveAsync(AuditLogEntry entry, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
