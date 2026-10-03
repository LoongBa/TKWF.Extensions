using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// BlobRecordStore 测试——使用 SQLite 内存库验证真实读写 + 异常静默。
/// <para>BlobRecordStore 经 BlobRecordEntityDataService（FreeSqlEntityDAC + UnitOfWorkManager 驱动）委托持久化。</para>
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

    /// <summary>构造 BlobRecordEntityDataService——经真实 FreeSql DAC（UnitOfWorkManager + FreeSqlEntityDAC）驱动。</summary>
    private static BlobRecordEntityDataService CreateDataService(IFreeSql fsql, IDomainUser user)
    {
        var uowManager = new UnitOfWorkManager(fsql);
        var dac = new FreeSqlEntityDAC<BlobRecordEntity>(uowManager);
        return new BlobRecordEntityDataService(user, dac);
    }

    /// <summary>
    /// ADR88/DI004：Store 不再注入 DataService——经 IDomainUser.Use&lt;T&gt;() 懒加载解析，
    /// 测试桩注入能力容器（真实 DataService 注册到 ServiceProvider，stub 解析）。
    /// </summary>
    private static BlobRecordStore CreateStore(IFreeSql fsql, FakeLogger<BlobRecordStore> logger)
    {
        var stub = new StubDomainUser();
        var services = new ServiceCollection();
        services.AddSingleton(CreateDataService(fsql, stub));
        stub.ServiceProvider = services.BuildServiceProvider();
        return new BlobRecordStore(stub, logger);
    }

    [Fact]
    public async Task SaveAsync_NewRecord_PersistsToDatabase()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<BlobRecordStore>();
        var store = CreateStore(fsql, logger);

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
        var list = await store.GetListAsync(ct: CancellationToken.None);
        Assert.Single(list);
        var saved = list[0];
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
        var logger = new FakeLogger<BlobRecordStore>();
        var store = CreateStore(fsql, logger);

        var record = new BlobRecordEntity
        {
            Name = "doc.pdf",
            Path = "abc/doc.pdf",
            ContentType = "application/pdf",
            Size = 2048
        };
        await store.SaveAsync(record);

        var saved = (await store.GetListAsync(ct: CancellationToken.None))[0];
        var originalId = saved.Id;
        saved.Size = 4096;

        // Act
        await store.SaveAsync(saved);

        // Assert — 改造后 Upsert 保留自增 Id（不再先删后插）
        var list = await store.GetListAsync(ct: CancellationToken.None);
        Assert.Single(list);
        var updated = list[0];
        Assert.Equal(originalId, updated.Id);
        Assert.Equal(4096, updated.Size);
    }

    [Fact]
    public async Task GetAsync_ExistingRecord_ReturnsEntity()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<BlobRecordStore>();
        var store = CreateStore(fsql, logger);

        var record = new BlobRecordEntity
        {
            Name = "video.mp4",
            Path = "def/video.mp4",
            ContentType = "video/mp4",
            Size = 1048576
        };
        await store.SaveAsync(record);
        var id = (await store.GetListAsync(ct: CancellationToken.None))[0].Id;

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
        var logger = new FakeLogger<BlobRecordStore>();
        var store = CreateStore(fsql, logger);

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
        var logger = new FakeLogger<BlobRecordStore>();
        var store = CreateStore(fsql, logger);

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
        var logger = new FakeLogger<BlobRecordStore>();
        var store = CreateStore(fsql, logger);

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
        var logger = new FakeLogger<BlobRecordStore>();
        var store = CreateStore(fsql, logger);

        // Act — null record should be silently skipped
        await store.SaveAsync(null!);

        // Assert — no records created
        Assert.Empty(await store.GetListAsync(ct: CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_ExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        // Arrange — 使用已 Dispose 的 FreeSql，操作必定抛异常
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<BlobRecordStore>();
        var store = CreateStore(fsql, logger);

        fsql.Dispose();

        var record = new BlobRecordEntity { Name = "test.txt", Path = "test.txt" };

        // Act — should not throw
        await store.SaveAsync(record);

        // Assert — logger captured warning
        Assert.Single(logger.Warnings);
        Assert.Contains("Blob 记录保存失败", logger.Warnings[0]);
    }

    // ── Test helpers ──

    /// <summary>最小 IDomainUser 桩——Use&lt;T&gt;()/GetService&lt;T&gt;() 从注入的 ServiceProvider 容器解析
    /// （测试工厂经 <see cref="ServiceProvider"/> 注入；未注入时抛 NotSupportedException）。</summary>
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
