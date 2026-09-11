using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.CodeGeneration;
using Xunit;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// V0.4.0：管理 API 测试——DataService 管理方法（SearchLogs/GetDetail/Cleanup/GetStats/Delete）+ ExcludeMethods 断言。
/// <para>覆盖 Oracle P1-1（ExcludeMethods 排除含 ArgumentsJson 标准 CRUD）/P1-2（SearchLogs 裁剪 DTO）/
/// P1-4（Cleanup 直接实现）/P1-5（统计暴露）/P1-3（Delete 单条）。</para>
/// </summary>
public class AuditLogManagementApiTests
{
    private static IFreeSql CreateInMemoryFreeSql()
        => new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();

    private static async Task<AuditLogEntity> CreateLogAsync(
        IFreeSql fsql, string? userName = null, bool success = true,
        string serviceName = "OrderService", string methodName = "CreateOrder",
        DateTime? executionTime = null, int durationMs = 10)
    {
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);
        var entity = new AuditLogEntity
        {
            UserName = userName ?? "alice",
            UserId = "1",
            ServiceName = serviceName,
            MethodName = methodName,
            ArgumentsJson = "{\"amount\":100}",
            ExecutionTime = executionTime ?? DateTime.UtcNow,
            DurationMs = durationMs,
            Success = success,
            CorrelationId = "corr-1",
            CreateTime = DateTimeOffset.Now
        };
        return await dataService.EntityCreateAsync(entity, CancellationToken.None);
    }

    [Fact]
    public void GenerateController_ExcludeMethods_ExcludesSensitiveCrud()
    {
        // Oracle P1-1：ExcludeMethods 应排除 GetByIdAsync/SelectAsync/SelectPageAsync/CreateAsync/UpdateAsync
        //（防 ArgumentsJson 泄露 D5 + 防伪造审计）——断言特性配置正确
        var attr = typeof(AuditLogEntityDataService)
            .GetCustomAttributes(typeof(GenerateControllerAttribute), false)
            .Cast<GenerateControllerAttribute>()
            .FirstOrDefault();

        Assert.NotNull(attr);
        Assert.True(attr!.FromDataService);
        Assert.Contains("GetByIdAsync", attr.ExcludeMethods);
        Assert.Contains("SelectAsync", attr.ExcludeMethods);
        Assert.Contains("SelectPageAsync", attr.ExcludeMethods);
        Assert.Contains("CreateAsync", attr.ExcludeMethods);
        Assert.Contains("UpdateAsync", attr.ExcludeMethods);
        // DeleteAsync 保留（单条删除端点）
        Assert.DoesNotContain("DeleteAsync", attr.ExcludeMethods);
    }

    [Fact]
    public async Task SearchLogs_ReturnsTrimmedDto_NoArgumentsJson()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        await CreateLogAsync(fsql);
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);

        var result = await dataService.SearchLogsAsync(new AuditLogQueryInput { Take = 50 }, CancellationToken.None);

        Assert.Equal(1, result.Total);
        var dto = result.Items.Single();
        Assert.Equal("alice", dto.UserName);
        Assert.Equal("OrderService", dto.ServiceName);
        // 列表 DTO 不含 ArgumentsJson（D5 保持——Oracle P1-2）：AuditLogListItemDto 类型无该属性
        Assert.Null(dto.GetType().GetProperty("ArgumentsJson"));
    }

    [Fact]
    public async Task GetDetail_ReturnsArgumentsJson()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var created = await CreateLogAsync(fsql);
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);

        var detail = await dataService.GetDetailAsync(created.Id, CancellationToken.None);

        Assert.NotNull(detail);
        Assert.Equal("{\"amount\":100}", detail!.ArgumentsJson);   // 详情含 ArgumentsJson（权限门控由消费方）
        Assert.Equal("alice", detail.UserName);
    }

    [Fact]
    public async Task GetDetail_NonExistent_ReturnsNull()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);

        var detail = await dataService.GetDetailAsync(999_999, CancellationToken.None);
        Assert.Null(detail);
    }

    [Fact]
    public async Task Cleanup_DeletesExpired_RespectsRetention()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        // 30 天前（默认 retention 90 天内——保留）+ 现在（保留）
        await CreateLogAsync(fsql, executionTime: DateTime.UtcNow.AddDays(-30));
        await CreateLogAsync(fsql, executionTime: DateTime.UtcNow);
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);

        int deleted = await dataService.CleanupAsync(CancellationToken.None);

        Assert.Equal(0, deleted);   // 默认 90 天 retention——30 天前日志保留
    }

    [Fact]
    public async Task Cleanup_DeletesExpired_WhenOlderThanRetention()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        // 120 天前（超过默认 90 天 retention——删除）+ 现在（保留）
        await CreateLogAsync(fsql, executionTime: DateTime.UtcNow.AddDays(-120));
        await CreateLogAsync(fsql, executionTime: DateTime.UtcNow);
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);

        int deleted = await dataService.CleanupAsync(CancellationToken.None);

        Assert.Equal(1, deleted);   // 仅 120 天前日志被删
    }

    [Fact]
    public async Task Delete_SingleLog_PhysicalDelete()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        var created = await CreateLogAsync(fsql);
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);

        bool deleted = await dataService.DeleteAsync(created.Id, CancellationToken.None);

        Assert.True(deleted);
        var detail = await dataService.GetDetailAsync(created.Id, CancellationToken.None);
        Assert.Null(detail);   // 物理删除
    }

    [Fact]
    public async Task GetStats_ReturnsAggregates()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        await CreateLogAsync(fsql, success: true, durationMs: 10);
        await CreateLogAsync(fsql, success: false, durationMs: 20);
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);

        var stats = await dataService.GetStatsAsync(null, null, CancellationToken.None);

        Assert.Equal(2, stats.Total);
        Assert.Equal(1, stats.Succeeded);
        Assert.Equal(1, stats.Failed);
        Assert.True(stats.AvgDurationMs > 0);
    }
}
