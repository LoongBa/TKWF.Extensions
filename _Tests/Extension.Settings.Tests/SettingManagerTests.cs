using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings.Tests;

/// <summary>
/// SettingManager 测试——覆盖分层优先级、默认值、JSON 序列化、匿名降级、缓存命中/失效、Options。
/// <para>V0.3.0（领域自治根治，正确路线）：</para>
/// <list type="bullet">
/// <item><strong>集成测试走生产路径</strong>——真实 DI（扩展 ConfigureServices + FreeSql 基础设施 +
///     真实 <see cref="DomainUser{TUserInfo}"/>）+ <c>User.Use&lt;ISettingManager&gt;()</c> 解析
///     （AOP 路径：设 CurrentAopUser → GetRequiredService → AddConstructibleService 构造工厂 → SettingManager）；</item>
/// <item><strong>分层逻辑单测</strong>——可配置 stub 用户直构 Manager（继承 DomainServiceBase，
///     <see cref="DomainServiceBase.User"/> 上下文可精确控制 User/Tenant/匿名）。</item>
/// </list>
/// </summary>
public class SettingManagerTests
{
    private static IFreeSql CreateInMemoryFreeSql()
    {
        return new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
    }

    // ──────────────────────────────────────────────
    // 生产路径集成测试（真实 DomainUser + Use<T>()）
    // ──────────────────────────────────────────────

    private static (ServiceProvider Provider, DomainUser<TestUserInfo> User) CreateProductionHost()
    {
        var services = new ServiceCollection();
        // 1. 扩展装配——生产形态（消费方白名单启用后三钩子执行 ConfigureServices）
        new SettingsExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        services.AddLogging();
        // 2. FreeSql 基础设施（消费方 DomainHost 等价注册）
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<SettingEntity>, FreeSqlEntityDAC<SettingEntity>>();
        // 3. 解析作用域绑定（生产经 DomainHost.NewDomainContext 绑定 AsyncLocal——测试等价 BindScope）
        var provider = services.BuildServiceProvider();
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };
        return (provider, user);
    }

    [Fact]
    public async Task Use_ISettingManager_Anonymous_WritesAndReadsGlobalLayer()
    {
        var (provider, user) = CreateProductionHost();

        // 生产路径：User.Use<ISettingManager>() —— AOP 路径解析（匿名 → 写入/读取 Global 层）
        var manager = user.Use<ISettingManager>();
        await manager.SetAsync("Theme", "dark", TestContext.Current.CancellationToken);

        var result = await manager.GetAsync("Theme", "", TestContext.Current.CancellationToken);
        Assert.Equal("dark", result);

        // 落库断言：匿名 → Global 层
        var fsql = provider.GetRequiredService<IFreeSql>();
        var saved = await fsql.Select<SettingEntity>()
            .Where(s => s.Name == "Theme")
            .FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.Equal("Global", saved!.ProviderName);
        Assert.Null(saved.ProviderKey);
        Assert.Equal("dark", saved.Value);
    }

    [Fact]
    public async Task Use_ISettingManager_OverwritesPreviousValue()
    {
        var (_, user) = CreateProductionHost();
        var manager = user.Use<ISettingManager>();

        await manager.SetAsync("Color", "red", TestContext.Current.CancellationToken);
        await manager.SetAsync("Color", "blue", TestContext.Current.CancellationToken);

        Assert.Equal("blue", await manager.GetAsync("Color", "", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Use_ISettingManager_NonExistent_ReturnsDefault()
    {
        var (_, user) = CreateProductionHost();
        var manager = user.Use<ISettingManager>();

        Assert.Equal("fallback", await manager.GetAsync("NotExist", "fallback", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Use_ISettingManager_EmptyDefault_ReturnsEmptyString()
    {
        var (_, user) = CreateProductionHost();
        var manager = user.Use<ISettingManager>();

        Assert.Equal("", await manager.GetAsync("NotExist", "", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task Use_ISettingManager_Typed_ReturnsDeserialized()
    {
        var (_, user) = CreateProductionHost();
        var manager = user.Use<ISettingManager>();

        await manager.SetAsync("MaxRetries", 3, TestContext.Current.CancellationToken);
        var result = await manager.GetAsync("MaxRetries", 0, TestContext.Current.CancellationToken);

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task Use_ISettingManager_Typed_InvalidJson_ReturnsDefault()
    {
        var (_, user) = CreateProductionHost();
        var manager = user.Use<ISettingManager>();

        await manager.SetAsync("Broken", "not-a-json", TestContext.Current.CancellationToken);
        var result = await manager.GetAsync("Broken", 42, TestContext.Current.CancellationToken);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task Use_ISettingManager_Typed_ComplexObject_Deserializes()
    {
        var (_, user) = CreateProductionHost();
        var manager = user.Use<ISettingManager>();

        await manager.SetAsync("Layout", new TestLayout { Columns = 3, Rows = 2 }, TestContext.Current.CancellationToken);
        var result = await manager.GetAsync("Layout", new TestLayout(), TestContext.Current.CancellationToken);

        Assert.Equal(3, result.Columns);
        Assert.Equal(2, result.Rows);
    }

    [Fact]
    public async Task Use_ISettingManager_Typed_NullableInt_ReturnsDefault()
    {
        var (_, user) = CreateProductionHost();
        var manager = user.Use<ISettingManager>();

        var result = await manager.GetAsync<int?>("NonExistent", null, TestContext.Current.CancellationToken);
        Assert.Null(result);
    }

    [Fact]
    public async Task Use_ISettingManager_MultipleSettings_Independent()
    {
        var (_, user) = CreateProductionHost();
        var manager = user.Use<ISettingManager>();

        await manager.SetAsync("A", "1", TestContext.Current.CancellationToken);
        await manager.SetAsync("B", "2", TestContext.Current.CancellationToken);

        Assert.Equal("1", await manager.GetAsync("A", "", TestContext.Current.CancellationToken));
        Assert.Equal("2", await manager.GetAsync("B", "", TestContext.Current.CancellationToken));
    }

    [Fact]
    public void Use_ISettingManager_ResolvesInstance()
    {
        // 生产路径解析语义：Use<T>() 接口 AOP 路径经 DI 构造工厂创建实例——验证解析链路可重复
        var (_, user) = CreateProductionHost();
        var m1 = user.Use<ISettingManager>();
        var m2 = user.Use<ISettingManager>();

        Assert.NotNull(m1);
        Assert.NotNull(m2);
    }

    // ──────────────────────────────────────────────
    // 分层逻辑单测（stub 用户精确控制 User/Tenant/匿名）
    // ──────────────────────────────────────────────

    private static SettingManager CreateManager(
        StubDomainUser? user = null,
        int cacheExpirationSeconds = 300)
    {
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        user ??= new StubDomainUser();

        // DataService 能力注册（stub 经 Use<T> 懒加载——生产 NoAop 路径的等价注册）
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<SettingEntity>, FreeSqlEntityDAC<SettingEntity>>();
        user.ServiceProvider = services.BuildServiceProvider();

        var options = Options.Create(new SettingsOptions { CacheExpirationSeconds = cacheExpirationSeconds });
        var cache = new MemoryCache(new MemoryCacheOptions());
        return new SettingManager(user, options, cache, new FakeLogger<SettingManager>());
    }

    /// <summary>经 DataService 直接写入指定层（绕过 Manager 的 User 层写入逻辑）——生产路径等价。</summary>
    private static async Task WriteSettingAsync(StubDomainUser user, string name, string value, string providerName, string? providerKey)
    {
        var ds = user.Use<SettingEntityDataService>();
        var now = DateTimeOffset.Now;
        await ds.UpsertByKeyAsync(new SettingEntity
        {
            Name = name,
            Value = value,
            ProviderName = providerName,
            ProviderKey = providerKey,
            Description = null,
            IsVisibleToClients = true,
            CreateTime = now,
            UpdateTime = now
        }, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task GetAsync_UserLayer_ReturnsUserValue()
    {
        // Arrange: 写入 User 层（有用户时 SetAsync 自动写 User 层）
        var user = new StubDomainUser(userId: "user-42", isAuthenticated: true);
        var manager = CreateManager(user);
        await manager.SetAsync("Theme", "dark", TestContext.Current.CancellationToken);

        // Act
        var result = await manager.GetAsync("Theme", "light", TestContext.Current.CancellationToken);

        // Assert: 命中 User 层
        Assert.Equal("dark", result);
    }

    [Fact]
    public async Task GetAsync_TenantLayer_FallbackFromUser()
    {
        // Arrange: 写入 Tenant 层
        var user = new StubDomainUser(userId: "user-1", tenantId: 100, isAuthenticated: true);
        var manager = CreateManager(user);
        await WriteSettingAsync(user, "Logo", "tenant-logo", "Tenant", "100");

        // Act
        var result = await manager.GetAsync("Logo", "default", TestContext.Current.CancellationToken);

        // Assert: User 层未命中，回退到 Tenant 层
        Assert.Equal("tenant-logo", result);
    }

    [Fact]
    public async Task GetAsync_GlobalLayer_FallbackFromTenant()
    {
        // Arrange: 仅写入 Global 层
        var user = new StubDomainUser(userId: "user-1", tenantId: 100, isAuthenticated: true);
        var manager = CreateManager(user);
        await WriteSettingAsync(user, "Slogan", "global-slogan", "Global", null);

        // Act
        var result = await manager.GetAsync("Slogan", "default", TestContext.Current.CancellationToken);

        // Assert: User/Tenant 层未命中，回退到 Global 层
        Assert.Equal("global-slogan", result);
    }

    [Fact]
    public async Task GetAsync_AllLayers_ReturnsDefault()
    {
        // Arrange: 无任何设置
        var user = new StubDomainUser(userId: "user-1", tenantId: 100, isAuthenticated: true);
        var manager = CreateManager(user);

        // Act
        var result = await manager.GetAsync("Missing", "myDefault", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("myDefault", result);
    }

    [Fact]
    public async Task GetAsync_UserOverridesTenant()
    {
        // Arrange: 同名设置在 User 和 Tenant 层都有值
        var user = new StubDomainUser(userId: "user-1", tenantId: 100, isAuthenticated: true);
        var manager = CreateManager(user);
        await WriteSettingAsync(user, "FontSize", "14", "Tenant", "100");
        await WriteSettingAsync(user, "FontSize", "18", "User", "user-1");

        // Act
        var result = await manager.GetAsync("FontSize", "12", TestContext.Current.CancellationToken);

        // Assert: User 层优先
        Assert.Equal("18", result);
    }

    [Fact]
    public async Task GetAsync_TenantOverridesGlobal()
    {
        // Arrange: 同名设置在 Tenant 和 Global 层都有值
        var user = new StubDomainUser(userId: "user-1", tenantId: 100, isAuthenticated: true);
        var manager = CreateManager(user);
        await WriteSettingAsync(user, "Lang", "en", "Global", null);
        await WriteSettingAsync(user, "Lang", "zh-CN", "Tenant", "100");

        // Act: User 层无设置，Tenant 层有
        var result = await manager.GetAsync("Lang", "en", TestContext.Current.CancellationToken);

        // Assert: 命中 Tenant 层
        Assert.Equal("zh-CN", result);
    }

    [Fact]
    public async Task GetAsync_Anonymous_SkipsUserAndTenant()
    {
        // Arrange: 匿名用户，User 和 Tenant 层有值
        var user = new StubDomainUser(isAuthenticated: false);
        var manager = CreateManager(user);
        await WriteSettingAsync(user, "Theme", "user-theme", "User", "user-1");
        await WriteSettingAsync(user, "Theme", "tenant-theme", "Tenant", "100");
        await WriteSettingAsync(user, "Theme", "global-theme", "Global", null);

        // Act
        var result = await manager.GetAsync("Theme", "default", TestContext.Current.CancellationToken);

        // Assert: 匿名用户跳过 User/Tenant，命中 Global 层
        Assert.Equal("global-theme", result);
    }

    [Fact]
    public async Task GetAsync_Anonymous_NoGlobal_ReturnsDefault()
    {
        // Arrange: 匿名用户，Global 层也无设置
        var user = new StubDomainUser(isAuthenticated: false);
        var manager = CreateManager(user);

        // Act
        var result = await manager.GetAsync("Theme", "default", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("default", result);
    }

    [Fact]
    public async Task SetAsync_Anonymous_WritesToGlobalLayer()
    {
        // Arrange: 匿名用户
        var user = new StubDomainUser(isAuthenticated: false);
        var manager = CreateManager(user);

        // Act
        await manager.SetAsync("Theme", "dark", TestContext.Current.CancellationToken);

        // Assert: 写入 Global 层
        var saved = await user.Use<SettingEntityDataService>()
            .GetByKeyAsync("Theme", "Global", null, TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.Equal("dark", saved!.Value);
    }

    [Fact]
    public async Task SetAsync_Authenticated_WritesToUserLayer()
    {
        // Arrange: 已认证用户
        var user = new StubDomainUser(userId: "user-42", isAuthenticated: true);
        var manager = CreateManager(user);

        // Act
        await manager.SetAsync("Theme", "dark", TestContext.Current.CancellationToken);

        // Assert: 写入 User 层
        var saved = await user.Use<SettingEntityDataService>()
            .GetByKeyAsync("Theme", "User", "user-42", TestContext.Current.CancellationToken);
        Assert.NotNull(saved);
        Assert.Equal("dark", saved!.Value);
    }

    // ── 缓存命中/失效（DB 状态验证——不依赖计数桩） ──

    [Fact]
    public async Task GetAsync_CacheHit_DoesNotReReadDatabase()
    {
        // Arrange: 匿名用户，Global 层有值
        var user = new StubDomainUser();
        var manager = CreateManager(user);
        var fsql = user.ServiceProvider!.GetRequiredService<IFreeSql>();
        await WriteSettingAsync(user, "Theme", "dark", "Global", null);

        // Act: 首次读取（缓存写入）
        Assert.Equal("dark", await manager.GetAsync("Theme", "", TestContext.Current.CancellationToken));

        // 直接删除 DB 记录——若重读 DB 应返回默认值；缓存命中则仍返回旧值
        await fsql.Delete<SettingEntity>().Where(s => s.Name == "Theme").ExecuteAffrowsAsync(TestContext.Current.CancellationToken);
        Assert.Equal("dark", await manager.GetAsync("Theme", "", TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task GetAsync_NullResult_CachedAsDefault_SurvivesDbInsert()
    {
        // Arrange: 匿名用户，设置不存在
        var user = new StubDomainUser();
        var manager = CreateManager(user);

        // Act: 首次读取（null 缓存为 NotFoundSentinel）
        Assert.Equal("fallback", await manager.GetAsync("Missing", "fallback", TestContext.Current.CancellationToken));

        // DB 直接插入——null 已缓存，不穿透
        await WriteSettingAsync(user, "Missing", "now-exists", "Global", null);
        Assert.Equal("fallback", await manager.GetAsync("Missing", "fallback", TestContext.Current.CancellationToken));

        // SetAsync 失效缓存后重新读取到新值
        await manager.SetAsync("Missing", "fresh", TestContext.Current.CancellationToken);
        Assert.Equal("fresh", await manager.GetAsync("Missing", "fallback", TestContext.Current.CancellationToken));
    }

    // ── Options ──

    [Fact]
    public void Options_DefaultCacheExpirationSeconds_Is300()
    {
        var options = new SettingsOptions();
        Assert.Equal(300, options.CacheExpirationSeconds);
    }

    [Fact]
    public void Options_CustomCacheExpirationSeconds_IsRespected()
    {
        var options = new SettingsOptions { CacheExpirationSeconds = 60 };
        Assert.Equal(60, options.CacheExpirationSeconds);
    }

    // ── Test helpers ──

    private sealed class TestLayout
    {
        public int Columns { get; set; }
        public int Rows { get; set; }
    }

    /// <summary>
    /// 可配置 stub 用户（User/Tenant/匿名精确控制）——继承式 Manager 经基类 <see cref="DomainServiceBase.User"/>
    /// 读取上下文；<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价（ActivatorUtilities 直建，IDomainUser 参数显式传 this，
    /// 其余从 DI 解析——经注入的 ServiceProvider）。
    /// </summary>
    private sealed class StubDomainUser : IDomainUser
    {
        private readonly string? _userId;
        private readonly long? _tenantId;
        private readonly bool _isAuthenticated;
        private IServiceProvider? _provider;

        public StubDomainUser(
            string? userId = null,
            long? tenantId = null,
            bool isAuthenticated = false)
        {
            _userId = userId;
            _tenantId = tenantId;
            _isAuthenticated = isAuthenticated;
        }

        /// <summary>ServiceProvider（测试工厂注册时注入——Use&lt;T&gt; 解析源）。</summary>
        public IServiceProvider? ServiceProvider { set => _provider = value; get => _provider; }

        public string SessionKey => "test-session";
        public bool IsAuthenticated => _isAuthenticated;
        public bool IsSystemActor => false;
        public IUserInfo? UserInfo => null;
        public long? TenantId => _tenantId;
        public bool IsNoAuditActive => false;
        public string? UserId => _userId;
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
