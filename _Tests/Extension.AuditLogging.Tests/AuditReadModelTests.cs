using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using Xunit;

namespace TKWF.Ext.AuditLogging.Tests;

/// <summary>
/// v0.7 统一读模型测试（C1/C5/G6 + 完整性）——真实 DataService/Reader/门面装配（无 mock）。
/// </summary>
public class AuditReadModelTests
{
    private static IFreeSql CreateFsql()
    {
        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<AuditLogEntity>();
        fsql.CodeFirst.SyncStructure<BusinessAuditEntity>();
        return fsql;
    }

    private sealed class Host
    {
        public ServiceProvider Sp = null!;
        public StubDomainUser User = null!;
        public AuditQueryService Facade = null!;
        public AuditExportService Export = null!;
    }

    private static Host Build(IFreeSql fsql)
    {
        var user = new StubDomainUser("u1", null, true);
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<AuditLogEntity>, FreeSqlEntityDAC<AuditLogEntity>>();
        services.AddSingleton<IEntityDAC<BusinessAuditEntity>, FreeSqlEntityDAC<BusinessAuditEntity>>();
        services.AddSingleton<IDomainUser>(user);
        services.AddScoped<AuditLogEntityDataService>();
        services.AddScoped<BusinessAuditEntityDataService>();
        services.AddScoped<IAuditFacetReader>(_ => new InvocationFacetReader(user, NullLogger<InvocationFacetReader>.Instance));
        services.AddScoped<IAuditFacetReader>(_ => new BusinessFacetReader(user, NullLogger<BusinessFacetReader>.Instance));

        var sp = services.BuildServiceProvider();
        user.ServiceProvider = sp;
        return new Host
        {
            Sp = sp,
            User = user,
            Facade = new AuditQueryService(user, NullLogger<AuditQueryService>.Instance),
            Export = new AuditExportService(user, NullLogger<AuditExportService>.Instance),
        };
    }

    private static async Task<AuditLogEntity> SeedAsync(ServiceProvider sp, string method, string correlationId)
    {
        var ds = sp.GetRequiredService<AuditLogEntityDataService>();
        var entity = new AuditLogEntity
        {
            UserId = "u1",
            UserName = "tester",
            ServiceName = "Svc",
            MethodName = method,
            ExecutionTime = DateTime.UtcNow,
            DurationMs = 7,
            Success = true,
            CorrelationId = correlationId,
        };
        await ds.EntityCreateAsync(entity);
        return entity;
    }

    [Fact]
    public async Task QueryAsync_Invocation_ReturnsRegisteredFacetRows()
    {
        using var fsql = CreateFsql();
        var host = Build(fsql);
        await SeedAsync(host.Sp, "DoA", "c-1");
        await SeedAsync(host.Sp, "DoB", "c-2");

        var page = await host.Facade.QueryAsync(AuditFacet.Invocation, new AuditFacetQuery());

        Assert.Equal(2, page.Total);
        Assert.All(page.Items, i => Assert.IsType<InvocationAuditView>(i));
        Assert.Equal(new[] { AuditFacet.Invocation, AuditFacet.Business }, host.Facade.GetAvailableFacets());
    }

    [Fact]
    public async Task QueryAsync_UnregisteredFacet_ThrowsFacetUnavailable()
    {
        using var fsql = CreateFsql();
        var host = Build(fsql);

        var ex = await Assert.ThrowsAsync<AuditFacetUnavailableException>(
            () => host.Facade.QueryAsync(AuditFacet.Security, new AuditFacetQuery()));

        Assert.Equal(AuditFacet.Security, ex.Facet);
        Assert.Equal("AUDIT_FACET_UNAVAILABLE", ex.ErrorCode);
    }

    [Fact]
    public async Task GetIncrementalAsync_AfterId_ReturnsOnlyNewerIds()
    {
        using var fsql = CreateFsql();
        var host = Build(fsql);
        var first = await SeedAsync(host.Sp, "Do1", "c-1");
        await SeedAsync(host.Sp, "Do2", "c-2");
        await SeedAsync(host.Sp, "Do3", "c-3");

        var rows = await host.Facade.GetIncrementalAsync(AuditFacet.Invocation, first.Id, 10);

        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.True(r.Id > first.Id));
    }

    [Fact]
    public async Task GetByCorrelationAsync_ReturnsOnlyRegisteredFacets()
    {
        using var fsql = CreateFsql();
        var host = Build(fsql);
        await SeedAsync(host.Sp, "DoA", "same");
        await SeedAsync(host.Sp, "DoB", "other");

        var result = await host.Facade.GetByCorrelationAsync("same");

        Assert.Single(result.Items);
        Assert.Equal(new[] { AuditFacet.Invocation, AuditFacet.Business }, result.ParticipatingFacets);
    }

    [Fact]
    public async Task ExportAsync_Csv_StreamsHeaderAndRows()
    {
        using var fsql = CreateFsql();
        var host = Build(fsql);
        await SeedAsync(host.Sp, "DoA", "c-1");
        await SeedAsync(host.Sp, "DoB", "c-2");

        using var ms = new MemoryStream();
        var count = await host.Export.ExportAsync(
            AuditFacet.Invocation, new AuditFacetQuery(), AuditExportFormat.Csv, ms);

        Assert.Equal(2, count);
        var text = Encoding.UTF8.GetString(ms.ToArray());
        Assert.StartsWith("Facet,Id,ActorType", text);
        Assert.Contains("DoA", text);
        Assert.Contains("DoB", text);
    }

    [Fact]
    public async Task IntegrityProvider_ComputeAndVerify_RoundTrips()
    {
        var provider = new DefaultAuditIntegrityProvider();
        var record = new InvocationAuditView(
            1, AuditActor.User("u1", "tester"), new AuditTarget("Svc", "Do"),
            "Do", "{}", "c-1", DateTimeOffset.UtcNow, "Svc", "Do", 7, true);

        var hash = await provider.ComputeHashAsync(record);

        Assert.Equal(64, hash.Length);
        Assert.True(await provider.VerifyAsync(record, hash));
        Assert.False(await provider.VerifyAsync(record, new string('0', 64)));
    }
}
