using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Interception.Auditing;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// FreeSqlAuditLogStore 测试——使用 SQLite 内存库验证真实写入 + 异常静默。
/// </summary>
public class FreeSqlAuditLogStoreTests
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
        var logger = new FakeLogger<FreeSqlAuditLogStore>();
        var store = new FreeSqlAuditLogStore(fsql, logger);

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

        // Assert
        var count = fsql.Select<AuditLogEntity>().Count();
        Assert.Equal(1, count);

        var saved = fsql.Select<AuditLogEntity>().First();
        Assert.Equal("testuser", saved.UserName);
        Assert.Equal("u123", saved.UserId);
        Assert.Equal("OrderService", saved.ServiceName);
        Assert.Equal("CreateOrder", saved.MethodName);
        Assert.Equal("{\"id\":1}", saved.ArgumentsJson);
        Assert.Equal(42, saved.DurationMs);
        Assert.True(saved.Success);
        Assert.Null(saved.Exception);
        Assert.Equal("corr-001", saved.CorrelationId);
    }

    [Fact]
    public async Task SaveAsync_ExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        // Arrange — 使用已 Dispose 的 FreeSql，Insert 必定抛异常
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<FreeSqlAuditLogStore>();
        var store = new FreeSqlAuditLogStore(fsql, logger);

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

        // Act — should not throw
        await store.SaveAsync(entry);

        // Assert — logger captured warning
        Assert.Single(logger.Warnings);
        Assert.Contains("审计日志写入失败", logger.Warnings[0]);
    }

    [Fact]
    public async Task SaveAsync_NullEntry_DoesNotThrow()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<FreeSqlAuditLogStore>();
        var store = new FreeSqlAuditLogStore(fsql, logger);

        // Act — null entry should be silently skipped
        await store.SaveAsync(null!);

        // Assert — no records created
        var count = fsql.Select<AuditLogEntity>().Count();
        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SaveAsync_FieldAlignment_AllFieldsPreserved()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<FreeSqlAuditLogStore>();
        var store = new FreeSqlAuditLogStore(fsql, logger);

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

        // Assert
        var saved = fsql.Select<AuditLogEntity>().First();
        Assert.Equal("alice", saved.UserName);
        Assert.Equal("a1", saved.UserId);
        Assert.Equal("PaymentService", saved.ServiceName);
        Assert.Equal("ProcessPayment", saved.MethodName);
        Assert.Equal("{\"amount\":100}", saved.ArgumentsJson);
        Assert.Equal(execTime, saved.ExecutionTime, TimeSpan.FromSeconds(1));
        Assert.Equal(150, saved.DurationMs);
        Assert.False(saved.Success);
        Assert.Equal("Insufficient funds", saved.Exception);
        Assert.Equal("pay-999", saved.CorrelationId);
        Assert.True(saved.CreateTime > DateTimeOffset.MinValue);
    }

    [Fact]
    public void Constructor_NullFreeSql_Throws()
    {
        var logger = new FakeLogger<FreeSqlAuditLogStore>();
        Assert.Throws<ArgumentNullException>(() => new FreeSqlAuditLogStore(null!, logger));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        Assert.Throws<ArgumentNullException>(() => new FreeSqlAuditLogStore(fsql, null!));
    }

    [Fact]
    public async Task SaveAsync_MultipleEntries_AllPersisted()
    {
        // Arrange
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<FreeSqlAuditLogStore>();
        var store = new FreeSqlAuditLogStore(fsql, logger);

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

        // Assert
        var count = fsql.Select<AuditLogEntity>().Count();
        Assert.Equal(5, count);
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
