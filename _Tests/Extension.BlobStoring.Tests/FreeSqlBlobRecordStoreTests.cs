using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// FreeSqlBlobRecordStore 测试——使用 SQLite 内存库验证真实读写 + 异常静默。
/// </summary>
public class FreeSqlBlobRecordStoreTests
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
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<FreeSqlBlobRecordStore>();
        var store = new FreeSqlBlobRecordStore(fsql, logger);

        var record = new BlobRecordEntity
        {
            Name = "photo.png",
            Path = "abc123/photo.png",
            ContentType = "image/png",
            Size = 1024,
            UploaderName = "testuser"
        };

        // Act
        await store.SaveAsync(record);

        // Assert
        var count = fsql.Select<BlobRecordEntity>().Count();
        Assert.Equal(1, count);

        var saved = fsql.Select<BlobRecordEntity>().First();
        Assert.Equal("photo.png", saved.Name);
        Assert.Equal("abc123/photo.png", saved.Path);
        Assert.Equal("image/png", saved.ContentType);
        Assert.Equal(1024, saved.Size);
        Assert.Equal("testuser", saved.UploaderName);
    }

    [Fact]
    public async Task SaveAsync_UpdateExisting_UpdatesRecord()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<FreeSqlBlobRecordStore>();
        var store = new FreeSqlBlobRecordStore(fsql, logger);

        var record = new BlobRecordEntity
        {
            Name = "doc.pdf",
            Path = "abc/doc.pdf",
            ContentType = "application/pdf",
            Size = 2048
        };
        await store.SaveAsync(record);

        var saved = fsql.Select<BlobRecordEntity>().First();
        saved.Size = 4096;

        // Act
        await store.SaveAsync(saved);

        // Assert
        var count = fsql.Select<BlobRecordEntity>().Count();
        Assert.Equal(1, count);

        var updated = fsql.Select<BlobRecordEntity>().First();
        Assert.Equal(4096, updated.Size);
    }

    [Fact]
    public async Task GetAsync_ExistingRecord_ReturnsEntity()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<FreeSqlBlobRecordStore>();
        var store = new FreeSqlBlobRecordStore(fsql, logger);

        var record = new BlobRecordEntity
        {
            Name = "video.mp4",
            Path = "def/video.mp4",
            ContentType = "video/mp4",
            Size = 1048576
        };
        await store.SaveAsync(record);
        var id = fsql.Select<BlobRecordEntity>().First().Id;

        // Act
        var result = await store.GetAsync(id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("video.mp4", result!.Name);
        Assert.Equal("video/mp4", result.ContentType);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<FreeSqlBlobRecordStore>();
        var store = new FreeSqlBlobRecordStore(fsql, logger);

        // Act
        var result = await store.GetAsync(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByNameAsync_ExistingRecord_ReturnsEntity()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<FreeSqlBlobRecordStore>();
        var store = new FreeSqlBlobRecordStore(fsql, logger);

        await store.SaveAsync(new BlobRecordEntity
        {
            Name = "image.jpg",
            Path = "ghi/image.jpg",
            ContentType = "image/jpeg"
        });

        // Act
        var result = await store.GetByNameAsync("image.jpg");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("image.jpg", result!.Name);
    }

    [Fact]
    public async Task GetListAsync_MultipleRecords_ReturnsAll()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<FreeSqlBlobRecordStore>();
        var store = new FreeSqlBlobRecordStore(fsql, logger);

        await store.SaveAsync(new BlobRecordEntity { Name = "a.txt", Path = "a/a.txt", ContentType = "text/plain" });
        await store.SaveAsync(new BlobRecordEntity { Name = "b.png", Path = "b/b.png", ContentType = "image/png" });
        await store.SaveAsync(new BlobRecordEntity { Name = "c.pdf", Path = "c/c.pdf", ContentType = "application/pdf" });

        // Act
        var list = await store.GetListAsync();

        // Assert
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task SaveAsync_NullRecord_DoesNotThrow()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<FreeSqlBlobRecordStore>();
        var store = new FreeSqlBlobRecordStore(fsql, logger);

        // Act — null record should be silently skipped
        await store.SaveAsync(null!);

        // Assert — no records created
        var count = fsql.Select<BlobRecordEntity>().Count();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SaveAsync_ExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        // Arrange — 使用已 Dispose 的 FreeSql，操作必定抛异常
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<FreeSqlBlobRecordStore>();
        var store = new FreeSqlBlobRecordStore(fsql, logger);

        fsql.Dispose();

        var record = new BlobRecordEntity { Name = "test.txt", Path = "test.txt" };

        // Act — should not throw
        await store.SaveAsync(record);

        // Assert — logger captured warning
        Assert.Single(logger.Warnings);
        Assert.Contains("Blob 记录保存失败", logger.Warnings[0]);
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
