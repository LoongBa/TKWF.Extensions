using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Emailing.Tests;

/// <summary>
/// EmailingExtensionInitializer 测试（A5 Store-debt cleanup 后）——IEmailSender 接线型 TryAddScoped 注册形态。
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
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerSender()
    {
        var services = new ServiceCollection();
        services.AddScoped<IEmailSender, ConsumerEmailSender>();
        new EmailingExtensionInitializer<EmailingUserInfo>().ConfigureServices(services);

        var senderDescriptors = services.Where(d => d.ServiceType == typeof(IEmailSender)).ToList();
        Assert.Single(senderDescriptors);
        Assert.Equal(typeof(ConsumerEmailSender), senderDescriptors[0].ImplementationType);
    }

    private sealed class ConsumerEmailSender : IEmailSender
    {
        public Task SendAsync(EmailMessage message, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
