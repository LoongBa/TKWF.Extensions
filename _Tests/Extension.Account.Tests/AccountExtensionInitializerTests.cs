using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.AuthController;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Account.Tests;

/// <summary>
/// AccountExtensionInitializer 测试——[TKWFExtension] 特性声明、DI 注册形态、主框架缺口注册。
/// <para>A5 Store-debt cleanup：IAccountLockoutStore/IPasswordResetStore 已删除（DataService 薄包装伪层）——
/// Policy/Flow 直接经 IServiceProvider 解析 SG1 DataService。</para>
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
    public void ConfigureServices_DoesNotRegister_PasswordManager()
    {
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAccountPasswordManager));

        Assert.Null(descriptor);
    }

    [Fact]
    public void ConfigureServices_Registers_ILoginHistoryService_GuardFactory()
    {
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ILoginHistoryService));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void LoginHistoryServiceGuardFactory_OutsideUseScope_Throws()
    {
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);
        services.AddLogging();

        var implDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(LoginHistoryService));
        Assert.NotNull(implDescriptor);
        Assert.NotNull(implDescriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<TKW.Framework.Domain.Exceptions.DomainGuardException>(
            () => provider.GetRequiredService<ILoginHistoryService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("ILoginHistoryService", ex.Message);
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

    private sealed class ConsumerLockoutPolicy : IAccountLockoutPolicy
    {
        public Task<bool> IsLockedAsync(string userName, CancellationToken ct = default) => Task.FromResult(false);
        public Task OnFailedLoginAsync(string userName, CancellationToken ct = default) => Task.CompletedTask;
        public Task OnSuccessfulLoginAsync(string userName, CancellationToken ct = default) => Task.CompletedTask;
        public Task UnlockAsync(string userName, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class ConsumerResetFlow : IPasswordResetFlow
    {
        public Task<bool> InitiateResetAsync(string userName, CancellationToken ct = default) => Task.FromResult(false);
        public Task<ResetResult> CompleteResetAsync(string userName, string resetCode, string newClientHash, string salt, CancellationToken ct = default)
            => Task.FromResult(new ResetResult(false));
    }
}
