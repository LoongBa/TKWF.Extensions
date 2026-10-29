using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Ext.Federation;
using TKWF.Federation.WeChat;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// WeChatOperationsExtensionInitializer 测试——覆盖 [TKWFExtension] 特性声明、AddConstructibleService 注册形态
/// （6 接口构造工厂 + 实现类 throw-factory）、TryAddEnumerableConstructible handler 集合（ADR92）、
/// keyed ISymmetricKeyProvider（E4 密钥管理）、DbWeChatCredentialResolver 两源并存注册。
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

    /// <summary>6 门面接口全部注册为构造工厂（非实现映射）+ Scoped 生命周期（AddConstructibleService 形态）。</summary>
    [Theory]
    [InlineData(typeof(IChannelConfigService))]
    [InlineData(typeof(IMenuService))]
    [InlineData(typeof(IReplyRuleService))]
    [InlineData(typeof(IMaterialService))]
    [InlineData(typeof(ISubscriptionStatService))]
    [InlineData(typeof(ILogQueryService))]
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

    /// <summary>6 门面实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use&lt;接口&gt;() 创建。</summary>
    [Theory]
    [InlineData(typeof(ChannelConfigService))]
    [InlineData(typeof(MenuService))]
    [InlineData(typeof(ReplyRuleService))]
    [InlineData(typeof(MaterialService))]
    [InlineData(typeof(SubscriptionStatService))]
    [InlineData(typeof(LogQueryService))]
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

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IChannelConfigService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IChannelConfigService", ex.Message);
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

    /// <summary>keyed ISymmetricKeyProvider 注册（E4——键 WeChatOperations；FileSymmetricKeyProvider 构造即加载）。</summary>
    [Fact]
    public void ConfigureServices_Registers_KeyedSymmetricKeyProvider()
    {
        var services = new ServiceCollection();
        new WeChatOperationsExtensionInitializer<WeChatOperationsUserInfo>().ConfigureServices(services);
        services.AddLogging();
        // ⚠️ 测试环境开发模式（FileSymmetricKeyProvider 随机兜底密钥——生产模式缺密钥文件 fail-fast）
        services.AddOptions<WeChatOperationsOptions>().Configure(o => o.IsProduction = false);
        var provider = services.BuildServiceProvider();

        var keys = provider.GetRequiredKeyedService<ISymmetricKeyProvider>(WeChatOperationsKeyProviderKeys.WeChatOperations);
        Assert.NotNull(keys);

        // 开发模式随机兜底密钥可用（IsProduction=false 默认——测试不配密钥文件）
        var cipher = keys.Encrypt("roundtrip-plain");
        Assert.Equal("roundtrip-plain", keys.Decrypt(cipher));
    }

    /// <summary>DbWeChatCredentialResolver 两源并存注册（TryAddEnumerable Scoped——契约非 IDomainService 普通 DI 集合）。</summary>
    [Fact]
    public void ConfigureServices_Registers_DbCredentialResolver_Collection()
    {
        var services = new ServiceCollection();
        new WeChatOperationsExtensionInitializer<WeChatOperationsUserInfo>().ConfigureServices(services);
        services.AddOptions<WeChatOperationsOptions>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IWeChatCredentialResolver)
            && d.ImplementationType == typeof(DbWeChatCredentialResolver));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    /// <summary>WeChatChannelRegistrySource 两源并存注册（D03 1.7——TryAddEnumerable 集合元素；Priority 语义断言归 Federation.Tests 组合测试）。</summary>
    [Fact]
    public void ConfigureServices_Registers_DbChannelSource()
    {
        var services = new ServiceCollection();
        new WeChatOperationsExtensionInitializer<WeChatOperationsUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IChannelSource)
            && d.ImplementationType == typeof(WeChatChannelRegistrySource));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
        // Priority 契约存在（DIM 可覆盖——D03 §8.2 优先权语义）
        Assert.NotNull(typeof(WeChatChannelRegistrySource).GetProperty(nameof(WeChatChannelRegistrySource.Priority)));
    }
}
