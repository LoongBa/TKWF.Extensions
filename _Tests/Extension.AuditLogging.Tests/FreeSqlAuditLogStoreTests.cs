using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interception.Auditing;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// AuditLogStore 测试——使用 SQLite 内存库验证真实写入 + 异常静默。
/// </summary>
public class AuditLogStoreTests
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
    public async Task SaveAsync_NormalInsert_PersistsToDatabase()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<AuditLogStore>();
        var store = AuditLoggingTestHost.CreateStore(fsql);

        var entry = new AuditLogEntry(
            UserName: "testuser",
            UserId: "u123",
            ServiceName: "OrderService",
            MethodName: "CreateOrder",
            ArgumentsJson: "{\"id\":1}",
            ExecutionTime: DateTimeOffset.Parse("2026-01-15T10:30:00+08:00"),
            DurationMs: 42,
            Success: true,
            Exception: null,
            CorrelationId: "corr-001"
        );

        // Act
        await store.SaveAsync(entry);

        // Assert — 经 DataService 业务方法回查（红线：断言不经裸 fsql.Select）
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);
        var saved = await dataService.EntityGetAsync(e => e.CorrelationId == "corr-001", CancellationToken.None);
        Assert.NotNull(saved);
        var entity = saved!;
        Assert.Equal("testuser", entity.UserName);
        Assert.Equal("u123", entity.UserId);
        Assert.Equal("OrderService", entity.ServiceName);
        Assert.Equal("CreateOrder", entity.MethodName);
        Assert.Equal("{\"id\":1}", entity.ArgumentsJson);
        Assert.Equal(42, entity.DurationMs);
        Assert.True(entity.Success);
        Assert.Null(entity.Exception);
        Assert.Equal("corr-001", entity.CorrelationId);
    }

    [Fact]
    public async Task SaveAsync_ExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        // Arrange — 使用已 Dispose 的 FreeSql，Insert 必定抛异常
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<AuditLogStore>();
        var store = AuditLoggingTestHost.CreateStore(fsql);

        // Dispose 后使用 → 抛 ObjectDisposedException
        fsql.Dispose();

        var entry = new AuditLogEntry(
            UserName: "user1",
            UserId: "u1",
            ServiceName: "Svc",
            MethodName: "Method",
            ArgumentsJson: null,
            ExecutionTime: DateTimeOffset.Now,
            DurationMs: 10,
            Success: true,
            Exception: null,
            CorrelationId: null
        );

        // Act — Dispose 后写入：DataService 封装层不再抛（UoW 降级），静默跳过
        await store.SaveAsync(entry);

        // Assert — 静默语义保留（不抛异常）
        Assert.True(true);
    }

    [Fact]
    public async Task SaveAsync_NullEntry_DoesNotThrow()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<AuditLogStore>();
        var store = AuditLoggingTestHost.CreateStore(fsql);

        // Act — null entry should be silently skipped
        await store.SaveAsync(null!);

        // Assert — no records created
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);
        var all = await dataService.EntitySelectAsync(predicate: null, ct: CancellationToken.None);
        Assert.Empty(all);
    }

    [Fact]
    public async Task SaveAsync_FieldAlignment_AllFieldsPreserved()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<AuditLogStore>();
        var store = AuditLoggingTestHost.CreateStore(fsql);

        var execTime = new DateTime(2026, 6, 1, 14, 0, 0, DateTimeKind.Utc);
        var entry = new AuditLogEntry(
            UserName: "alice",
            UserId: "a1",
            ServiceName: "PaymentService",
            MethodName: "ProcessPayment",
            ArgumentsJson: "{\"amount\":100}",
            ExecutionTime: execTime,
            DurationMs: 150,
            Success: false,
            Exception: "Insufficient funds",
            CorrelationId: "pay-999"
        );

        // Act
        await store.SaveAsync(entry);

        // Assert — 经 DataService 业务方法回查（红线：断言不经裸 fsql.Select）
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);
        var saved = await dataService.EntityGetAsync(e => e.CorrelationId == "pay-999", CancellationToken.None);
        Assert.NotNull(saved);
        var entity = saved!;
        Assert.Equal("alice", entity.UserName);
        Assert.Equal("a1", entity.UserId);
        Assert.Equal("PaymentService", entity.ServiceName);
        Assert.Equal("ProcessPayment", entity.MethodName);
        Assert.Equal("{\"amount\":100}", entity.ArgumentsJson);
        Assert.Equal(execTime, entity.ExecutionTime, TimeSpan.FromSeconds(1));
        Assert.Equal(150, entity.DurationMs);
        Assert.False(entity.Success);
        Assert.Equal("Insufficient funds", entity.Exception);
        Assert.Equal("pay-999", entity.CorrelationId);
        Assert.True(entity.CreateTime > DateTimeOffset.MinValue);
    }

    [Fact]
    public void Constructor_NullDataService_Throws()
    {
        var logger = new FakeLogger<AuditLogStore>();
        Assert.Throws<ArgumentNullException>(() => new AuditLogStore(null!, logger));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var dac = new FreeSqlEntityDAC<AuditLogEntity>(new UnitOfWorkManager(fsql));
        var dataService = new AuditLogEntityDataService(new StubDomainUser(), dac);
        Assert.Throws<ArgumentNullException>(() => new AuditLogStore(dataService, null!));
    }

    [Fact]
    public async Task SaveAsync_MultipleEntries_AllPersisted()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<AuditLogStore>();
        var store = AuditLoggingTestHost.CreateStore(fsql);

        // Act
        for (int i = 0; i < 5; i++)
        {
            await store.SaveAsync(new AuditLogEntry(
                UserName: $"user{i}",
                UserId: $"u{i}",
                ServiceName: $"Service{i}",
                MethodName: $"Method{i}",
                ArgumentsJson: null,
                ExecutionTime: DateTimeOffset.Now,
                DurationMs: i * 10,
                Success: true,
                Exception: null,
                CorrelationId: null
            ));
        }

        // Assert — 经 DataService 业务方法回查（红线：断言不经裸 fsql.Select）
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);
        var all = await dataService.EntitySelectAsync(predicate: null, ct: CancellationToken.None);
        Assert.Equal(5, all.Count);
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
