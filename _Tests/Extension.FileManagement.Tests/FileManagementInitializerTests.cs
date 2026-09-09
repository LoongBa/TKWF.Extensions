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
/// （IFileManager/IFileFolderStore/IManagedFileStore/两 DataService/Options 绑定）、TryAddScoped 不覆盖、
/// 白名单声明（V4.9.85 ADR47）。
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

    [Fact]
    public void ConfigureServices_Registers_Manager_Descriptor()
    {
        var services = new ServiceCollection();
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IFileManager));

        Assert.NotNull(descriptor);
        // FileManager 构造函数 internal（多依赖注入）→ 工厂注册（对齐 Calendar D20 断言模式）
        Assert.NotNull(descriptor!.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

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
    public void ConfigureServices_Registers_BothDataServices()
    {
        var services = new ServiceCollection();
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        var folderDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(FileFolderEntityDataService));
        var fileDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ManagedFileEntityDataService));

        Assert.NotNull(folderDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, folderDescriptor!.Lifetime);
        Assert.NotNull(fileDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, fileDescriptor!.Lifetime);
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
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerManager()
    {
        var services = new ServiceCollection();
        services.AddScoped<IFileManager>(_ => throw new NotSupportedException("consumer marker"));
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(IFileManager)).ToList();
        Assert.Single(descriptors);
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