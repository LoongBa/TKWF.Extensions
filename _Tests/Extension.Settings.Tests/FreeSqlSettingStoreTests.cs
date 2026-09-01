using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Settings.Tests;

/// <summary>
/// FreeSqlSettingStore 测试——使用 SQLite 内存库验证真实读写 + 异常静默。
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

    [Fact]
    public async Task SetAsync_NewSetting_PersistsToDatabase()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<FreeSqlSettingStore>();
        var store = new FreeSqlSettingStore(fsql, logger);

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
        var logger = new FakeLogger<FreeSqlSettingStore>();
        var store = new FreeSqlSettingStore(fsql, logger);

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
        var logger = new FakeLogger<FreeSqlSettingStore>();
        var store = new FreeSqlSettingStore(fsql, logger);

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
        var logger = new FakeLogger<FreeSqlSettingStore>();
        var store = new FreeSqlSettingStore(fsql, logger);

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
        var logger = new FakeLogger<FreeSqlSettingStore>();
        var store = new FreeSqlSettingStore(fsql, logger);

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
        var logger = new FakeLogger<FreeSqlSettingStore>();
        var store = new FreeSqlSettingStore(fsql, logger);

        // Act & Assert — should not throw
        await store.DeleteAsync("NotExist", "Global", null, CancellationToken.None);
    }

    [Fact]
    public async Task GetListAsync_MultipleSettings_ReturnsAll()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<FreeSqlSettingStore>();
        var store = new FreeSqlSettingStore(fsql, logger);

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
        var logger = new FakeLogger<FreeSqlSettingStore>();
        var store = new FreeSqlSettingStore(fsql, logger);

        await store.SetAsync("A", "1", "Global", null, null, CancellationToken.None);

        // Act
        var list = await store.GetListAsync("Tenant", null, CancellationToken.None);

        // Assert
        Assert.Empty(list);
    }

    [Fact]
    public void Constructor_NullFreeSql_Throws()
    {
        var logger = new FakeLogger<FreeSqlSettingStore>();
        Assert.Throws<ArgumentNullException>(() => new FreeSqlSettingStore(null!, logger));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        Assert.Throws<ArgumentNullException>(() => new FreeSqlSettingStore(fsql, null!));
    }

    [Fact]
    public async Task SetAsync_ExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        // Arrange — 使用已 Dispose 的 FreeSql，操作必定抛异常
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        var logger = new FakeLogger<FreeSqlSettingStore>();
        var store = new FreeSqlSettingStore(fsql, logger);

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
        var logger = new FakeLogger<FreeSqlSettingStore>();
        var store = new FreeSqlSettingStore(fsql, logger);

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
        var logger = new FakeLogger<FreeSqlSettingStore>();
        var store = new FreeSqlSettingStore(fsql, logger);

        // Act
        await store.SetAsync("NullVal", null, "Global", null, null, CancellationToken.None);

        // Assert
        var saved = fsql.Select<SettingEntity>().First();
        Assert.Null(saved.Value);
    }

    // ── Test helpers ──

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
