using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Settings.Tests;

/// <summary>
/// SettingManager 测试——覆盖分层优先级、默认值、JSON 序列化。
/// <para>当前 V0.1.0 仅 Global 层；测试验证分层逻辑基础 + 序列化鲁棒性。</para>
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

    private static SettingManager CreateManager(ISettingStore? store = null, IDomainUser? user = null, string provider = "Global")
    {
        var fsql = CreateInMemoryFreeSql();
        fsql.CodeFirst.SyncStructure<SettingEntity>();
        store ??= new FreeSqlSettingStore(fsql, new FakeLogger<FreeSqlSettingStore>());
        user ??= new StubDomainUser();
        var options = Options.Create(new SettingsOptions { DefaultSettingValueProvider = provider });
        return new SettingManager(store, user, options);
    }

    [Fact]
    public async Task GetAsync_ExistingSetting_ReturnsValue()
    {
        // Arrange
        var manager = CreateManager();
        await manager.SetAsync("Theme", "dark", CancellationToken.None);

        // Act
        var result = await manager.GetAsync("Theme", "", CancellationToken.None);

        // Assert
        Assert.Equal("dark", result);
    }

    [Fact]
    public async Task GetAsync_NonExistent_ReturnsDefault()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = await manager.GetAsync("NotExist", "fallback", CancellationToken.None);

        // Assert
        Assert.Equal("fallback", result);
    }

    [Fact]
    public async Task GetAsync_EmptyDefault_ReturnsEmptyString()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = await manager.GetAsync("NotExist", "", CancellationToken.None);

        // Assert
        Assert.Equal("", result);
    }

    [Fact]
    public async Task SetAsync_OverwritesPreviousValue()
    {
        // Arrange
        var manager = CreateManager();
        await manager.SetAsync("Color", "red", CancellationToken.None);

        // Act
        await manager.SetAsync("Color", "blue", CancellationToken.None);
        var result = await manager.GetAsync("Color", "", CancellationToken.None);

        // Assert
        Assert.Equal("blue", result);
    }

    [Fact]
    public async Task GetAsync_Typed_ReturnsDeserialized()
    {
        // Arrange
        var manager = CreateManager();
        await manager.SetAsync("MaxRetries", "3", CancellationToken.None);

        // Act
        var result = await manager.GetAsync("MaxRetries", 0, CancellationToken.None);

        // Assert
        Assert.Equal(3, result);
    }

    [Fact]
    public async Task GetAsync_Typed_InvalidJson_ReturnsDefault()
    {
        // Arrange
        var manager = CreateManager();
        await manager.SetAsync("Broken", "not-a-json", CancellationToken.None);

        // Act
        var result = await manager.GetAsync("Broken", 42, CancellationToken.None);

        // Assert
        Assert.Equal(42, result);
    }

    [Fact]
    public async Task SetAsync_Typed_SerializesToJSON()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        await manager.SetAsync("Config", new { Width = 1920, Height = 1080 }, CancellationToken.None);

        // Assert — value should be JSON string
        var raw = await manager.GetAsync("Config", "", CancellationToken.None);
        Assert.Contains("1920", raw);
        Assert.Contains("1080", raw);
    }

    [Fact]
    public async Task GetAsync_Typed_ComplexObject_Deserializes()
    {
        // Arrange
        var manager = CreateManager();
        await manager.SetAsync("Layout", new TestLayout { Columns = 3, Rows = 2 }, CancellationToken.None);

        // Act
        var result = await manager.GetAsync("Layout", new TestLayout(), CancellationToken.None);

        // Assert
        Assert.Equal(3, result.Columns);
        Assert.Equal(2, result.Rows);
    }

    [Fact]
    public async Task GetAsync_Typed_NullableInt_ReturnsDefault()
    {
        // Arrange
        var manager = CreateManager();

        // Act
        var result = await manager.GetAsync<int?>("NonExistent", null, CancellationToken.None);

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public async Task GetAsync_MultipleSettings_Independent()
    {
        // Arrange
        var manager = CreateManager();
        await manager.SetAsync("A", "1", CancellationToken.None);
        await manager.SetAsync("B", "2", CancellationToken.None);

        // Act
        var a = await manager.GetAsync("A", "", CancellationToken.None);
        var b = await manager.GetAsync("B", "", CancellationToken.None);

        // Assert
        Assert.Equal("1", a);
        Assert.Equal("2", b);
    }

    // ── Test helpers ──

    private sealed class TestLayout
    {
        public int Columns { get; set; }
        public int Rows { get; set; }
    }

    private sealed class StubDomainUser : IDomainUser
    {
        public string SessionKey => "test-session";
        public bool IsAuthenticated => false;
        public bool IsSystemActor => false;
        public IUserInfo? UserInfo => null;
        public long? TenantId => null;
        public bool IsNoAuditActive => false;
        public string? UserId => "test-user";
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
}
