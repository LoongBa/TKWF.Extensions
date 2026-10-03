using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// BlobRecordStore 测试——覆盖记录 CRUD + 异常静默。
/// <para>V4.10.53（领域自治根治，正确路线——对齐 Extension.Settings.Tests）：</para>
/// <list type="bullet">
/// <item><strong>集成测试走生产路径</strong>——真实 DI（扩展 ConfigureServices + FreeSql 基础设施 +
///     真实 <see cref="DomainUser{TUserInfo}"/>）+ <c>User.Use&lt;IBlobRecordStore&gt;()</c> 解析
///     （AOP 路径：设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂 → BlobRecordStore）；</item>
/// <item><strong>异常静默单测</strong>——可配置 stub 用户直构 Store（继承 DomainServiceBase，
///     <see cref="DomainServiceBase.User"/> 上下文 + FakeLogger 捕获 Warning），对齐分层逻辑单测模式。</item>
/// </list>
/// </summary>
public class FreeSqlBlobRecordStoreTests
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（每次调用新连接 = 独立内存库）。</summary>
    private static IFreeSql CreateInMemoryFreeSql()
    {
        return new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
    }

    // ──────────────────────────────────────────────
    // 生产路径集成测试（真实 DomainUser + User.Use<IBlobRecordStore>()）
    // ──────────────────────────────────────────────

    private static (ServiceProvider Provider, DomainUser<TestUserInfo> User) CreateProductionHost()
    {
        var services = new ServiceCollection();
        // 1. 扩展装配——生产形态（消费方白名单启用后三钩子执行 ConfigureServices）
        new BlobStoringExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        // 2. FreeSql 基础设施（消费方 DomainHost 等价注册）
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<BlobRecordEntity>, FreeSqlEntityDAC<BlobRecordEntity>>();
        // 3. 解析作用域绑定（生产经 DomainHost.NewDomainContext 绑定 AsyncLocal——测试等价 BindScope）
        var provider = services.BuildServiceProvider();
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };
        return (provider, user);
    }

    [Fact]
    public async Task SaveAsync_NewRecord_PersistsToDatabase()
    {
        // Arrange — 生产路径：User.Use<IBlobRecordStore>() AOP 解析（守卫工厂）
        var (_, user) = CreateProductionHost();
        var store = user.Use<IBlobRecordStore>();

        var record = new BlobRecordEntity
        {
            Name = "photo.png",
            Path = "abc123/photo.png",
            ContentType = "image/png",
            Size = 1024,
            UploaderName = "testuser"
        };

        // Act
        await store.SaveAsync(record);

        // Assert
        var list = await store.GetListAsync(ct: CancellationToken.None);
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
        // Arrange
        var (_, user) = CreateProductionHost();
        var store = user.Use<IBlobRecordStore>();

        var record = new BlobRecordEntity
        {
            Name = "doc.pdf",
            Path = "abc/doc.pdf",
            ContentType = "application/pdf",
            Size = 2048
        };
        await store.SaveAsync(record);

        var saved = (await store.GetListAsync(ct: CancellationToken.None))[0];
        var originalId = saved.Id;
        saved.Size = 4096;

        // Act
        await store.SaveAsync(saved);

        // Assert — 改造后 Upsert 保留自增 Id（不再先删后插）
        var list = await store.GetListAsync(ct: CancellationToken.None);
        Assert.Single(list);
        var updated = list[0];
        Assert.Equal(originalId, updated.Id);
        Assert.Equal(4096, updated.Size);
    }

    [Fact]
    public async Task GetAsync_ExistingRecord_ReturnsEntity()
    {
        // Arrange
        var (_, user) = CreateProductionHost();
        var store = user.Use<IBlobRecordStore>();

        var record = new BlobRecordEntity
        {
            Name = "video.mp4",
            Path = "def/video.mp4",
            ContentType = "video/mp4",
            Size = 1048576
        };
        await store.SaveAsync(record);
        var id = (await store.GetListAsync(ct: CancellationToken.None))[0].Id;

        // Act
        var result = await store.GetAsync(id);

        // Assert
        Assert.NotNull(result);
        Assert.Equal("video.mp4", result!.Name);
        Assert.Equal("video/mp4", result.ContentType);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var (_, user) = CreateProductionHost();
        var store = user.Use<IBlobRecordStore>();

        // Act
        var result = await store.GetAsync(999);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetByNameAsync_ExistingRecord_ReturnsEntity()
    {
        // Arrange
        var (_, user) = CreateProductionHost();
        var store = user.Use<IBlobRecordStore>();

        await store.SaveAsync(new BlobRecordEntity
        {
            Name = "image.jpg",
            Path = "ghi/image.jpg",
            ContentType = "image/jpeg"
        });

        // Act
        var result = await store.GetByNameAsync("image.jpg");

        // Assert
        Assert.NotNull(result);
        Assert.Equal("image.jpg", result!.Name);
    }

    [Fact]
    public async Task GetListAsync_MultipleRecords_ReturnsAll()
    {
        // Arrange
        var (_, user) = CreateProductionHost();
        var store = user.Use<IBlobRecordStore>();

        await store.SaveAsync(new BlobRecordEntity { Name = "a.txt", Path = "a/a.txt", ContentType = "text/plain" });
        await store.SaveAsync(new BlobRecordEntity { Name = "b.png", Path = "b/b.png", ContentType = "image/png" });
        await store.SaveAsync(new BlobRecordEntity { Name = "c.pdf", Path = "c/c.pdf", ContentType = "application/pdf" });

        // Act
        var list = await store.GetListAsync();

        // Assert
        Assert.Equal(3, list.Count);
    }

    [Fact]
    public async Task SaveAsync_NullRecord_DoesNotThrow()
    {
        // Arrange
        var (_, user) = CreateProductionHost();
        var store = user.Use<IBlobRecordStore>();

        // Act — null record should be silently skipped
        await store.SaveAsync(null!);

        // Assert — no records created
        Assert.Empty(await store.GetListAsync(ct: CancellationToken.None));
    }

    [Fact]
    public void Use_IBlobRecordStore_ResolvesInstance()
    {
        // 生产路径解析语义：Use<T>() 接口 AOP 路径经 DI 守卫工厂创建实例——验证解析链路可重复
        var (_, user) = CreateProductionHost();
        var s1 = user.Use<IBlobRecordStore>();
        var s2 = user.Use<IBlobRecordStore>();

        Assert.NotNull(s1);
        Assert.NotNull(s2);
    }

    // ──────────────────────────────────────────────
    // 异常静默单测（stub 用户 + FakeLogger 直构 Store——分层逻辑单测模式）
    // ──────────────────────────────────────────────

    /// <summary>
    /// 直构 BlobRecordStore（继承 DomainServiceBase，经基类 User 读上下文）——stub 的 Use&lt;T&gt;()
    /// 按生产 NoAop 路径等价（ActivatorUtilities 直建 BlobRecordEntityDataService，IDomainUser 显式传 this，
    /// IEntityDAC 从 DI 解析）。
    /// </summary>
    private static BlobRecordStore CreateStore(IFreeSql fsql, FakeLogger<BlobRecordStore> logger)
    {
        var stub = new StubDomainUser();
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<BlobRecordEntity>, FreeSqlEntityDAC<BlobRecordEntity>>();
        stub.ServiceProvider = services.BuildServiceProvider();
        return new BlobRecordStore(stub, logger);
    }

    [Fact]
    public async Task SaveAsync_ExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        // Arrange — 使用已 Dispose 的 FreeSql，操作必定抛异常
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<BlobRecordEntity>();
        var logger = new FakeLogger<BlobRecordStore>();
        var store = CreateStore(fsql, logger);

        fsql.Dispose();

        var record = new BlobRecordEntity { Name = "test.txt", Path = "test.txt" };

        // Act — should not throw
        await store.SaveAsync(record);

        // Assert — logger captured warning
        Assert.Single(logger.Warnings);
        Assert.Contains("Blob 记录保存失败", logger.Warnings[0]);
    }

    // ── Test helpers ──

    /// <summary>
    /// 可配置 stub 用户——继承式 Store 经基类 <see cref="DomainServiceBase.User"/> 读取上下文；
    /// <c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价（ActivatorUtilities 直建，IDomainUser 参数显式传 this，
    /// 其余从 DI 解析——经注入的 ServiceProvider）。
    /// </summary>
    private sealed class StubDomainUser : IDomainUser
    {
        private IServiceProvider? _provider;

        /// <summary>ServiceProvider（测试工厂注册时注入——Use&lt;T&gt; 解析源）。</summary>
        public IServiceProvider? ServiceProvider { set => _provider = value; get => _provider; }

        public string SessionKey => "test-session";
        public bool IsAuthenticated => false;
        public bool IsSystemActor => false;
        public IUserInfo? UserInfo => null;
        public long? TenantId => null;
        public bool IsNoAuditActive => false;
        public string? UserId => null;
        public string? UserName => "test";
        public bool IsInRole(string role) => false;

        public TDomainService Use<TDomainService>() where TDomainService : IDomainService
        {
            // 生产 NoAop 路径等价：ActivatorUtilities 直建，IDomainUser 参数显式传 this，其余从 DI 解析
            if (_provider is null)
                throw new NotSupportedException("Stub: Use<T> 未注入 ServiceProvider");
            return (TDomainService)ActivatorUtilities.CreateInstance(_provider, typeof(TDomainService), this);
        }

        public TService GetService<TService>() where TService : notnull
        {
            if (_provider is null)
                throw new NotSupportedException("Stub: GetService<T> 未注入 ServiceProvider");
            return _provider.GetRequiredService<TService>();
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
