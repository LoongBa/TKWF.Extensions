using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKWF.Ext.Calendar;

namespace TKWF.Ext.Calendar.Tests;

/// <summary>
/// D20：CalendarExtensionInitializer 接线测试——[TKWFExtension] 特性声明、DI 注册完整、
/// TryAddScoped 不覆盖消费方自定义实现、白名单声明（V4.9.85 ADR47）。
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

    [Fact]
    public void ConfigureServices_Registers_Manager_Descriptor()
    {
        var services = new ServiceCollection();
        new CalendarExtensionInitializer<CalendarUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICalendarManager));

        Assert.NotNull(descriptor);
        // CalendarManager 构造函数 internal（ICalendarStore 为 internal 契约）→ 工厂注册；
        // TryAddScoped 工厂语义与类型注册等价（消费方自定义实现仍优先）
        Assert.NotNull(descriptor!.ImplementationFactory);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_Store_Descriptor()
    {
        var services = new ServiceCollection();
        new CalendarExtensionInitializer<CalendarUserInfo>().ConfigureServices(services);

        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(ICalendarStore));

        Assert.NotNull(descriptor);
        Assert.Equal(typeof(CalendarStore), descriptor!.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, descriptor.Lifetime);
    }

    [Fact]
    public void ConfigureServices_Registers_BothDataServices()
    {
        var services = new ServiceCollection();
        new CalendarExtensionInitializer<CalendarUserInfo>().ConfigureServices(services);

        var calDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(CalendarEntityDataService));
        var evtDescriptor = services.FirstOrDefault(d => d.ServiceType == typeof(CalendarEventEntityDataService));

        Assert.NotNull(calDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, calDescriptor!.Lifetime);
        Assert.NotNull(evtDescriptor);
        Assert.Equal(ServiceLifetime.Scoped, evtDescriptor!.Lifetime);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerManager()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICalendarManager, ConsumerCalendarManager>();
        new CalendarExtensionInitializer<CalendarUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(ICalendarManager)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerCalendarManager), descriptors[0].ImplementationType);
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerStore()
    {
        var services = new ServiceCollection();
        services.AddScoped<ICalendarStore, ConsumerCalendarStore>();
        new CalendarExtensionInitializer<CalendarUserInfo>().ConfigureServices(services);

        var descriptors = services.Where(d => d.ServiceType == typeof(ICalendarStore)).ToList();
        Assert.Single(descriptors);
        Assert.Equal(typeof(ConsumerCalendarStore), descriptors[0].ImplementationType);
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

    /// <summary>测试专用 ICalendarManager：标记消费方自定义实现（仅 DI 标记，不实际调用）。</summary>
    private sealed class ConsumerCalendarManager : ICalendarManager
    {
        public Task<CalendarEntity> CreateCalendarAsync(string code, string name, string? description = null, string? color = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateCalendarAsync(long id, string? name = null, string? description = null, string? color = null, bool? isEnabled = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteCalendarAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CalendarEntity> GetCalendarAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CalendarEntity>> GetCalendarsAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CalendarEventEntity> CreateEventAsync(long calendarId, string title, DateTime startUtc, DateTime? endUtc = null, string? description = null, string? location = null, bool allDay = false, string? recurrenceRule = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateEventAsync(long id, long? calendarId = null, string? title = null, DateTime? startUtc = null, DateTime? endUtc = null, string? description = null, string? location = null, bool? allDay = null, string? recurrenceRule = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteEventAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<EventOccurrenceList> GetOccurrencesAsync(long? calendarId, DateTime fromUtc, DateTime toUtc, int maxCount = 1000, CancellationToken ct = default) => throw new NotImplementedException();
    }

    /// <summary>测试专用 ICalendarStore：标记消费方自定义实现（仅 DI 标记，不实际调用）。</summary>
    private sealed class ConsumerCalendarStore : ICalendarStore
    {
        public Task<long> CreateAsync(CalendarEntity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateAsync(CalendarEntity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CalendarEntity?> GetByIdAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CalendarEntity?> GetByCodeAsync(string code, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CalendarEntity>> GetAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
        public Task<CalendarEventEntity?> GetEventByIdAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CalendarEventEntity>> GetSingleByRangeAsync(long? calendarId, DateTime? fromUtc, DateTime? toUtc, int skip, int take, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CalendarEventEntity>> GetRecurringByRangeAsync(long? calendarId, DateTime? fromUtc, DateTime? toUtc, int skip, int take, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<CalendarEventEntity>> GetByCalendarIdAsync(long calendarId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CountByCalendarIdAsync(long calendarId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<long> CreateEventAsync(CalendarEventEntity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UpdateEventAsync(CalendarEventEntity entity, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeleteEventAsync(long id, CancellationToken ct = default) => throw new NotImplementedException();
    }
}
