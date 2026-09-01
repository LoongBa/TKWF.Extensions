using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Emailing.Tests;

/// <summary>
/// FreeSqlEmailRecordStore 测试——使用 SQLite 内存库验证真实读写 + 异常静默。
/// </summary>
public class FreeSqlEmailRecordStoreTests
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
    public async Task SaveAsync_NewRecord_PersistsToDatabase()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var logger = new FakeLogger<FreeSqlEmailRecordStore>();
        var store = new FreeSqlEmailRecordStore(fsql, logger);

        var entity = new EmailRecordEntity
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Hello",
            Body = "World",
            IsHtml = false,
            Status = "Sent"
        };

        // Act
        await store.SaveAsync(entity);

        // Assert
        var count = fsql.Select<EmailRecordEntity>().Count();
        Assert.Equal(1, count);

        var saved = fsql.Select<EmailRecordEntity>().First();
        Assert.Equal("test@example.com", saved.To);
        Assert.Equal("sender@example.com", saved.From);
        Assert.Equal("Hello", saved.Subject);
        Assert.Equal("World", saved.Body);
        Assert.False(saved.IsHtml);
        Assert.Equal("Sent", saved.Status);
    }

    [Fact]
    public async Task SaveAsync_UpdateExisting_UpdatesRecord()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var logger = new FakeLogger<FreeSqlEmailRecordStore>();
        var store = new FreeSqlEmailRecordStore(fsql, logger);

        var entity = new EmailRecordEntity
        {
            To = "test@example.com",
            Subject = "Original",
            Status = "Pending"
        };
        await store.SaveAsync(entity);

        // 获取生成的 Id
        var saved = fsql.Select<EmailRecordEntity>().First();
        saved.Status = "Sent";
        saved.SendTime = DateTime.Now;

        // Act
        await store.SaveAsync(saved);

        // Assert
        var count = fsql.Select<EmailRecordEntity>().Count();
        Assert.Equal(1, count);

        var updated = fsql.Select<EmailRecordEntity>().First();
        Assert.Equal("Sent", updated.Status);
        Assert.NotNull(updated.SendTime);
    }

    [Fact]
    public async Task GetAsync_ExistingRecord_ReturnsEntity()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var logger = new FakeLogger<FreeSqlEmailRecordStore>();
        var store = new FreeSqlEmailRecordStore(fsql, logger);

        var entity = new EmailRecordEntity
        {
            To = "test@example.com",
            Subject = "Test Subject",
            Status = "Sent"
        };
        await store.SaveAsync(entity);
        var id = fsql.Select<EmailRecordEntity>().First().Id;

        // Act
        var result = await store.GetAsync(id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("Test Subject", result!.Subject);
        Assert.Equal("test@example.com", result.To);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var logger = new FakeLogger<FreeSqlEmailRecordStore>();
        var store = new FreeSqlEmailRecordStore(fsql, logger);

        // Act
        var result = await store.GetAsync(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetListAsync_MultipleRecords_ReturnsAll()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var logger = new FakeLogger<FreeSqlEmailRecordStore>();
        var store = new FreeSqlEmailRecordStore(fsql, logger);

        await store.SaveAsync(new EmailRecordEntity { To = "a@example.com", Subject = "A", Status = "Sent" });
        await store.SaveAsync(new EmailRecordEntity { To = "b@example.com", Subject = "B", Status = "Failed" });
        await store.SaveAsync(new EmailRecordEntity { To = "c@example.com", Subject = "C", Status = "Sent" });

        // Act
        var list = await store.GetListAsync();

        // Assert
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task GetListAsync_WithStatusFilter_ReturnsFiltered()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var logger = new FakeLogger<FreeSqlEmailRecordStore>();
        var store = new FreeSqlEmailRecordStore(fsql, logger);

        await store.SaveAsync(new EmailRecordEntity { To = "a@example.com", Subject = "A", Status = "Sent" });
        await store.SaveAsync(new EmailRecordEntity { To = "b@example.com", Subject = "B", Status = "Failed" });
        await store.SaveAsync(new EmailRecordEntity { To = "c@example.com", Subject = "C", Status = "Sent" });

        // Act
        var list = await store.GetListAsync("Failed");

        // Assert
        Assert.Single(list);
        Assert.Equal("Failed", list[0].Status);
    }

    [Fact]
    public async Task SaveAsync_NullEntity_DoesNotThrow()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var logger = new FakeLogger<FreeSqlEmailRecordStore>();
        var store = new FreeSqlEmailRecordStore(fsql, logger);

        // Act — null entity should be silently skipped
        await store.SaveAsync(null!);

        // Assert — no records created
        var count = fsql.Select<EmailRecordEntity>().Count();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SaveAsync_ExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        // Arrange — 使用已 Dispose 的 FreeSql，操作必定抛异常
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var logger = new FakeLogger<FreeSqlEmailRecordStore>();
        var store = new FreeSqlEmailRecordStore(fsql, logger);

        fsql.Dispose();

        var entity = new EmailRecordEntity { To = "test@example.com", Subject = "Test" };

        // Act — should not throw
        await store.SaveAsync(entity);

        // Assert — logger captured warning
        Assert.Single(logger.Warnings);
        Assert.Contains("邮件记录保存失败", logger.Warnings[0]);
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
