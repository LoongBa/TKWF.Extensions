using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Emailing.Tests;

/// <summary>
/// EmailRecordEntityDataService 测试（A5 Store-debt cleanup 后）——SQLite 内存库真实读写。
/// </summary>
public class FreeSqlEmailRecordStoreTests
{
    private static IFreeSql CreateInMemoryFreeSql()
        => new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();

    private static EmailRecordEntityDataService CreateDataService(IFreeSql fsql, IDomainUser user)
    {
        var uowManager = new UnitOfWorkManager(fsql);
        var dac = new FreeSqlEntityDAC<EmailRecordEntity>(uowManager);
        return new EmailRecordEntityDataService(user, dac);
    }

    private sealed class StubDomainUser : IDomainUser
    {
        public string SessionKey => "test-session";
        public bool IsAuthenticated => false;
        public bool IsSystemActor => false;
        public IUserInfo? UserInfo => null;
        public long? TenantId => null;
        public bool IsNoAuditActive => false;
        public string? UserId => null;
        public string? UserName => null;
        public bool IsInRole(string role) => false;
        public TDomainService Use<TDomainService>() where TDomainService : IDomainService => throw new NotSupportedException();
        public TService GetService<TService>() where TService : notnull => throw new NotSupportedException();
        public TService GetOptionalService<TService>() where TService : class => null!;
        public System.Collections.Generic.IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
    }

    [Fact]
    public async Task SaveAsync_NewRecord_PersistsToDatabase()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var store = CreateDataService(fsql, new StubDomainUser());

        var entity = new EmailRecordEntity
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Hello",
            Body = "World",
            IsHtml = false,
            Status = "Sent"
        };
        await store.UpsertAsync(entity, ct: TestContext.Current.CancellationToken);

        var list = await store.GetListByStatusAsync(null, ct: CancellationToken.None);
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
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var store = CreateDataService(fsql, new StubDomainUser());

        var entity = new EmailRecordEntity
        {
            To = "test@example.com",
            Subject = "Original",
            Status = "Pending"
        };
        await store.UpsertAsync(entity, ct: TestContext.Current.CancellationToken);

        var saved = (await store.GetListByStatusAsync(null, ct: CancellationToken.None))[0];
        saved.Status = "Sent";
        saved.SendTime = DateTime.Now;
        await store.UpsertAsync(saved, ct: TestContext.Current.CancellationToken);

        var list = await store.GetListByStatusAsync(null, ct: CancellationToken.None);
        Assert.Single(list);
        var updated = list[0];
        Assert.Equal("Sent", updated.Status);
        Assert.NotNull(updated.SendTime);
    }

    [Fact]
    public async Task GetAsync_ExistingRecord_ReturnsEntity()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var store = CreateDataService(fsql, new StubDomainUser());

        var entity = new EmailRecordEntity
        {
            To = "test@example.com",
            Subject = "Test Subject",
            Status = "Sent"
        };
        await store.UpsertAsync(entity, ct: TestContext.Current.CancellationToken);
        var id = (await store.GetListByStatusAsync(null, ct: CancellationToken.None))[0].Id;

        var result = await store.GetEntityByIdAsync(id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("Test Subject", result!.Subject);
        Assert.Equal("test@example.com", result.To);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var store = CreateDataService(fsql, new StubDomainUser());

        var result = await store.GetEntityByIdAsync(999, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetListAsync_MultipleRecords_ReturnsAll()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var store = CreateDataService(fsql, new StubDomainUser());

        await store.UpsertAsync(new EmailRecordEntity { To = "a@example.com", Subject = "A", Status = "Sent" }, ct: TestContext.Current.CancellationToken);
        await store.UpsertAsync(new EmailRecordEntity { To = "b@example.com", Subject = "B", Status = "Failed" }, ct: TestContext.Current.CancellationToken);
        await store.UpsertAsync(new EmailRecordEntity { To = "c@example.com", Subject = "C", Status = "Sent" }, ct: TestContext.Current.CancellationToken);

        var list = await store.GetListByStatusAsync(null, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task GetListAsync_WithStatusFilter_ReturnsFiltered()
    {
        using var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();
        var store = CreateDataService(fsql, new StubDomainUser());

        await store.UpsertAsync(new EmailRecordEntity { To = "a@example.com", Subject = "A", Status = "Sent" }, ct: TestContext.Current.CancellationToken);
        await store.UpsertAsync(new EmailRecordEntity { To = "b@example.com", Subject = "B", Status = "Failed" }, ct: TestContext.Current.CancellationToken);
        await store.UpsertAsync(new EmailRecordEntity { To = "c@example.com", Subject = "C", Status = "Sent" }, ct: TestContext.Current.CancellationToken);

        var list = await store.GetListByStatusAsync("Failed", ct: TestContext.Current.CancellationToken);
        Assert.Single(list);
        Assert.Equal("Failed", list[0].Status);
    }
}
