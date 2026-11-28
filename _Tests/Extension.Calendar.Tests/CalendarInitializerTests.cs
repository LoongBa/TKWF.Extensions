using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKWF.Ext.Calendar;

namespace TKWF.Ext.Calendar.Tests;

/// <summary>
/// D20：CalendarExtensionInitializer 接线测试——[TKWFExtension] 特性声明、DI 注册完整、
/// TryAddScoped 不覆盖消费方自定义实现、白名单声明（V4.9.85 ADR47）。
/// <para>V4.10.53（领域自治根治，ADR90，正确路线）注册形态二态：</para>
/// <list type="bullet">
/// <item><see cref="ICalendarManager"/>（接口 : IDomainService）→ <b>AddConstructibleService</b>：接口 = 可构造守卫工厂
///     （非实现映射）+ 实现类 = throw-factory；域作用域外直接 DI 解析接口必抛领域架构守卫。</item>
/// <item>A5 Store-debt cleanup：ICalendarStore/CalendarStore 已删除（DataService 薄包装伪层）。</item>
/// </list>
/// </summary>
public class CalendarInitializerTests
{
    [Fact]
    public void ExtensionAttribute_Declared()
    {
        var attr = typeof(CalendarExtensionInitializer<CalendarUserInfo>)
            .GetCustomAttributes(typeof(TKWFExtensionAttribute), false)
            .Cast<TKWFExtensionAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.Equal("Calendar", attr!.Name);
    }

    // ── ICalendarManager：AddConstructibleService（标准门面）──

    [Fact]
    public void ConfigureServices_Registers_Manager_GuardFactory()
    {
        var services = new ServiceCollection();
        new CalendarExtensionInitializer<CalendarUserInfo>().ConfigureServices(services);

        // V4.10.53：AddConstructibleService——接口注册为可构造守卫工厂（非实现映射），Scoped 生命周期
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICalendarManager));

        Assert.NotNull(descriptor);
        Assert.Null(descriptor!.ImplementationType);
        Assert.NotNull(descriptor.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_Manager_ThrowFactory()
    {
        var services = new ServiceCollection();
        new CalendarExtensionInitializer<CalendarUserInfo>().ConfigureServices(services);

        // 实现类注册为 throw-factory——禁止直接 DI 解析，必须经 User.Use<ICalendarManager>() 创建
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(CalendarManager));
        Assert.NotNull(descriptor);
        Assert.NotNull(descriptor!.ImplementationFactory);

        var provider = services.BuildServiceProvider();
        Assert.Throws<DomainGuardException>(() => provider.GetRequiredService<CalendarManager>());
    }

    [Fact]
    public void ManagerInterface_OutsideUseScope_Throws()
    {
        // AddConstructibleService 的 CurrentAopUser 守卫——非 User.Use<T>() 调用链内解析即抛（DI004 运行期兜底）
        var services = new ServiceCollection();
        new CalendarExtensionInitializer<CalendarUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<DomainGuardException>(() => provider.GetRequiredService<ICalendarManager>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("ICalendarManager", ex.Message);
    }

    [Fact]
    public void ConfigureServices_Registers_BothDataServices()
    {
        // v4.10.8 (ADR61)：Initializer 不再手动注册 DataService——SG 基类类型判定生成，
        // 经扩展 ProjectMetaContext.GetServiceRegistrations() 暴露（消费方聚合自动注册为可构造工厂）。
        var regs = TKWF.Ext.Calendar.Generated.ProjectMetaContext.GetOrCreateInstance()
            .GetServiceRegistrations().ToList();

        Assert.Contains(regs, r => r.Implementation == typeof(CalendarEntityDataService));
        Assert.Contains(regs, r => r.Implementation == typeof(CalendarEventEntityDataService));
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
        Assert.Equal(typeof(CalendarExtensionInitializer<>), attr!.InitializerType);
    }

}
