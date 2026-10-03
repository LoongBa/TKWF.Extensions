using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Emailing.Tests;

/// <summary>
/// EmailingExtensionInitializer 测试——覆盖 [TKWFExtension] 特性声明、DI 注册形态（V4.10.53 领域自治整改：
/// IEmailRecordStore = AddConstructibleService 守卫工厂 + throw-factory + 域外解析抛；IEmailSender = 接线型 TryAddScoped）。
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
    public void ConfigureServices_Registers_IEmailRecordStore_GuardFactory()
    {
        // V4.10.53：IEmailRecordStore（接口 : IDomainService）→ AddConstructibleService——接口注册为可构造守卫工厂
        // （ImplementationFactory 非 ImplementationType 映射——DI004 运行期兜底）
        var services = new ServiceCollection();
        new EmailingExtensionInitializer<EmailingUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailRecordStore));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_EmailRecordStore_ThrowFactory()
    {
        // AddConstructibleService：实现类注册为 throw-factory（禁止直接 DI 解析——必须经 User.Use<接口>()）
        var services = new ServiceCollection();
        new EmailingExtensionInitializer<EmailingUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(EmailRecordStore));

        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);
        using var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<EmailRecordStore>());
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        // AddConstructibleService 的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new EmailingExtensionInitializer<EmailingUserInfo>().ConfigureServices(services);
        using var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IEmailRecordStore>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IEmailRecordStore", ex.Message);
    }

    [Fact]
    public void ConfigureServices_Registers_IEmailSender_Descriptor()
    {
        // V4.10.53：IEmailSender（Abstractions 契约非 IDomainService）→ 接线型 TryAddScoped 普通 DI（ImplementationType 映射）
        var services = new ServiceCollection();
        new EmailingExtensionInitializer<EmailingUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEmailSender));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(SmtpEmailSender), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerSender()
    {
        // IEmailSender 保持 TryAddScoped——消费方自定义实现优先
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

    /// <summary>测试专用 IEmailSender：标记消费方自定义实现。</summary>
    private sealed class ConsumerEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
