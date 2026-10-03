using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Emailing.Tests;

/// <summary>
/// EmailRecordStore 测试——使用 SQLite 内存库验证真实读写 + 异常静默。
/// <para>EmailRecordStore 经 EmailRecordEntityDataService（FreeSqlEntityDAC + UnitOfWorkManager 驱动）委托持久化。</para>
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

    /// <summary>构造 EmailRecordEntityDataService——经真实 FreeSql DAC（UnitOfWorkManager + FreeSqlEntityDAC）驱动。</summary>
    private static EmailRecordEntityDataService CreateDataService(IFreeSql fsql, IDomainUser user)
    {
        var uowManager = new UnitOfWorkManager(fsql);
        var dac = new FreeSqlEntityDAC<EmailRecordEntity>(uowManager);
        return new EmailRecordEntityDataService(user, dac);
    }

    /// <summary>
    /// ADR88/DI004：Store 不再注入 DataService——经 IDomainUser.Use&lt;T&gt;() 懒加载解析，
    /// 测试桩注入能力容器（真实 DataService 注册到 ServiceProvider，stub 解析）。
    /// </summary>
    private static EmailRecordStore CreateStore(IFreeSql fsql, FakeLogger<EmailRecordStore> logger)
    {
        var stub = new StubDomainUser();
        var services = new ServiceCollection();
        services.AddSingleton(CreateDataService(fsql, stub));
        stub.ServiceProvider = services.BuildServiceProvider();
        return new EmailRecordStore(stub, logger);
    }

    [Fact]
    public async Task SaveAsync_NewRecord_PersistsToDatabase()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var logger = new FakeLogger<EmailRecordStore>();
        var store = CreateStore(fsql, logger);

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
        var list = await store.GetListAsync(ct: CancellationToken.None);
        Assert.Single(list);
        var saved = list[0];
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
        var logger = new FakeLogger<EmailRecordStore>();
        var store = CreateStore(fsql, logger);

        var entity = new EmailRecordEntity
        {
            To = "test@example.com",
            Subject = "Original",
            Status = "Pending"
        };
        await store.SaveAsync(entity);

        // 获取生成的 Id
        var saved = (await store.GetListAsync(ct: CancellationToken.None))[0];
        saved.Status = "Sent";
        saved.SendTime = DateTime.Now;

        // Act
        await store.SaveAsync(saved);

        // Assert
        var list = await store.GetListAsync(ct: CancellationToken.None);
        Assert.Single(list);
        var updated = list[0];
        Assert.Equal("Sent", updated.Status);
        Assert.NotNull(updated.SendTime);
    }

    [Fact]
    public async Task GetAsync_ExistingRecord_ReturnsEntity()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var logger = new FakeLogger<EmailRecordStore>();
        var store = CreateStore(fsql, logger);

        var entity = new EmailRecordEntity
        {
            To = "test@example.com",
            Subject = "Test Subject",
            Status = "Sent"
        };
        await store.SaveAsync(entity);
        var id = (await store.GetListAsync(ct: CancellationToken.None))[0].Id;

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
        var logger = new FakeLogger<EmailRecordStore>();
        var store = CreateStore(fsql, logger);

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
        var logger = new FakeLogger<EmailRecordStore>();
        var store = CreateStore(fsql, logger);

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
        var logger = new FakeLogger<EmailRecordStore>();
        var store = CreateStore(fsql, logger);

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
        var logger = new FakeLogger<EmailRecordStore>();
        var store = CreateStore(fsql, logger);

        // Act — null entity should be silently skipped
        await store.SaveAsync(null!);

        // Assert — no records created
        Assert.Empty(await store.GetListAsync(ct: CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_ExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        // Arrange — 使用已 Dispose 的 FreeSql，操作必定抛异常
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var logger = new FakeLogger<EmailRecordStore>();
        var store = CreateStore(fsql, logger);

        fsql.Dispose();

        var entity = new EmailRecordEntity { To = "test@example.com", Subject = "Test" };

        // Act — should not throw
        await store.SaveAsync(entity);

        // Assert — logger captured warning
        Assert.Single(logger.Warnings);
        Assert.Contains("邮件记录保存失败", logger.Warnings[0]);
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
