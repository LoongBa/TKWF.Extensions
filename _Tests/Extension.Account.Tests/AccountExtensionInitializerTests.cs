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
/// AccountExtensionInitializer 测试——[TKWFExtension] 特性声明、DI 注册形态、主框架缺口注册。
/// <para>V4.10.53（领域自治根治，正确路线）注册形态：</para>
/// <list type="bullet">
/// <item><see cref="IAccountLockoutStore"/> / <see cref="IPasswordResetStore"/>（接口 : IDomainService）→
///     <c>AddConstructibleService</c>：接口 = 可构造守卫工厂（非实现映射）+ 实现类 = throw-factory；
///     域作用域外（无 CurrentAopUser）直接 DI 解析接口必抛领域架构守卫；</item>
/// <item><see cref="IAccountLockoutPolicy"/> / <see cref="IPasswordResetFlow"/>（主框架扩展点，非 IDomainService）→
///     TryAddScoped 普通 DI（接线型）；<see cref="ILoginHistoryService"/> → TryAddScoped 接线型。</item>
/// </list>
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
    public void ConfigureServices_Registers_IAccountLockoutStore_GuardFactory()
    {
        // V4.10.53：AddConstructibleService——接口注册为可构造守卫工厂（非实现映射）
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IAccountLockoutStore));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IPasswordResetStore_GuardFactory()
    {
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IPasswordResetStore));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_StoreImplementations_ThrowFactory()
    {
        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<接口>() 创建
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var lockoutImpl = services.FirstOrDefault(d => d.ServiceType == typeof(AccountLockoutStore));
        var resetImpl = services.FirstOrDefault(d => d.ServiceType == typeof(PasswordResetStore));
        Assert.NotNull(lockoutImpl);
        Assert.NotNull(lockoutImpl!.ImplementationFactory);
        Assert.NotNull(resetImpl);
        Assert.NotNull(resetImpl!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<AccountLockoutStore>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<PasswordResetStore>());
    }

    [Fact]
    public void StoreInterfaceFactories_OutsideUseScope_Throw()
    {
        // AddConstructibleService 的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex1 = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IAccountLockoutStore>());
        Assert.Contains("领域架构守卫", ex1.Message);
        Assert.Contains("IAccountLockoutStore", ex1.Message);

        var ex2 = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IPasswordResetStore>());
        Assert.Contains("领域架构守卫", ex2.Message);
        Assert.Contains("IPasswordResetStore", ex2.Message);
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
    public void ConfigureServices_Registers_ILoginHistoryService_Descriptor()
    {
        // V0.3.0：登录历史与异常检测查询服务（消费 SecurityLog 扩展查询 API）——接线型 TryAddScoped
        var services = new ServiceCollection();
        new AccountExtensionInitializer<AccountUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ILoginHistoryService));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(LoginHistoryService), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
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