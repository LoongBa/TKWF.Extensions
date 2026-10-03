using System;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKWF.Ext.FileManagement;

namespace TKWF.Ext.FileManagement.Tests;

/// <summary>
/// D18：FileManagementExtensionInitializer 接线测试——[TKWFExtension] 特性声明、DI 注册完整
/// （IFileManager/IFileFolderStore/IManagedFileStore/DataService/Options 绑定）、TryAddScoped 不覆盖（Store）、
/// 白名单声明（V4.9.85 ADR47）。
/// <para>V4.10.53（领域自治根治，ADR90，正确路线）注册形态三态：</para>
/// <list type="bullet">
/// <item><see cref="IFileManager"/>（接口 : IDomainService）→ <b>AddConstructibleService</b>：接口 = 可构造守卫工厂
///     （非实现映射）+ 实现类 = throw-factory；域作用域外直接 DI 解析接口必抛领域架构守卫。</item>
/// <item><see cref="IFileFolderStore"/> / <see cref="IManagedFileStore"/> / <see cref="IManagedFileVersionStore"/>
///     （internal 接线型契约）→ <b>TryAddScoped 普通 DI</b>（ImplementationType + 消费方自定义优先）。</item>
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
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<FileManager>());
    }

    [Fact]
    public void ManagerInterface_OutsideUseScope_Throws()
    {
        // AddConstructibleService 的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IFileManager>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("IFileManager", ex.Message);
    }

    // ── Store：内部接线型 TryAddScoped（ImplementationType 普通 DI）──

    [Fact]
    public void ConfigureServices_Registers_FolderStore_Descriptor()
    {
        var services = new ServiceCollection();
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFileFolderStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(FileFolderStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_FileStore_Descriptor()
    {
        var services = new ServiceCollection();
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IManagedFileStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(ManagedFileStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_VersionStore_Descriptor()
    {
        var services = new ServiceCollection();
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IManagedFileVersionStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(ManagedFileVersionStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
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
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerFolderStore()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFileFolderStore>(_ => throw new NotSupportedException("consumer marker"));
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IFileFolderStore)).ToList();
        Assert.Single(descriptors);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerFileStore()
    {
        var services = new ServiceCollection();
        services.AddScoped<IManagedFileStore>(_ => throw new NotSupportedException("consumer marker"));
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IManagedFileStore)).ToList();
        Assert.Single(descriptors);
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
