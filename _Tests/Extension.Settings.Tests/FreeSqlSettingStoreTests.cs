using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
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
    private static SettingEntityDataService CreateDataService(IFreeSql fsql)
    {
        var uowManager = new UnitOfWorkManager(fsql);
        var dac = new FreeSqlEntityDAC<SettingEntity>(uowManager);
        return new SettingEntityDataService(new StubDomainUser(), dac);
    }

    private static SettingStore CreateStore(IFreeSql fsql, FakeLogger<SettingStore> logger)
        => new(CreateDataService(fsql), logger);

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
        var count = fsql.Select<SettingEntity>().Count();
        Assert.Equal(1, count);

        var saved = fsql.Select<SettingEntity>().First();
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
        var count = fsql.Select<SettingEntity>().Count();
        Assert.Equal(1, count);

        var saved = fsql.Select<SettingEntity>().First();
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
        Assert.Equal(1, fsql.Select<SettingEntity>().Count());

        // Act
        await store.DeleteAsync("Temp", "Global", null, CancellationToken.None);

        // Assert
        Assert.Equal(0, fsql.Select<SettingEntity>().Count());
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
    public void Constructor_NullDataService_Throws()
    {
        var logger = new FakeLogger<SettingStore>();
        Assert.Throws<ArgumentNullException>(() => new SettingStore(null!, logger));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        Assert.Throws<ArgumentNullException>(() => new SettingStore(CreateDataService(fsql), null!));
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
        var saved = fsql.Select<SettingEntity>().First();
        Assert.Null(saved.Value);
    }

    // ── Test helpers ──

    /// <summary>最小 IDomainUser 桩——仅满足编译，不提供真实用户上下文。</summary>
    private sealed class StubDomainUser : IDomainUser
    {
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
            => throw new NotSupportedException("Stub: Use<T> not supported in unit tests");
        public TService GetService<TService>() where TService : notnull
            => throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");
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
