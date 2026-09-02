using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings.Tests;

/// <summary>
/// SettingManager 测试——覆盖分层优先级、默认值、JSON 序列化、匿名降级、缓存命中/失效、Options。
/// <para>V0.2.0：完整分层（User → Tenant → Global → 默认值）+ IMemoryCache 读缓存。</para>
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

    private static SettingManager CreateManager(
        ISettingStore? store = null,
        IDomainUser? user = null,
        string provider = "Global",
        IMemoryCache? cache = null,
        int cacheExpirationSeconds = 300)
    {
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        store ??= new FreeSqlSettingStore(fsql, new FakeLogger<FreeSqlSettingStore>());
        user ??= new StubDomainUser();
        var options = Options.Create(new SettingsOptions
        {
            DefaultSettingValueProvider = provider,
            CacheExpirationSeconds = cacheExpirationSeconds
        });
        cache ??= new MemoryCache(new MemoryCacheOptions());
        return new SettingManager(store, user, options, cache, new FakeLogger<SettingManager>());
    }

    // ── V0.1.0 原有测试（保持向后兼容） ──

    [Fact]
    public async Task GetAsync_ExistingSetting_ReturnsValue()
    {
        var manager = CreateManager();
        await manager.SetAsync("Theme", "dark", CancellationToken.None);

        var result = await manager.GetAsync("Theme", "", CancellationToken.None);

        Assert.Equal("dark", result);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsDefault()
    {
        var manager = CreateManager();

        var result = await manager.GetAsync("NotExist", "fallback", CancellationToken.None);

        Assert.Equal("fallback", result);
    }

    [Fact]
    public async Task GetAsync_EmptyDefault_ReturnsEmptyString()
    {
        var manager = CreateManager();

        var result = await manager.GetAsync("NotExist", "", CancellationToken.None);

        Assert.Equal("", result);
    }

    [Fact]
    public async Task SetAsync_OverwritesPreviousValue()
    {
        var manager = CreateManager();
        await manager.SetAsync("Color", "red", CancellationToken.None);

        await manager.SetAsync("Color", "blue", CancellationToken.None);
        var result = await manager.GetAsync("Color", "", CancellationToken.None);

        Assert.Equal("blue", result);
    }

    [Fact]
    public async Task GetAsync_Typed_ReturnsDeserialized()
    {
        var manager = CreateManager();
        await manager.SetAsync("MaxRetries", "3", CancellationToken.None);

        var result = await manager.GetAsync("MaxRetries", 0, CancellationToken.None);

        Assert.Equal(3, result);
    }

    [Fact]
    public async Task GetAsync_Typed_InvalidJson_ReturnsDefault()
    {
        var manager = CreateManager();
        await manager.SetAsync("Broken", "not-a-json", CancellationToken.None);

        var result = await manager.GetAsync("Broken", 42, CancellationToken.None);

        Assert.Equal(42, result);
    }

    [Fact]
    public async Task SetAsync_Typed_SerializesToJSON()
    {
        var manager = CreateManager();

        await manager.SetAsync("Config", new { Width = 1920, Height = 1080 }, CancellationToken.None);

        var raw = await manager.GetAsync("Config", "", CancellationToken.None);
        Assert.Contains("1920", raw);
        Assert.Contains("1080", raw);
    }

    [Fact]
    public async Task GetAsync_Typed_ComplexObject_Deserializes()
    {
        var manager = CreateManager();
        await manager.SetAsync("Layout", new TestLayout { Columns = 3, Rows = 2 }, CancellationToken.None);

        var result = await manager.GetAsync("Layout", new TestLayout(), CancellationToken.None);

        Assert.Equal(3, result.Columns);
        Assert.Equal(2, result.Rows);
    }

    [Fact]
    public async Task GetAsync_Typed_NullableInt_ReturnsDefault()
    {
        var manager = CreateManager();

        var result = await manager.GetAsync<int?>("NonExistent", null, CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_MultipleSettings_Independent()
    {
        var manager = CreateManager();
        await manager.SetAsync("A", "1", CancellationToken.None);
        await manager.SetAsync("B", "2", CancellationToken.None);

        var a = await manager.GetAsync("A", "", CancellationToken.None);
        var b = await manager.GetAsync("B", "", CancellationToken.None);

        Assert.Equal("1", a);
        Assert.Equal("2", b);
    }

    // ── V0.2.0 新增测试：分层回退 ──

    [Fact]
    public async Task GetAsync_UserLayer_ReturnsUserValue()
    {
        // Arrange: 写入 User 层（有用户时 SetAsync 自动写 User 层）
        var user = new StubDomainUser(userId: "user-42", tenantId: 1, isAuthenticated: true);
        var manager = CreateManager(user: user);
        await manager.SetAsync("Theme", "dark", CancellationToken.None);

        // Act
        var result = await manager.GetAsync("Theme", "light", CancellationToken.None);

        // Assert: 命中 User 层
        Assert.Equal("dark", result);
    }

    [Fact]
    public async Task GetAsync_TenantLayer_FallbackFromUser()
    {
        // Arrange: 写入 Tenant 层
        var store = CreateCountingStore();
        var user = new StubDomainUser(userId: "user-1", tenantId: 100, isAuthenticated: true);
        var manager = CreateManager(store: store, user: user);

        // 直接写 Tenant 层（通过 Store，绕过 Manager 的 User 层写入）
        await store.SetAsync("Logo", "tenant-logo", "Tenant", "100", null, CancellationToken.None);

        // Act
        var result = await manager.GetAsync("Logo", "default", CancellationToken.None);

        // Assert: User 层未命中，回退到 Tenant 层
        Assert.Equal("tenant-logo", result);
    }

    [Fact]
    public async Task GetAsync_GlobalLayer_FallbackFromTenant()
    {
        // Arrange: 仅写入 Global 层
        var store = CreateCountingStore();
        var user = new StubDomainUser(userId: "user-1", tenantId: 100, isAuthenticated: true);
        var manager = CreateManager(store: store, user: user);

        await store.SetAsync("Slogan", "global-slogan", "Global", null, null, CancellationToken.None);

        // Act
        var result = await manager.GetAsync("Slogan", "default", CancellationToken.None);

        // Assert: User/Tenant 层未命中，回退到 Global 层
        Assert.Equal("global-slogan", result);
    }

    [Fact]
    public async Task GetAsync_AllLayers_ReturnsDefault()
    {
        // Arrange: 无任何设置
        var user = new StubDomainUser(userId: "user-1", tenantId: 100, isAuthenticated: true);
        var manager = CreateManager(user: user);

        // Act
        var result = await manager.GetAsync("Missing", "myDefault", CancellationToken.None);

        // Assert
        Assert.Equal("myDefault", result);
    }

    [Fact]
    public async Task GetAsync_UserOverridesTenant()
    {
        // Arrange: 同名设置在 User 和 Tenant 层都有值
        var store = CreateCountingStore();
        var user = new StubDomainUser(userId: "user-1", tenantId: 100, isAuthenticated: true);
        var manager = CreateManager(store: store, user: user);

        await store.SetAsync("FontSize", "14", "Tenant", "100", null, CancellationToken.None);
        await store.SetAsync("FontSize", "18", "User", "user-1", null, CancellationToken.None);

        // Act
        var result = await manager.GetAsync("FontSize", "12", CancellationToken.None);

        // Assert: User 层优先
        Assert.Equal("18", result);
    }

    [Fact]
    public async Task GetAsync_TenantOverridesGlobal()
    {
        // Arrange: 同名设置在 Tenant 和 Global 层都有值
        var store = CreateCountingStore();
        var user = new StubDomainUser(userId: "user-1", tenantId: 100, isAuthenticated: true);
        var manager = CreateManager(store: store, user: user);

        await store.SetAsync("Lang", "en", "Global", null, null, CancellationToken.None);
        await store.SetAsync("Lang", "zh-CN", "Tenant", "100", null, CancellationToken.None);

        // Act: User 层无设置，Tenant 层有
        var result = await manager.GetAsync("Lang", "en", CancellationToken.None);

        // Assert: 命中 Tenant 层
        Assert.Equal("zh-CN", result);
    }

    // ── V0.2.0 新增测试：匿名降级 ──

    [Fact]
    public async Task GetAsync_Anonymous_SkipsUserAndTenant()
    {
        // Arrange: 匿名用户，User 和 Tenant 层有值
        var store = CreateCountingStore();
        var user = new StubDomainUser(isAuthenticated: false);
        var manager = CreateManager(store: store, user: user);

        await store.SetAsync("Theme", "user-theme", "User", "user-1", null, CancellationToken.None);
        await store.SetAsync("Theme", "tenant-theme", "Tenant", "100", null, CancellationToken.None);
        await store.SetAsync("Theme", "global-theme", "Global", null, null, CancellationToken.None);

        // Act
        var result = await manager.GetAsync("Theme", "default", CancellationToken.None);

        // Assert: 匿名用户跳过 User/Tenant，命中 Global 层
        Assert.Equal("global-theme", result);
    }

    [Fact]
    public async Task GetAsync_Anonymous_NoGlobal_ReturnsDefault()
    {
        // Arrange: 匿名用户，Global 层也无设置
        var user = new StubDomainUser(isAuthenticated: false);
        var manager = CreateManager(user: user);

        // Act
        var result = await manager.GetAsync("Theme", "default", CancellationToken.None);

        // Assert
        Assert.Equal("default", result);
    }

    [Fact]
    public async Task SetAsync_Anonymous_WritesToGlobalLayer()
    {
        // Arrange: 匿名用户
        var store = CreateCountingStore();
        var user = new StubDomainUser(isAuthenticated: false);
        var manager = CreateManager(store: store, user: user);

        // Act
        await manager.SetAsync("Theme", "dark", CancellationToken.None);

        // Assert: 写入 Global 层
        var saved = await store.GetAsync("Theme", "Global", null, CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("dark", saved!.Value);
    }

    [Fact]
    public async Task SetAsync_Authenticated_WritesToUserLayer()
    {
        // Arrange: 已认证用户
        var store = CreateCountingStore();
        var user = new StubDomainUser(userId: "user-42", tenantId: 1, isAuthenticated: true);
        var manager = CreateManager(store: store, user: user);

        // Act
        await manager.SetAsync("Theme", "dark", CancellationToken.None);

        // Assert: 写入 User 层
        var saved = await store.GetAsync("Theme", "User", "user-42", CancellationToken.None);
        Assert.NotNull(saved);
        Assert.Equal("dark", saved!.Value);
    }

    // ── V0.2.0 新增测试：缓存命中/失效 ──

    [Fact]
    public async Task GetAsync_CacheHit_DoesNotCallStoreSecondTime()
    {
        // Arrange: 使用计数 Store
        var store = CreateCountingStore();
        var user = new StubDomainUser(isAuthenticated: false);
        var manager = CreateManager(store: store, user: user);

        await store.SetAsync("Theme", "dark", "Global", null, null, CancellationToken.None);
        store.ResetCounts();

        // Act: 第一次读取（缓存未命中，调用 Store）
        var r1 = await manager.GetAsync("Theme", "", CancellationToken.None);
        var callsAfterFirst = store.GetCalls;

        // 第二次读取（缓存命中，不调用 Store）
        var r2 = await manager.GetAsync("Theme", "", CancellationToken.None);
        var callsAfterSecond = store.GetCalls;

        // Assert
        Assert.Equal("dark", r1);
        Assert.Equal("dark", r2);
        Assert.Equal(1, callsAfterFirst);  // 第一次调用 Store
        Assert.Equal(1, callsAfterSecond); // 第二次不调用 Store
    }

    [Fact]
    public async Task SetAsync_InvalidateCache_NextGetCallsStore()
    {
        // Arrange: 使用计数 Store
        var store = CreateCountingStore();
        var user = new StubDomainUser(isAuthenticated: false);
        var manager = CreateManager(store: store, user: user);

        await store.SetAsync("Theme", "dark", "Global", null, null, CancellationToken.None);
        store.ResetCounts();

        // Act: 第一次读取（缓存命中）
        await manager.GetAsync("Theme", "", CancellationToken.None);
        var callsAfterRead = store.GetCalls;

        // SetAsync 后缓存失效
        await manager.SetAsync("Theme", "light", CancellationToken.None);
        store.ResetCounts();

        // 第二次读取（缓存失效，调用 Store）
        var result = await manager.GetAsync("Theme", "", CancellationToken.None);
        var callsAfterWrite = store.GetCalls;

        // Assert
        Assert.Equal("light", result);
        Assert.Equal(1, callsAfterWrite); // 缓存失效后重新调用 Store
    }

    [Fact]
    public async Task GetAsync_NullResult_CachedAsDefaultValue()
    {
        // Arrange: 不存在的设置（Store 返回 null）
        var store = CreateCountingStore();
        var user = new StubDomainUser(isAuthenticated: false);
        var manager = CreateManager(store: store, user: user);
        store.ResetCounts();

        // Act: 第一次读取
        var r1 = await manager.GetAsync("Missing", "fallback", CancellationToken.None);
        var callsAfterFirst = store.GetCalls;

        // 第二次读取（缓存命中 null 值）
        var r2 = await manager.GetAsync("Missing", "fallback", CancellationToken.None);
        var callsAfterSecond = store.GetCalls;

        // Assert
        Assert.Equal("fallback", r1);
        Assert.Equal("fallback", r2);
        Assert.Equal(1, callsAfterFirst);
        Assert.Equal(1, callsAfterSecond); // null 也缓存，不穿透
    }

    [Fact]
    public async Task GetAsync_CacheExpiration_CustomSeconds()
    {
        // Arrange: 设置很短的缓存过期时间
        var store = CreateCountingStore();
        var user = new StubDomainUser(isAuthenticated: false);
        var manager = CreateManager(store: store, user: user, cacheExpirationSeconds: 1);
        store.ResetCounts();

        // 写入设置
        await store.SetAsync("Key", "value", "Global", null, null, CancellationToken.None);

        // 第一次读取
        await manager.GetAsync("Key", "", CancellationToken.None);
        Assert.Equal(1, store.GetCalls);

        // 立即再次读取（缓存命中）
        await manager.GetAsync("Key", "", CancellationToken.None);
        Assert.Equal(1, store.GetCalls);

        // 等待缓存过期
        await Task.Delay(1100, TestContext.Current.CancellationToken);

        // 过期后再次读取（缓存失效，重新调用 Store）
        await manager.GetAsync("Key", "", CancellationToken.None);
        Assert.Equal(2, store.GetCalls);
    }

    // ── V0.2.0 新增测试：Options 绑定 ──

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

    private static CountingSettingStore CreateCountingStore()
    {
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        return new CountingSettingStore(fsql, new FakeLogger<FreeSqlSettingStore>());
    }

    private sealed class TestLayout
    {
        public int Columns { get; set; }
        public int Rows { get; set; }
    }

    private sealed class StubDomainUser : IDomainUser
    {
        private readonly string? _userId;
        private readonly long? _tenantId;
        private readonly bool _isAuthenticated;

        public StubDomainUser(
            string? userId = "test-user",
            long? tenantId = null,
            bool isAuthenticated = false)
        {
            _userId = userId;
            _tenantId = tenantId;
            _isAuthenticated = isAuthenticated;
        }

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
            => throw new NotSupportedException("Stub: Use<T> not supported in unit tests");
        public TService GetService<TService>() where TService : notnull
            => throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");
        public TService GetOptionalService<TService>() where TService : class => null!;
        public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
    }

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

    /// <summary>计数 ISettingStore：追踪 GetAsync 调用次数，用于验证缓存行为。</summary>
    private sealed class CountingSettingStore : ISettingStore
    {
        private readonly IFreeSql _freeSql;
        private readonly ILogger<FreeSqlSettingStore> _logger;

        public int GetCalls { get; private set; }

        public CountingSettingStore(IFreeSql freeSql, ILogger<FreeSqlSettingStore> logger)
        {
            _freeSql = freeSql;
            _logger = logger;
        }

        public void ResetCounts() => GetCalls = 0;

        public async Task<SettingEntity?> GetAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
        {
            GetCalls++;
            return await _freeSql.Select<SettingEntity>()
                .Where(s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey)
                .FirstAsync(ct);
        }

        public Task<IReadOnlyList<SettingEntity>> GetListAsync(string providerName, string? providerKey, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<SettingEntity>>(Array.Empty<SettingEntity>());

        public async Task SetAsync(string name, string? value, string providerName, string? providerKey, string? description, CancellationToken ct = default)
        {
            var now = DateTimeOffset.Now;
            await _freeSql.Delete<SettingEntity>()
                .Where(s => s.Name == name && s.ProviderName == providerName && s.ProviderKey == providerKey)
                .ExecuteAffrowsAsync(ct);

            var entity = new SettingEntity
            {
                Name = name,
                Value = value,
                ProviderName = providerName,
                ProviderKey = providerKey,
                Description = description,
                IsVisibleToClients = true,
                CreateTime = now,
                UpdateTime = now
            };
            await _freeSql.Insert(entity).ExecuteAffrowsAsync(ct);
        }

        public Task DeleteAsync(string name, string providerName, string? providerKey, CancellationToken ct = default)
            => Task.CompletedTask;
    }
}
