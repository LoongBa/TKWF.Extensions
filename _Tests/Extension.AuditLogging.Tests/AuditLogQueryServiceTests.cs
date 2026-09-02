using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain.Interception.Auditing;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// AuditLogQueryService 测试——覆盖单条件/组合过滤、分页、CountAsync、无结果、异常静默、索引特性、DTO 字段验证。
/// <para>使用 FreeSql SQLite 内存库（对齐现有测试基建）。</para>
/// </summary>
public class AuditLogQueryServiceTests
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（每次调用新连接 = 独立内存库）。</summary>
    private static IFreeSql CreateInMemoryFreeSql()
    {
        return new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>创建 AuditLogQueryService 实例。</summary>
    private static (AuditLogQueryService Service, IFreeSql FreeSql) CreateService()
    {
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<AuditLogQueryService>();
        var service = new AuditLogQueryService(fsql, logger);
        return (service, fsql);
    }

    /// <summary>插入测试数据并返回。</summary>
    private static async Task InsertTestData(IFreeSql fsql)
    {
        var entries = new[]
        {
            new AuditLogEntity
            {
                UserName = "alice", UserId = "u1", ServiceName = "OrderService", MethodName = "CreateOrder",
                ExecutionTime = new DateTime(2026, 1, 15, 10, 0, 0), DurationMs = 50, Success = true,
                CorrelationId = "corr-001", CreateTime = DateTimeOffset.Now
            },
            new AuditLogEntity
            {
                UserName = "bob", UserId = "u2", ServiceName = "OrderService", MethodName = "DeleteOrder",
                ExecutionTime = new DateTime(2026, 1, 16, 11, 0, 0), DurationMs = 30, Success = false,
                Exception = "Not found", CorrelationId = "corr-002", CreateTime = DateTimeOffset.Now
            },
            new AuditLogEntity
            {
                UserName = "alice", UserId = "u1", ServiceName = "PaymentService", MethodName = "ProcessPayment",
                ExecutionTime = new DateTime(2026, 1, 17, 12, 0, 0), DurationMs = 200, Success = true,
                CorrelationId = "corr-001", CreateTime = DateTimeOffset.Now
            },
            new AuditLogEntity
            {
                UserName = "charlie", UserId = "u3", ServiceName = "PaymentService", MethodName = "Refund",
                ExecutionTime = new DateTime(2026, 1, 18, 13, 0, 0), DurationMs = 100, Success = true,
                CorrelationId = "corr-003", CreateTime = DateTimeOffset.Now
            },
            new AuditLogEntity
            {
                UserName = "bob", UserId = "u2", ServiceName = "InventoryService", MethodName = "CheckStock",
                ExecutionTime = new DateTime(2026, 1, 19, 14, 0, 0), DurationMs = 15, Success = true,
                CorrelationId = null, CreateTime = DateTimeOffset.Now
            },
        };

        foreach (var entry in entries)
            await fsql.Insert(entry).ExecuteAffrowsAsync();
    }

    // ── 单条件过滤 ──

    [Fact]
    public async Task GetListAsync_FilterByStartTime_ReturnsCorrectRecords()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            StartTime = new DateTime(2026, 1, 16, 0, 0, 0)
        });

        Assert.Equal(4, result.Total);
        Assert.All(result.Items, item => Assert.True(item.ExecutionTime >= new DateTime(2026, 1, 16, 0, 0, 0)));
    }

    [Fact]
    public async Task GetListAsync_FilterByEndTime_ReturnsCorrectRecords()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            EndTime = new DateTime(2026, 1, 16, 23, 59, 59)
        });

        Assert.Equal(2, result.Total);
    }

    [Fact]
    public async Task GetListAsync_FilterByUserName_LikeMatch()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            UserName = "ali"  // LIKE '%ali%' → matches "alice"
        });

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Equal("alice", item.UserName));
    }

    [Fact]
    public async Task GetListAsync_FilterByUserId_ExactMatch()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            UserId = "u2"
        });

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Equal("u2", item.UserId));
    }

    [Fact]
    public async Task GetListAsync_FilterByServiceName_ExactMatch()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            ServiceName = "PaymentService"
        });

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Equal("PaymentService", item.ServiceName));
    }

    [Fact]
    public async Task GetListAsync_FilterByMethodName_ExactMatch()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            MethodName = "CreateOrder"
        });

        Assert.Single(result.Items);
        Assert.Equal("CreateOrder", result.Items[0].MethodName);
    }

    [Fact]
    public async Task GetListAsync_FilterBySuccess_FalseOnly()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            Success = false
        });

        Assert.Single(result.Items);
        Assert.False(result.Items[0].Success);
        Assert.Equal("Not found", result.Items[0].Exception);
    }

    [Fact]
    public async Task GetListAsync_FilterByCorrelationId_ExactMatch()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            CorrelationId = "corr-001"
        });

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.Equal("corr-001", item.CorrelationId));
    }

    [Fact]
    public async Task GetListAsync_FilterByMinDurationMs_ReturnsRecordsAboveThreshold()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            MinDurationMs = 100
        });

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item => Assert.True(item.DurationMs >= 100));
    }

    [Fact]
    public async Task GetListAsync_FilterByMaxDurationMs_ReturnsRecordsBelowThreshold()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            MaxDurationMs = 50
        });

        Assert.Equal(3, result.Total);
        Assert.All(result.Items, item => Assert.True(item.DurationMs <= 50));
    }

    // ── 组合过滤（AND） ──

    [Fact]
    public async Task GetListAsync_CombinedFilters_ReturnsIntersection()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            UserName = "alice",
            ServiceName = "PaymentService",
            Success = true
        });

        Assert.Single(result.Items);
        var item = result.Items[0];
        Assert.Equal("alice", item.UserName);
        Assert.Equal("PaymentService", item.ServiceName);
        Assert.True(item.Success);
    }

    [Fact]
    public async Task GetListAsync_CombinedTimeRange_FiltersCorrectly()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            StartTime = new DateTime(2026, 1, 15, 0, 0, 0),
            EndTime = new DateTime(2026, 1, 17, 23, 59, 59),
            Success = true
        });

        Assert.Equal(2, result.Total);
        Assert.All(result.Items, item =>
        {
            Assert.True(item.ExecutionTime >= new DateTime(2026, 1, 15, 0, 0, 0));
            Assert.True(item.ExecutionTime <= new DateTime(2026, 1, 17, 23, 59, 59));
            Assert.True(item.Success);
        });
    }

    [Fact]
    public async Task GetListAsync_CombinedDurationRange_FiltersCorrectly()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            MinDurationMs = 30,
            MaxDurationMs = 100
        });

        // 30ms(bob), 50ms(alice), 100ms(charlie) → 3 records in [30,100]
        Assert.Equal(3, result.Total);
        Assert.All(result.Items, item =>
        {
            Assert.True(item.DurationMs >= 30);
            Assert.True(item.DurationMs <= 100);
        });
    }

    // ── 分页 Skip/Take ──

    [Fact]
    public async Task GetListAsync_Pagination_DefaultTakeIs50()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        // 默认 Take=50，数据只有 5 条 → 全部返回
        var result = await service.GetListAsync(new AuditLogQueryInput());

        Assert.Equal(5, result.Total);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task GetListAsync_Pagination_SkipAndTake()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            Skip = 2,
            Take = 2
        });

        Assert.Equal(5, result.Total);
        Assert.Equal(2, result.Items.Count);
    }

    [Fact]
    public async Task GetListAsync_Pagination_TakeExceedsMax_ClampedTo200()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        // Take=500 应被 clamp 到 200，数据只有 5 条 → 返回 5 条
        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            Take = 500
        });

        Assert.Equal(5, result.Total);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task GetListAsync_Pagination_TakeZero_DefaultsTo50()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        // Take=0 应默认为 50
        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            Take = 0
        });

        Assert.Equal(5, result.Total);
        Assert.Equal(5, result.Items.Count);
    }

    [Fact]
    public async Task GetListAsync_Pagination_SkipBeyondTotal_EmptyResult()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            Skip = 100,
            Take = 10
        });

        Assert.Equal(5, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetListAsync_Pagination_OrderByExecutionTimeDescending()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            Take = 5
        });

        // 应按 ExecutionTime 降序排列
        for (int i = 0; i < result.Items.Count - 1; i++)
        {
            Assert.True(result.Items[i].ExecutionTime >= result.Items[i + 1].ExecutionTime);
        }
    }

    // ── CountAsync ──

    [Fact]
    public async Task CountAsync_NoFilters_ReturnsTotalCount()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var count = await service.CountAsync(new AuditLogQueryInput());

        Assert.Equal(5, count);
    }

    [Fact]
    public async Task CountAsync_WithFilters_ReturnsFilteredCount()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var count = await service.CountAsync(new AuditLogQueryInput
        {
            UserName = "alice"
        });

        Assert.Equal(2, count);
    }

    [Fact]
    public async Task CountAsync_NoMatch_ReturnsZero()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var count = await service.CountAsync(new AuditLogQueryInput
        {
            UserName = "nonexistent"
        });

        Assert.Equal(0, count);
    }

    // ── 无结果 ──

    [Fact]
    public async Task GetListAsync_EmptyDatabase_ReturnsEmptyWithZeroTotal()
    {
        var (service, fsql) = CreateService();
        // 不插入数据

        var result = await service.GetListAsync(new AuditLogQueryInput());

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    [Fact]
    public async Task GetListAsync_NoMatch_ReturnsEmptyWithZeroTotal()
    {
        var (service, fsql) = CreateService();
        await InsertTestData(fsql);

        var result = await service.GetListAsync(new AuditLogQueryInput
        {
            UserName = "nonexistent"
        });

        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
    }

    // ── 异常静默 ──

    [Fact]
    public async Task GetListAsync_Exception_LogsWarningAndReturnsEmpty()
    {
        // Arrange — 使用已 Dispose 的 FreeSql，查询必定抛异常
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<AuditLogQueryService>();
        var service = new AuditLogQueryService(fsql, logger);

        fsql.Dispose();

        // Act
        var result = await service.GetListAsync(new AuditLogQueryInput());

        // Assert — 异常静默：返回空结果 + Warning 日志
        Assert.Equal(0, result.Total);
        Assert.Empty(result.Items);
        Assert.Single(logger.Warnings);
        Assert.Contains("审计日志查询失败", logger.Warnings[0]);
    }

    [Fact]
    public async Task CountAsync_Exception_LogsWarningAndReturnsZero()
    {
        // Arrange — 使用已 Dispose 的 FreeSql
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var logger = new FakeLogger<AuditLogQueryService>();
        var service = new AuditLogQueryService(fsql, logger);

        fsql.Dispose();

        // Act
        var count = await service.CountAsync(new AuditLogQueryInput());

        // Assert — 异常静默：返回 0 + Warning 日志
        Assert.Equal(0, count);
        Assert.Single(logger.Warnings);
        Assert.Contains("审计日志统计失败", logger.Warnings[0]);
    }

    // ── 构造器参数校验 ──

    [Fact]
    public void Constructor_NullFreeSql_Throws()
    {
        var logger = new FakeLogger<AuditLogQueryService>();
        Assert.Throws<ArgumentNullException>(() => new AuditLogQueryService(null!, logger));
    }

    [Fact]
    public void Constructor_NullLogger_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        Assert.Throws<ArgumentNullException>(() => new AuditLogQueryService(fsql, null!));
    }

    // ── null 输入校验 ──

    [Fact]
    public async Task GetListAsync_NullQuery_ThrowsArgumentNullException()
    {
        var (service, _) = CreateService();
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.GetListAsync(null!));
    }

    [Fact]
    public async Task CountAsync_NullQuery_ThrowsArgumentNullException()
    {
        var (service, _) = CreateService();
        await Assert.ThrowsAsync<ArgumentNullException>(() => service.CountAsync(null!));
    }

    // ── 索引特性反射断言 ──

    [Fact]
    public void AuditLogEntity_HasIndexAttribute_IX_AuditLog_ExecutionTime()
    {
        var type = typeof(AuditLogEntity);
        var indexes = type.GetCustomAttributes<FreeSql.DataAnnotations.IndexAttribute>(false).ToList();

        var match = indexes.FirstOrDefault(i =>
            i.Name == "IX_AuditLog_ExecutionTime" && !i.IsUnique);

        Assert.NotNull(match);
    }

    [Fact]
    public void AuditLogEntity_HasIndexAttribute_IX_AuditLog_UserName()
    {
        var type = typeof(AuditLogEntity);
        var indexes = type.GetCustomAttributes<FreeSql.DataAnnotations.IndexAttribute>(false).ToList();

        var match = indexes.FirstOrDefault(i =>
            i.Name == "IX_AuditLog_UserName" && !i.IsUnique);

        Assert.NotNull(match);
    }

    [Fact]
    public void AuditLogEntity_HasIndexAttribute_IX_AuditLog_CorrelationId()
    {
        var type = typeof(AuditLogEntity);
        var indexes = type.GetCustomAttributes<FreeSql.DataAnnotations.IndexAttribute>(false).ToList();

        var match = indexes.FirstOrDefault(i =>
            i.Name == "IX_AuditLog_CorrelationId" && !i.IsUnique);

        Assert.NotNull(match);
    }

    [Fact]
    public void AuditLogEntity_HasExactlyThreeNonUniqueIndexes()
    {
        var type = typeof(AuditLogEntity);
        var indexes = type.GetCustomAttributes<FreeSql.DataAnnotations.IndexAttribute>(false).ToList();

        Assert.Equal(3, indexes.Count);
        Assert.All(indexes, idx => Assert.False(idx.IsUnique));
    }

    // ── DTO 字段验证（不含 ArgumentsJson） ──

    [Fact]
    public void AuditLogListItemDto_DoesNotContainArgumentsJson()
    {
        var type = typeof(AuditLogListItemDto);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance);

        var hasArgumentsJson = properties.Any(p => p.Name == "ArgumentsJson");
        Assert.False(hasArgumentsJson, "AuditLogListItemDto 不应包含 ArgumentsJson 字段（安全决策 D5）");
    }

    [Fact]
    public void AuditLogListItemDto_ContainsExpectedFields()
    {
        var type = typeof(AuditLogListItemDto);
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.Contains("Id", properties);
        Assert.Contains("UserName", properties);
        Assert.Contains("UserId", properties);
        Assert.Contains("ServiceName", properties);
        Assert.Contains("MethodName", properties);
        Assert.Contains("ExecutionTime", properties);
        Assert.Contains("DurationMs", properties);
        Assert.Contains("Success", properties);
        Assert.Contains("Exception", properties);
        Assert.Contains("CorrelationId", properties);
        Assert.Contains("CreateTime", properties);
    }

    [Fact]
    public void AuditLogPagedResult_ContainsTotalAndItems()
    {
        var type = typeof(AuditLogPagedResult);
        var props = type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(p => p.Name)
            .ToList();

        Assert.Contains("Total", props);
        Assert.Contains("Items", props);
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
