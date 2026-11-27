using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using Xunit;

namespace TKWF.Ext.AuditLogging.Tests;

public class BusinessAuditWriterTests
{
    private static IFreeSql CreateInMemoryFreeSql()
        => new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();

    private static ServiceProvider BuildProvider(IFreeSql fsql, bool registerDataService = true)
    {
        var services = new ServiceCollection();
        services.AddSingleton(fsql);
        services.AddSingleton(new UnitOfWorkManager(fsql));
        services.AddSingleton<IEntityDAC<BusinessAuditEntity>, FreeSqlEntityDAC<BusinessAuditEntity>>();
        services.AddSingleton<IDomainUser>(new StubDomainUser());
        if (registerDataService)
        {
            services.AddScoped<BusinessAuditEntityDataService>();
        }
        return services.BuildServiceProvider();
    }

    private static BusinessAuditWriter CreateWriter(
        IServiceProvider sp, RecordingFailureHook hook, AuditDiagnostics diagnostics, bool failOpen = true)
        => new(sp,
            NullLogger<BusinessAuditWriter>.Instance,
            new DefaultAuditMaskingPolicy(null),
            hook,
            diagnostics,
            Options.Create(new AuditFailurePolicyOptions { FailOpen = failOpen }));

    private static BusinessAuditEntityDataService CreateDataService(IFreeSql fsql)
        => new(new StubDomainUser(), new FreeSqlEntityDAC<BusinessAuditEntity>(new UnitOfWorkManager(fsql)));

    [Fact]
    public async Task SaveAsync_PersistsAndMasksBeforeAfter()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BusinessAuditEntity>();
        using var sp = BuildProvider(fsql);

        var hook = new RecordingFailureHook();
        var diagnostics = new AuditDiagnostics();
        var writer = CreateWriter(sp, hook, diagnostics);

        await writer.SaveAsync(new BusinessAuditEntry(
            Actor: AuditActor.Job("contract-expire"),
            Target: new AuditTarget("contract", "c-1"),
            ActionType: AuditActionType.StatusChanged,
            ExecutionTime: DateTimeOffset.UtcNow,
            Summary: "contract paused",
            Before: new Dictionary<string, object?> { ["password"] = "p@ss", ["name"] = "alice" },
            After: new Dictionary<string, object?> { ["status"] = "paused" },
            CorrelationId: "corr-w-1"), CancellationToken.None);

        var saved = await CreateDataService(fsql).EntityGetAsync(x => x.CorrelationId == "corr-w-1", CancellationToken.None);

        Assert.NotNull(saved);
        Assert.Equal("Job", saved!.ActorType);
        Assert.Equal("job:contract-expire", saved.ActorId);
        Assert.Equal("contract", saved.TargetType);
        Assert.Equal("c-1", saved.TargetId);
        Assert.Contains("***", saved.BeforeJson);
        Assert.DoesNotContain("p@ss", saved.BeforeJson);
        Assert.Contains("alice", saved.BeforeJson);
        Assert.Contains("paused", saved.AfterJson);
        Assert.Equal(0, diagnostics.Snapshot().WriteFailures);
        Assert.Empty(hook.Failures);
    }

    [Fact]
    public async Task SaveAsync_FailOpen_InvokesHookAndSwallows()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BusinessAuditEntity>();
        using var sp = BuildProvider(fsql, registerDataService: false);

        var hook = new RecordingFailureHook();
        var diagnostics = new AuditDiagnostics();
        var writer = CreateWriter(sp, hook, diagnostics, failOpen: true);

        await writer.SaveAsync(new BusinessAuditEntry(
            AuditActor.System(),
            new AuditTarget("merchant", "m-1"),
            AuditActionType.Created,
            DateTimeOffset.UtcNow,
            CorrelationId: "corr-w-2"), CancellationToken.None);

        Assert.Equal(1, diagnostics.Snapshot().WriteFailures);
        var failure = Assert.Single(hook.Failures);
        Assert.Equal(AuditFacet.Business, failure.Facet);
        Assert.Equal("corr-w-2", failure.CorrelationId);
    }

    [Fact]
    public async Task SaveAsync_FailClosed_ThrowsAfterHook()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BusinessAuditEntity>();
        using var sp = BuildProvider(fsql, registerDataService: false);

        var hook = new RecordingFailureHook();
        var diagnostics = new AuditDiagnostics();
        var writer = CreateWriter(sp, hook, diagnostics, failOpen: false);

        await Assert.ThrowsAnyAsync<Exception>(() => writer.SaveAsync(new BusinessAuditEntry(
            AuditActor.System(),
            new AuditTarget("merchant", "m-2"),
            AuditActionType.Created,
            DateTimeOffset.UtcNow), CancellationToken.None));

        Assert.Single(hook.Failures);
        Assert.Equal(1, diagnostics.Snapshot().WriteFailures);
    }

    [Fact]
    public async Task SaveAsync_NullActor_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BusinessAuditEntity>();
        using var sp = BuildProvider(fsql);
        var writer = CreateWriter(sp, new RecordingFailureHook(), new AuditDiagnostics());

        await Assert.ThrowsAsync<ArgumentNullException>(() => writer.SaveAsync(
            new BusinessAuditEntry(null!, new AuditTarget("merchant", "m"), AuditActionType.Created, DateTimeOffset.UtcNow),
            CancellationToken.None));
    }

    [Fact]
    public async Task SaveAsync_NonNumericTenant_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BusinessAuditEntity>();
        using var sp = BuildProvider(fsql);
        var writer = CreateWriter(sp, new RecordingFailureHook(), new AuditDiagnostics());

        await Assert.ThrowsAsync<ArgumentException>(() => writer.SaveAsync(
            new BusinessAuditEntry(AuditActor.System(), new AuditTarget("merchant", "m"), AuditActionType.Created, DateTimeOffset.UtcNow, TenantId: "abc"),
            CancellationToken.None));
    }

    private sealed class RecordingFailureHook : IAuditFailureHook
    {
        public List<AuditPersistFailure> Failures { get; } = new();

        public void OnPersistFailed(AuditPersistFailure failure) => Failures.Add(failure);
    }
}
