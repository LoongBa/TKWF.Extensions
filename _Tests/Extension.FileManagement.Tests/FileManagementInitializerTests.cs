using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKWF.Ext.FileManagement;

namespace TKWF.Ext.FileManagement.Tests;

/// <summary>
/// D18：FileManagementExtensionInitializer 接线测试——[TKWFExtension] 特性声明、DI 注册完整
/// （IFileManager/DataService/Options 绑定）、白名单声明（V4.9.85 ADR47）。
/// A5 Store-debt 清理后 3 个 thin Store 伪层已删，门面经 User.Use&lt;XxxDataService&gt;() 直调。
/// <para>V4.10.53（领域自治根治，ADR90，正确路线）注册形态三态：</para>
/// <list type="bullet">
/// <item><see cref="IFileManager"/>（接口 : IDomainService）→ <b>AddConstructibleService</b>：接口 = 可构造守卫工厂
///     （非实现映射）+ 实现类 = throw-factory；域作用域外直接 DI 解析接口必抛领域架构守卫。</item>
/// <item>数据访问经门面内 <c>User.Use&lt;XxxEntityDataService&gt;()</c> 懒加载（SG1 ADR61 自动注册，无 Store 层）。</item>
/// </list>
/// </summary>
public class FileManagementInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(FileManagementExtensionInitializer<FileManagementUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("FileManagement", attr!.Name);
    }

    // ── IFileManager：AddConstructibleService（标准门面）──

    [Fact]
    public void ConfigureServices_Registers_Manager_GuardFactory()
    {
        var services = new ServiceCollection();
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        // V4.10.53：AddConstructibleService——接口注册为可构造守卫工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFileManager));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_Manager_ThrowFactory()
    {
        var services = new ServiceCollection();
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<IFileManager>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(FileManager));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<DomainGuardException>(() => provider.GetRequiredService<FileManager>());
    }

    [Fact]
    public void ManagerInterface_OutsideUseScope_Throws()
    {
        // AddConstructibleService 的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<DomainGuardException>(() => provider.GetRequiredService<IFileManager>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IFileManager", ex.Message);
    }

    // ── A5 Store-debt 清理回归：Initializer 不再注册任何 *Store 伪层 ──

    [Fact]
    public void ConfigureServices_DoesNotRegister_AnyStore()
    {
        var services = new ServiceCollection();
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        Assert.DoesNotContain(services, d => d.ServiceType.Name.EndsWith("Store", StringComparison.Ordinal));
    }

    [Fact]
    public void ConfigureServices_Registers_BothDataServices()
    {
        // v4.10.8 (ADR61)：Initializer 不再手动注册 DataService——SG 基类类型判定生成，
        // 经扩展 ProjectMetaContext.GetServiceRegistrations() 暴露（消费方聚合自动注册为可构造工厂）。
        var regs = TKWF.Ext.FileManagement.Generated.ProjectMetaContext.GetOrCreateInstance()
            .GetServiceRegistrations().ToList();

        Assert.Contains(regs, r => r.Implementation == typeof(FileFolderEntityDataService));
        Assert.Contains(regs, r => r.Implementation == typeof(ManagedFileEntityDataService));
        Assert.Contains(regs, r => r.Implementation == typeof(ManagedFileVersionEntityDataService));
    }

    [Fact]
    public void ConfigureServices_Registers_Options_Binding()
    {
        var services = new ServiceCollection();
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        // AddOptions<FileManagementOptions>() 注册开放式泛型 IOptions<>（UnnamedOptionsManager）+ IConfigureOptions
        // （BindConfiguration 的配置动作）；闭合 IOptions<FileManagementOptions> 由 Options 解析时物化。
        var openDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IOptions<>));
        Assert.NotNull(openDescriptor);
        var configureDescriptor = services.FirstOrDefault(d =>
            d.ServiceType == typeof(IConfigureOptions<FileManagementOptions>));
        Assert.NotNull(configureDescriptor);
    }

    [Fact]
    public void ConsumerHostInitializer_Declares_EnabledExtensionWhitelist()
    {
        var attr = typeof(ConsumerHostInitializer)
            .GetCustomAttributes(typeof(TKWFEnabledExtensionAttribute), false)
            .Cast<TKWFEnabledExtensionAttribute>()
            .FirstOrDefault();

        // V4.9.85 (ADR47)：消费方白名单声明——发现不自动启用，须显式声明三钩子才接线
        Assert.NotNull(attr);
        Assert.Equal(typeof(FileManagementExtensionInitializer<>), attr!.InitializerType);
    }
}
