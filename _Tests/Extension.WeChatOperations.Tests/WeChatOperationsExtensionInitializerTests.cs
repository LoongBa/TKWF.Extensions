using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// WeChatOperationsExtensionInitializer 测试——覆盖 [TKWFExtension] 特性声明、AddConstructibleService 注册形态
/// （8 接口构造工厂 + 实现类 throw-factory）、TryAddEnumerableConstructible handler 集合（ADR92）。
/// <para>V0.9.0 拆分（2026-11-05）：通道面断言（IChannelConfigService/keyed 密钥/DbWeChatCredentialResolver/
/// WeChatChannelRegistrySource）随下沉迁 Extension.WeChatChannels.Tests——本类仅保留运营侧。</para>
/// <para>V4.10.53（领域自治根治）：消费方统一经 <c>User.Use&lt;接口&gt;()</c> 解析（AOP 路径先设
/// CurrentAopUser 再 GetRequiredService）。</para>
/// </summary>
public class WeChatOperationsExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(WeChatOperationsExtensionInitializer<WeChatOperationsUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("WeChatOperations", attr.Name);
    }

    /// <summary>运营门面接口全部注册为构造工厂（非实现映射）+ Scoped 生命周期（AddConstructibleService 形态；
    /// V0.9.0 拆分后不含 IChannelConfigService——下沉 WeChatChannels；V0.10.0 增 IWeChatMessageRouter——
    /// 进程内路由编排接口与 IMessageGatewayService 单实现双注册，同形态同断言）。</summary>
    [Theory]
    [InlineData(typeof(IMenuService))]
    [InlineData(typeof(IReplyRuleService))]
    [InlineData(typeof(IMaterialService))]
    [InlineData(typeof(ISubscriptionStatService))]
    [InlineData(typeof(ILogQueryService))]
    [InlineData(typeof(IMessageGatewayService))]
    [InlineData(typeof(IWeChatMessageRouter))]
    [InlineData(typeof(IMessageForwardDispatcher))]
    [InlineData(typeof(IWeChatOperationsStatusService))]
    public void ConfigureServices_Registers_Facade_FactoryDescriptor(Type facadeType)
    {
        var services = new ServiceCollection();
        new WeChatOperationsExtensionInitializer<WeChatOperationsUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == facadeType);

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>运营 8 门面实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use&lt;接口&gt;() 创建。</summary>
    [Theory]
    [InlineData(typeof(MenuService))]
    [InlineData(typeof(ReplyRuleService))]
    [InlineData(typeof(MaterialService))]
    [InlineData(typeof(SubscriptionStatService))]
    [InlineData(typeof(LogQueryService))]
    [InlineData(typeof(MessageGatewayService))]
    [InlineData(typeof(MessageForwardDispatcher))]
    [InlineData(typeof(WeChatOperationsStatusService))]
    public void ConfigureServices_Registers_Impl_ThrowFactory(Type implType)
    {
        var services = new ServiceCollection();
        new WeChatOperationsExtensionInitializer<WeChatOperationsUserInfo>().ConfigureServices(services);
        services.AddLogging();
        services.AddOptions<WeChatOperationsOptions>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == implType);
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService(implType));
    }

    /// <summary>接口构造工厂的 CurrentAopUser 守卫——非 User.Use&lt;T&gt;() 调用链内解析即抛（DI004 运行期兜底）。</summary>
    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        var services = new ServiceCollection();
        new WeChatOperationsExtensionInitializer<WeChatOperationsUserInfo>().ConfigureServices(services);
        services.AddLogging();
        services.AddOptions<WeChatOperationsOptions>();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IMenuService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IMenuService", ex.Message);
    }

    /// <summary>事件回复 handler 经 TryAddEnumerableConstructible 注册（集合版守卫工厂——工厂委托形态入集合）。</summary>
    [Fact]
    public void ConfigureServices_Registers_ReplyHandler_ConstructibleCollection()
    {
        var services = new ServiceCollection();
        new WeChatOperationsExtensionInitializer<WeChatOperationsUserInfo>().ConfigureServices(services);

        // IEventReplyHandler 集合元素以工厂委托形态注册（ADR92 集合版守卫工厂——帧内供给 IDomainUser）
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IEventReplyHandler)
            && d.ImplementationFactory is not null);
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    /// <summary>依赖标注——通道底座契约（WeChatChannels.Abstractions）编译期门控（V0.9.0 拆分：消费双白名单）。</summary>
    [Fact]
    public void DependencyAttributes_Declared_ChannelContract()
    {
        var deps = typeof(WeChatOperationsExtensionInitializer<WeChatOperationsUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionDependencyAttribute), false)
            .Cast<TKWFExtensionDependencyAttribute>()
            .ToList();

        Assert.Contains(deps, d => d.DependencyType == typeof(TKWF.Ext.WeChatChannels.IChannelConfigService));
    }
}