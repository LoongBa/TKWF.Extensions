using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKWF.Ext.Approval;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Approval.Tests;

/// <summary>
/// D11：Initializer DI 注册测试（V0.3.0 领域自治根治重写）——覆盖 [TKWFExtension] 特性声明 +
/// <c>AddConstructibleService</c> 注册形态（接口可构造守卫工厂 + 实现类 throw-factory）+
/// CurrentAopUser 域作用域守卫（域作用域外解析即抛）+ Resolver TryAddEnumerable 多实现集合语义。
/// <para>V0.3.0（ADR90 正确路线）：三标准门面（IApprovalService/ApprovalManager、IApprovalQueryService/ApprovalQueryService、
/// IApprovalTimeoutService/ApprovalTimeoutService）注册形态由 TryAddScoped 改 <c>AddConstructibleService</c>——
/// 消费方统一经 <c>User.Use&lt;接口&gt;()</c> 解析（AOP 路径先设 CurrentAopUser 再 GetRequiredService）。
/// Resolver（IApprovalAssigneeResolver/DefaultApprovalAssigneeResolver）保持 TryAddEnumerable（多实现集合——消费方可追加自定义）。</para>
/// </summary>
public class ApprovalExtensionInitializerTests
{
    [Fact]
    public void ShouldHaveTKWFExtensionAttribute()
    {
        var attr = typeof(ApprovalExtensionInitializer<>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .FirstOrDefault() as TKWFExtensionAttribute;

        Assert.NotNull(attr);
        Assert.Equal("Approval", attr!.Name);
    }

    // ── AddConstructibleService 注册形态：接口可构造守卫工厂（非实现映射）──

    [Fact]
    public void ConfigureServices_Registers_IApprovalService_GuardFactoryDescriptor()
    {
        var services = new ServiceCollection();
        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IApprovalService));
        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IApprovalQueryService_GuardFactoryDescriptor()
    {
        var services = new ServiceCollection();
        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IApprovalQueryService));
        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_IApprovalTimeoutService_GuardFactoryDescriptor()
    {
        var services = new ServiceCollection();
        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IApprovalTimeoutService));
        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    // ── AddConstructibleService 注册形态：实现类 throw-factory（禁直接 DI 解析）──

    [Fact]
    public void ConfigureServices_Registers_ApprovalManager_ThrowFactory()
    {
        var services = new ServiceCollection();
        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ApprovalManager));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ApprovalManager>());
        Assert.Contains("领域架构守卫", ex.Message);
    }

    [Fact]
    public void ConfigureServices_Registers_ApprovalQueryService_ThrowFactory()
    {
        var services = new ServiceCollection();
        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ApprovalQueryService));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ApprovalQueryService>());
        Assert.Contains("领域架构守卫", ex.Message);
    }

    [Fact]
    public void ConfigureServices_Registers_ApprovalTimeoutService_ThrowFactory()
    {
        var services = new ServiceCollection();
        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ApprovalTimeoutService));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<ApprovalTimeoutService>());
        Assert.Contains("领域架构守卫", ex.Message);
    }

    // ── CurrentAopUser 域作用域守卫：域作用域外解析接口即抛（DI004 运行期兜底）──

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws_ForIApprovalService()
    {
        var services = new ServiceCollection();
        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IApprovalService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IApprovalService", ex.Message);
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws_ForIApprovalQueryService()
    {
        var services = new ServiceCollection();
        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IApprovalQueryService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IApprovalQueryService", ex.Message);
    }

    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws_ForIApprovalTimeoutService()
    {
        var services = new ServiceCollection();
        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IApprovalTimeoutService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IApprovalTimeoutService", ex.Message);
    }

    // ── Resolver：TryAddEnumerable 多实现集合（消费方可追加自定义 resolver，默认不被覆盖）──

    [Fact]
    public void ConfigureServices_Registers_DefaultApprovalAssigneeResolver_Enumerable()
    {
        var services = new ServiceCollection();
        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // TryAddEnumerable 注册形态：DefaultApprovalAssigneeResolver 作为多实现集合元素
        var provider = services.BuildServiceProvider();
        var resolvers = provider.GetServices<IApprovalAssigneeResolver>().ToList();
        Assert.Contains(resolvers, r => r is DefaultApprovalAssigneeResolver);
    }

    [Fact]
    public void ConfigureServices_TryAddEnumerable_DoesNotOverrideConsumerCustomResolver()
    {
        // 消费方先注册自定义 resolver（TryAddEnumerable——多实现集合语义：追加而非覆盖）
        var services = new ServiceCollection();
        services.TryAddEnumerable(
            ServiceDescriptor.Scoped<IApprovalAssigneeResolver, CustomApprovalAssigneeResolver>());

        new ApprovalExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var provider = services.BuildServiceProvider();
        var resolvers = provider.GetServices<IApprovalAssigneeResolver>().ToList();
        // 自定义 resolver 保留（多实现集合：消费方可追加自定义 Resolver——Role→用户解析扩展点）
        Assert.Contains(resolvers, r => r is CustomApprovalAssigneeResolver);
        Assert.Contains(resolvers, r => r is DefaultApprovalAssigneeResolver);
    }

    // ── 消费者自定义 resolver 桩（Role→用户解析扩展点示例）──

    private sealed class CustomApprovalAssigneeResolver : IApprovalAssigneeResolver
    {
        public Task<IReadOnlyList<string>> ResolveUserIdsAsync(ApprovalStepDefinition step, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<string>>(["custom-user"]);
    }
}
