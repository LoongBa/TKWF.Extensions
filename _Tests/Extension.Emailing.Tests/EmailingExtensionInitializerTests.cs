using System;
using System.Linq;
using System.Reflection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Emailing.Tests;

/// <summary>
/// EmailingExtensionInitializer 测试——覆盖 [TKWFExtension] 特性声明、DI 注册、TryAddScoped 语义。
/// </summary>
public class EmailingExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(EmailingExtensionInitializer<EmailingUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Emailing", attr.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_IEmailRecordStore_Descriptor()
    {
        var services = new ServiceCollection();
        new EmailingExtensionInitializer<EmailingUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailRecordStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(FreeSqlEmailRecordStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IEmailSender_Descriptor()
    {
        var services = new ServiceCollection();
        new EmailingExtensionInitializer<EmailingUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailSender));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(SmtpEmailSender), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStore()
    {
        var services = new ServiceCollection();
        services.AddScoped<IEmailRecordStore, ConsumerEmailRecordStore>();
        new EmailingExtensionInitializer<EmailingUserInfo>().ConfigureServices(services);

        var storeDescriptors = services.Where(d => d.ServiceType == typeof(IEmailRecordStore)).ToList();
        Assert.Single(storeDescriptors);
        Assert.Equal(typeof(ConsumerEmailRecordStore), storeDescriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerSender()
    {
        var services = new ServiceCollection();
        services.AddScoped<IEmailSender, ConsumerEmailSender>();
        new EmailingExtensionInitializer<EmailingUserInfo>().ConfigureServices(services);

        var senderDescriptors = services.Where(d => d.ServiceType == typeof(IEmailSender)).ToList();
        Assert.Single(senderDescriptors);
        Assert.Equal(typeof(ConsumerEmailSender), senderDescriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_Registers_ScopedLifecycle()
    {
        var services = new ServiceCollection();
        new EmailingExtensionInitializer<EmailingUserInfo>().ConfigureServices(services);

        var storeDesc = services.First(d => d.ServiceType == typeof(IEmailRecordStore));
        var senderDesc = services.First(d => d.ServiceType == typeof(IEmailSender));
        Assert.Equal(ServiceLifetime.Scoped, storeDesc.Lifetime);
        Assert.Equal(ServiceLifetime.Scoped, senderDesc.Lifetime);
    }

    /// <summary>测试专用 IEmailRecordStore：标记消费方自定义实现。</summary>
    private sealed class ConsumerEmailRecordStore : IEmailRecordStore
    {
        public Task<EmailRecordEntity?> GetAsync(long id, CancellationToken ct = default)
            => Task.FromResult<EmailRecordEntity?>(null);

        public Task<IReadOnlyList<EmailRecordEntity>> GetListAsync(string? status = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmailRecordEntity>>(Array.Empty<EmailRecordEntity>());

        public Task SaveAsync(EmailRecordEntity entity, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>测试专用 IEmailSender：标记消费方自定义实现。</summary>
    private sealed class ConsumerEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
