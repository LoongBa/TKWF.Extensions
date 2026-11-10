using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain.Exceptions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using TKWF.Federation.WeChat;

namespace TKWF.Ext.WeChatChannels.Tests;

/// <summary>
/// WeChatChannelsExtensionInitializer 测试——覆盖 [TKWFExtension] 特性声明、AddConstructibleService 注册形态
/// （门面构造工厂 + 实现类 throw-factory）、keyed ISymmetricKeyProvider（E4 密钥管理）、两源并存注册
/// （DbWeChatCredentialResolver / WeChatChannelRegistrySource）。2026-11-05 拆分下沉——原运营 Initializer
/// 测试中通道面断言迁此（code-reviewer #8 闭环）。
/// </summary>
public class WeChatChannelsExtensionInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(WeChatChannelsExtensionInitializer<TestUserInfo>)
            .GetCustomAttributes(typeof(TKW.Framework.Domain.TKWFExtensionAttribute), false)
            .Cast<TKW.Framework.Domain.TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("WeChatChannels", attr.Name);
    }

    /// <summary>门面接口注册为构造工厂（非实现映射）+ Scoped 生命周期（AddConstructibleService 形态）。</summary>
    [Fact]
    public void ConfigureServices_Registers_Facade_FactoryDescriptor()
    {
        var services = new ServiceCollection();
        new WeChatChannelsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IChannelConfigService));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    /// <summary>实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use&lt;接口&gt;() 创建。</summary>
    [Fact]
    public void ConfigureServices_Registers_Impl_ThrowFactory()
    {
        var services = new ServiceCollection();
        new WeChatChannelsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        services.AddOptions<WeChatChannelsOptions>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ChannelConfigService));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<DomainGuardException>(() => provider.GetRequiredService(typeof(ChannelConfigService)));
    }

    /// <summary>接口构造工厂的 CurrentAopUser 守卫——非 User.Use&lt;T&gt;() 调用链内解析即抛（DI004 运行期兜底）。</summary>
    [Fact]
    public void InterfaceFactory_OutsideUseScope_Throws()
    {
        var services = new ServiceCollection();
        new WeChatChannelsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        services.AddOptions<WeChatChannelsOptions>();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<DomainGuardException>(() => provider.GetRequiredService<IChannelConfigService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IChannelConfigService", ex.Message);
    }

    /// <summary>keyed ISymmetricKeyProvider 注册（E4——键 WeChatChannels；FileSymmetricKeyProvider 构造即加载）。</summary>
    [Fact]
    public void ConfigureServices_Registers_KeyedSymmetricKeyProvider()
    {
        var services = new ServiceCollection();
        new WeChatChannelsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        // ⚠️ 测试环境开发模式（FileSymmetricKeyProvider 随机兜底密钥——生产模式缺密钥文件 fail-fast）
        services.AddOptions<WeChatChannelsOptions>().Configure(o => o.IsProduction = false);
        var provider = services.BuildServiceProvider();

        var keys = provider.GetRequiredKeyedService<ISymmetricKeyProvider>(SymmetricKeyProviderKeys.WeChatChannels);
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
        new WeChatChannelsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddOptions<WeChatChannelsOptions>();

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IWeChatCredentialResolver)
            && d.ImplementationType == typeof(DbWeChatCredentialResolver));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
    }

    /// <summary>WeChatChannelRegistrySource 两源并存注册（TryAddEnumerable 集合元素——Priority 语义断言归 Federation.Tests 组合测试）。</summary>
    [Fact]
    public void ConfigureServices_Registers_DbChannelSource()
    {
        var services = new ServiceCollection();
        new WeChatChannelsExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IChannelSource)
            && d.ImplementationType == typeof(WeChatChannelRegistrySource));
        Assert.NotNull(descriptor);
        Assert.Equal(ServiceLifetime.Scoped, descriptor!.Lifetime);
        // Priority 契约存在（DIM 可覆盖——D03 §8.2 优先权语义）
        Assert.NotNull(typeof(WeChatChannelRegistrySource).GetProperty(nameof(WeChatChannelRegistrySource.Priority)));
    }
}
