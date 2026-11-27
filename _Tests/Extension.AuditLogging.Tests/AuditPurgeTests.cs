using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Xunit;

namespace TKWF.Ext.AuditLogging.Tests;

public class AuditPurgeTests
{
    private static IFreeSql CreateInMemoryFreeSql()
        => new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();

    private static async Task SeedExpiredAsync(IFreeSql fsql, int count, int daysAgo)
    {
        var dataService = AuditLoggingTestHost.CreateDataService(fsql);
        for (var i = 0; i < count; i++)
        {
            await dataService.EntityCreateAsync(new AuditLogEntity
            {
                ServiceName = "Svc",
                MethodName = "Method" + i,
                ExecutionTime = DateTime.UtcNow.AddDays(-daysAgo),
                Success = true,
                CreateTime = DateTimeOffset.Now,
            }, CancellationToken.None);
        }
    }

    [Fact]
    public async Task PurgeAsync_DeletesExpired_ReturnsResult()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        await SeedExpiredAsync(fsql, 2, daysAgo: 10);

        var analytics = AuditLoggingTestHost.CreateAnalyticsService(
            fsql, new AuditLoggingOptions { RetentionDays = 1, CleanupBatchSize = 10 });

        var result = await analytics.PurgeAsync(maxRounds: 100, ct: CancellationToken.None);

        Assert.Equal(2L, result.DeletedCount);
        Assert.Equal(AuditFacet.Invocation, result.Facet);
        Assert.Equal(1, result.RetentionDays);
        Assert.True(result.Rounds >= 1);
    }

    [Fact]
    public async Task PurgeAsync_BoundedByMaxRounds()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        await SeedExpiredAsync(fsql, 3, daysAgo: 10);

        var analytics = AuditLoggingTestHost.CreateAnalyticsService(
            fsql, new AuditLoggingOptions { RetentionDays = 1, CleanupBatchSize = 1 });

        var result = await analytics.PurgeAsync(maxRounds: 1, ct: CancellationToken.None);

        Assert.Equal(1L, result.DeletedCount);
        Assert.Equal(1, result.Rounds);
    }

    [Fact]
    public async Task CleanupExpiredAsync_DelegatesToPurge()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        await SeedExpiredAsync(fsql, 2, daysAgo: 10);

        var analytics = AuditLoggingTestHost.CreateAnalyticsService(
            fsql, new AuditLoggingOptions { RetentionDays = 1, CleanupBatchSize = 10 });

        var deleted = await analytics.CleanupExpiredAsync(CancellationToken.None);

        Assert.Equal(2, deleted);
    }
}
