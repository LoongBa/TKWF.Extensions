using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// BlobRecordEntityDataService 测试（A5 Store-debt cleanup 后）——经生产路径（真实 DI + DomainUser +
/// <c>User.Use&lt;BlobRecordEntityDataService&gt;()</c>）覆盖记录 CRUD。
/// </summary>
public class FreeSqlBlobRecordStoreTests
{
    private static IFreeSql CreateInMemoryFreeSql()
        => new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();

    private static (ServiceProvider Provider, DomainUser<TestUserInfo> User) CreateProductionHost()
    {
        var services = new ServiceCollection();
        new BlobStoringExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<BlobRecordEntity>, FreeSqlEntityDAC<BlobRecordEntity>>();
        var provider = services.BuildServiceProvider();
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };
        return (provider, user);
    }

    [Fact]
    public async Task SaveAsync_NewRecord_PersistsToDatabase()
    {
        var (_, user) = CreateProductionHost();
        var store = user.Use<BlobRecordEntityDataService>();

        var record = new BlobRecordEntity
        {
            Name = "photo.png",
            Path = "abc123/photo.png",
            ContentType = "image/png",
            Size = 1024,
            UploaderName = "testuser"
        };
        await store.UpsertAsync(record, ct: TestContext.Current.CancellationToken);

        var list = await store.GetListByContentTypeAsync(null, 0, 20, ct: CancellationToken.None);
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
        var (_, user) = CreateProductionHost();
        var store = user.Use<BlobRecordEntityDataService>();

        var record = new BlobRecordEntity
        {
            Name = "doc.pdf",
            Path = "abc/doc.pdf",
            ContentType = "application/pdf",
            Size = 2048
        };
        await store.UpsertAsync(record, ct: TestContext.Current.CancellationToken);

        var saved = (await store.GetListByContentTypeAsync(null, 0, 20, ct: CancellationToken.None))[0];
        var originalId = saved.Id;
        saved.Size = 4096;
        await store.UpsertAsync(saved, ct: TestContext.Current.CancellationToken);

        var list = await store.GetListByContentTypeAsync(null, 0, 20, ct: CancellationToken.None);
        Assert.Single(list);
        var updated = list[0];
        Assert.Equal(originalId, updated.Id);
        Assert.Equal(4096, updated.Size);
    }

    [Fact]
    public async Task GetAsync_ExistingRecord_ReturnsEntity()
    {
        var (_, user) = CreateProductionHost();
        var store = user.Use<BlobRecordEntityDataService>();

        var record = new BlobRecordEntity
        {
            Name = "video.mp4",
            Path = "def/video.mp4",
            ContentType = "video/mp4",
            Size = 1048576
        };
        await store.UpsertAsync(record, ct: TestContext.Current.CancellationToken);
        var id = (await store.GetListByContentTypeAsync(null, 0, 20, ct: CancellationToken.None))[0].Id;

        var result = await store.GetEntityByIdAsync(id, ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("video.mp4", result!.Name);
        Assert.Equal("video/mp4", result.ContentType);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        var (_, user) = CreateProductionHost();
        var store = user.Use<BlobRecordEntityDataService>();

        var result = await store.GetEntityByIdAsync(999, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetByNameAsync_ExistingRecord_ReturnsEntity()
    {
        var (_, user) = CreateProductionHost();
        var store = user.Use<BlobRecordEntityDataService>();

        await store.UpsertAsync(new BlobRecordEntity
        {
            Name = "image.jpg",
            Path = "ghi/image.jpg",
            ContentType = "image/jpeg"
        }, ct: TestContext.Current.CancellationToken);

        var result = await store.GetByNameAsync("image.jpg", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("image.jpg", result!.Name);
    }

    [Fact]
    public async Task GetListAsync_MultipleRecords_ReturnsAll()
    {
        var (_, user) = CreateProductionHost();
        var store = user.Use<BlobRecordEntityDataService>();

        await store.UpsertAsync(new BlobRecordEntity { Name = "a.txt", Path = "a/a.txt", ContentType = "text/plain" }, ct: TestContext.Current.CancellationToken);
        await store.UpsertAsync(new BlobRecordEntity { Name = "b.png", Path = "b/b.png", ContentType = "image/png" }, ct: TestContext.Current.CancellationToken);
        await store.UpsertAsync(new BlobRecordEntity { Name = "c.pdf", Path = "c/c.pdf", ContentType = "application/pdf" }, ct: TestContext.Current.CancellationToken);

        var list = await store.GetListByContentTypeAsync(null, 0, 20, ct: TestContext.Current.CancellationToken);

        Assert.Equal(3, list.Count);
    }

    [Fact]
    public void Use_BlobRecordDataService_ResolvesInstance()
    {
        var (_, user) = CreateProductionHost();
        var s1 = user.Use<BlobRecordEntityDataService>();
        var s2 = user.Use<BlobRecordEntityDataService>();

        Assert.NotNull(s1);
        Assert.NotNull(s2);
    }
}
