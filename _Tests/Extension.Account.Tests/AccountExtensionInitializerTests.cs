using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.AuthController;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// AccountExtensionInitializer 测试——[TKWFExtension] 特性声明、DI 注册、TryAddScoped 语义、主框架缺口注册。
/// </summary>
public class AccountExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(AccountExtensionInitializer<AccountUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Account", attr.Name);
    }

    [Fact]
    public void ConfigureServices_Registers_IAccountLockoutPolicy_Descriptor()
    {
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAccountLockoutPolicy));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(FreeSqlAccountLockoutPolicy), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IPasswordResetFlow_Descriptor()
    {
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPasswordResetFlow));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(DefaultPasswordResetFlow), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_Stores_Descriptors()
    {
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var lockoutStore = services.FirstOrDefault(d => d.ServiceType == typeof(IAccountLockoutStore));
        var resetStore = services.FirstOrDefault(d => d.ServiceType == typeof(IPasswordResetStore));

        Assert.NotNull(lockoutStore);
        Assert.Equal(typeof(FreeSqlAccountLockoutStore), lockoutStore!.ImplementationType);
        Assert.NotNull(resetStore);
        Assert.Equal(typeof(FreeSqlPasswordResetStore), resetStore!.ImplementationType);
    }

    [Fact]
    public void ConfigureServices_DoesNotRegister_PasswordManager()
    {
        // IAccountPasswordManager 由消费方实现——扩展不应注册默认实现
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAccountPasswordManager));

        Assert.Null(descriptor);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerLockoutPolicy()
    {
        var services = new ServiceCollection();
        services.AddScoped<IAccountLockoutPolicy, ConsumerLockoutPolicy>();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IAccountLockoutPolicy)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerLockoutPolicy), descriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerResetFlow()
    {
        var services = new ServiceCollection();
        services.AddScoped<IPasswordResetFlow, ConsumerResetFlow>();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IPasswordResetFlow)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerResetFlow), descriptors[0].ImplementationType);
    }

    /// <summary>测试专用 IAccountLockoutPolicy：标记消费方自定义实现。</summary>
    private sealed class ConsumerLockoutPolicy : IAccountLockoutPolicy
    {
        public Task<bool> IsLockedAsync(string userName, CancellationToken ct = default) => Task.FromResult(false);
        public Task OnFailedLoginAsync(string userName, CancellationToken ct = default) => Task.CompletedTask;
        public Task OnSuccessfulLoginAsync(string userName, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnlockAsync(string userName, CancellationToken ct = default) => Task.CompletedTask;
    }

    /// <summary>测试专用 IPasswordResetFlow：标记消费方自定义实现。</summary>
    private sealed class ConsumerResetFlow : IPasswordResetFlow
    {
        public Task<bool> InitiateResetAsync(string userName, CancellationToken ct = default) => Task.FromResult(false);
        public Task<ResetResult> CompleteResetAsync(string userName, string resetCode, string newClientHash, string salt, CancellationToken ct = default)
            => Task.FromResult(new ResetResult(false));
    }
}