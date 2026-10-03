using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings.Tests;

/// <summary>
/// SettingStore 测试——使用 SQLite 内存库验证真实读写 + 异常静默。
/// <para>SettingStore 经 SettingEntityDataService（FreeSqlEntityDAC + UnitOfWorkManager 驱动）委托持久化。</para>
/// </summary>
public class FreeSqlSettingStoreTests
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（每次调用新连接 = 独立内存库）。</summary>
    private static IFreeSql CreateInMemoryFreeSql()
    {
        return new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>构造 SettingEntityDataService——经真实 FreeSql DAC（UnitOfWorkManager + FreeSqlEntityDAC）驱动。</summary>
    private static SettingEntityDataService CreateDataService(IFreeSql fsql, IDomainUser? user = null)
    {
        var uowManager = new UnitOfWorkManager(fsql);
        var dac = new FreeSqlEntityDAC<SettingEntity>(uowManager);
        return new SettingEntityDataService(user ?? new StubDomainUser(), dac);
    }

    private static SettingStore CreateStore(IFreeSql fsql, FakeLogger<SettingStore> logger)
    {
        // ADR88/DI004：Store 构造不再注入 DataService——经 User.Use<SettingEntityDataService>() 懒加载
        var stub = new StubDomainUser();
        var services = new ServiceCollection();
        services.AddSingleton(CreateDataService(fsql, stub));
        stub.ServiceProvider = services.BuildServiceProvider();
        return new SettingStore(stub, logger);
    }

    [Fact]
    public async Task SetAsync_NewSetting_PersistsToDatabase()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<SettingStore>();
        var store = CreateStore(fsql, logger);

        // Act
        await store.SetAsync("Theme", "dark", "Global", null, "UI theme", CancellationToken.None);

        // Assert
        var list = await store.GetListAsync("Global", null, CancellationToken.None);
        Assert.Single(list);
        var saved = list[0];
        Assert.Equal("Theme", saved.Name);
        Assert.Equal("dark", saved.Value);
        Assert.Equal("Global", saved.ProviderName);
        Assert.Null(saved.ProviderKey);
        Assert.Equal("UI theme", saved.Description);
        Assert.True(saved.IsVisibleToClients);
    }

    [Fact]
    public async Task SetAsync_UpdateExisting_UpdatesValue()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<SettingStore>();
        var store = CreateStore(fsql, logger);

        // Act
        await store.SetAsync("Theme", "dark", "Global", null, null, CancellationToken.None);
        await store.SetAsync("Theme", "light", "Global", null, null, CancellationToken.None);

        // Assert
        var list = await store.GetListAsync("Global", null, CancellationToken.None);
        Assert.Single(list);
        var saved = list[0];
        Assert.Equal("light", saved.Value);
    }

    [Fact]
    public async Task GetAsync_ExistingSetting_ReturnsEntity()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<SettingStore>();
        var store = CreateStore(fsql, logger);

        await store.SetAsync("Language", "zh-CN", "Global", null, null, CancellationToken.None);

        // Act
        var result = await store.GetAsync("Language", "Global", null, CancellationToken.None);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Language", result!.Name);
        Assert.Equal("zh-CN", result.Value);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<SettingStore>();
        var store = CreateStore(fsql, logger);

        // Act
        var result = await store.GetAsync("NotExist", "Global", null, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task DeleteAsync_ExistingSetting_RemovesFromDatabase()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<SettingStore>();
        var store = CreateStore(fsql, logger);

        await store.SetAsync("Temp", "value", "Global", null, null, CancellationToken.None);
        Assert.Single(await store.GetListAsync("Global", null, CancellationToken.None));

        // Act
        await store.DeleteAsync("Temp", "Global", null, CancellationToken.None);

        // Assert
        Assert.Empty(await store.GetListAsync("Global", null, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteAsync_NonExistent_DoesNotThrow()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<SettingStore>();
        var store = CreateStore(fsql, logger);

        // Act & Assert — should not throw
        await store.DeleteAsync("NotExist", "Global", null, CancellationToken.None);
    }

    [Fact]
    public async Task GetListAsync_MultipleSettings_ReturnsAll()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<SettingStore>();
        var store = CreateStore(fsql, logger);

        await store.SetAsync("A", "1", "Global", null, null, CancellationToken.None);
        await store.SetAsync("B", "2", "Global", null, null, CancellationToken.None);
        await store.SetAsync("C", "3", "Global", null, null, CancellationToken.None);

        // Act
        var list = await store.GetListAsync("Global", null, CancellationToken.None);

        // Assert
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task GetListAsync_EmptyProvider_ReturnsEmpty()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<SettingStore>();
        var store = CreateStore(fsql, logger);

        await store.SetAsync("A", "1", "Global", null, null, CancellationToken.None);

        // Act
        var list = await store.GetListAsync("Tenant", null, CancellationToken.None);

        // Assert
        Assert.Empty(list);
    }

    [Fact]
    public void Constructor_NullUser_Throws()
    {
        var logger = new FakeLogger<SettingStore>();
        Assert.Throws<ArgumentNullException>(() => new SettingStore(null!, logger));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        Assert.Throws<ArgumentNullException>(() => new SettingStore(new StubDomainUser(), null!));
    }

    [Fact]
    public async Task SetAsync_ExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        // Arrange — 使用已 Dispose 的 FreeSql，操作必定抛异常
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<SettingStore>();
        var store = CreateStore(fsql, logger);

        fsql.Dispose();

        // Act — should not throw
        await store.SetAsync("X", "Y", "Global", null, null, CancellationToken.None);

        // Assert — logger captured warning
        Assert.Single(logger.Warnings);
        Assert.Contains("设置写入失败", logger.Warnings[0]);
    }

    [Fact]
    public async Task GetAsync_ExceptionThrown_LogsWarningAndReturnsNull()
    {
        // Arrange — 使用已 Dispose 的 FreeSql
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<SettingStore>();
        var store = CreateStore(fsql, logger);

        fsql.Dispose();

        // Act — should not throw
        var result = await store.GetAsync("X", "Global", null, CancellationToken.None);

        // Assert
        Assert.Null(result);
        Assert.Single(logger.Warnings);
    }

    [Fact]
    public async Task SetAsync_NullValue_PersistsNull()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<SettingStore>();
        var store = CreateStore(fsql, logger);

        // Act
        await store.SetAsync("NullVal", null, "Global", null, null, CancellationToken.None);

        // Assert
        var saved = await store.GetAsync("NullVal", "Global", null, CancellationToken.None);
        Assert.Null(saved!.Value);
    }

    // ── Test helpers ──

    /// <summary>最小 IDomainUser 桩——仅满足编译，不提供真实用户上下文。
    /// <para>ADR88/DI004：Use&lt;T&gt;()/GetService&lt;T&gt;() 从注入的 ServiceProvider 容器解析
    /// （测试工厂经 <see cref="ServiceProvider"/> 注入；未注入时保留原抛 NotSupportedException 语义）。</para></summary>
    private sealed class StubDomainUser : IDomainUser
    {
        private IServiceProvider? _provider;
        private readonly object _gate = new();
        private readonly Dictionary<Type, object?> _cache = new();

        /// <summary>ServiceProvider（测试工厂注册时注入——懒加载 Use&lt;T&gt; 解析源，线程安全）。</summary>
        public IServiceProvider ServiceProvider
        {
            set { lock (_gate) _provider = value; }
        }

        public string SessionKey => "test-session";
        public bool IsAuthenticated => false;
        public bool IsSystemActor => false;
        public IUserInfo? UserInfo => null;
        public long? TenantId => null;
        public bool IsNoAuditActive => false;
        public string? UserId => null;
        public string? UserName => null;
        public bool IsInRole(string role) => false;
        public TDomainService Use<TDomainService>() where TDomainService : IDomainService
        {
            IServiceProvider provider;
            lock (_gate) provider = _provider ?? throw new NotSupportedException("Stub: Use<T> not supported in unit tests");
            if (_cache.TryGetValue(typeof(TDomainService), out var cached) && cached is TDomainService svc)
                return svc;
            lock (_gate)
            {
                if (_cache.TryGetValue(typeof(TDomainService), out cached) && cached is TDomainService svc2)
                    return svc2;
                var resolved = provider.GetRequiredService<TDomainService>();
                _cache[typeof(TDomainService)] = resolved;
                return resolved;
            }
        }
        public TService GetService<TService>() where TService : notnull
        {
            IServiceProvider provider;
            lock (_gate) provider = _provider ?? throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");
            return provider.GetRequiredService<TService>();
        }
        public TService GetOptionalService<TService>() where TService : class => null!;
        public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
    }

    /// <summary>简化 ILogger 桩：捕获 Warning 日志。</summary>
    private sealed class FakeLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
