using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKW.Framework.Utility.RateLimitChecks;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.RateLimiting.Tests;

/// <summary>
/// SqlCountRateLimitCheck 测试宿主——真实生产路径（RateLimiting Initializer + FreeSql SQLite + 全实体 DAC）。
/// <para>帧载体：<see cref="ITestRateLimitProbe"/>（AddConstructibleService 守卫工厂——测试经
/// <c>user.Use&lt;ITestRateLimitProbe&gt;()</c> 帧内调用，CurrentAopUser 非空）→ 门面内调
/// <see cref="SqlCountRateLimitCheck"/>（接线型——帧内 GetRequiredService&lt;IRateLimitCounterDataService&gt;()
/// 守卫工厂解析）。消费方形态：<c>AddSingleton&lt;IRateLimitCheck, SqlCountRateLimitCheck&gt;()</c> 显式注册
/// （RC1 确定性路径）。</para>
/// <para>并发用例用文件模式 SQLite（多连接共享库文件，FreeSql ObjectPool 正常出借——:memory: 单连接
/// 在 CI 高负载并发下 ObjectPool.Get() 可能超时，对齐 MfaTestHost 先例）。</para>
/// </summary>
internal sealed class RateLimitTestHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly DomainUser<TestUserInfo> _user;
    private readonly string? _dbPath;

    public RateLimitTestHost(ServiceProvider serviceProvider, DomainUser<TestUserInfo> user, string? dbPath = null)
    {
        _serviceProvider = serviceProvider;
        _user = user;
        _dbPath = dbPath;
    }

    /// <summary>帧载体门面（测试经此在域作用域帧内调用 SqlCount——CurrentAopUser 由 Use&lt;T&gt;() 供给）。</summary>
    public ITestRateLimitProbe Probe => _user.Use<ITestRateLimitProbe>();

    /// <summary>底层 FreeSql（测试操纵计数行——过期重置用例模拟时间流逝；SQLite UTC 读回偏移已知限制见 AuthSurface README）。</summary>
    public IFreeSql Fsql => GetRequiredService<IFreeSql>();

    /// <summary>已注册 IRateLimitCheck 实现（消费方显式注册的 SqlCountRateLimitCheck——确定性路径断言）。</summary>
    public IRateLimitCheck RateLimitCheck => _serviceProvider.GetRequiredService<IRateLimitCheck>();

    public T GetRequiredService<T>() where T : notnull => _serviceProvider.GetRequiredService<T>();

    /// <summary>创建内存模式宿主（用例隔离）。</summary>
    public static RateLimitTestHost Create()
        => BuildHost(new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build(), dbPath: null);

    /// <summary>创建文件模式宿主（并发用例——多连接共享库文件）。</summary>
    public static RateLimitTestHost CreateFile(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), "tkfw-rl-db-" + Guid.NewGuid().ToString("N") + ".db");
        return BuildHost(new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={dbPath}")
            .UseAutoSyncStructure(true)
            .Build(), dbPath);
    }

    private static RateLimitTestHost BuildHost(IFreeSql fsql, string? dbPath)
    {
        fsql.CodeFirst.SyncStructure<RateLimitCounterEntity>();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<RateLimitCounterEntity>, FreeSqlEntityDAC<RateLimitCounterEntity>>();
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();

        // 扩展 Initializer（Options + IRateLimitCounterDataService 门面 AddConstructibleService）
        new RateLimitingExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 消费方确定性路径（RC1）：显式注册 SqlCountRateLimitCheck（接线型——ctor IServiceProvider 可 DI 构造）
        services.AddSingleton<IRateLimitCheck, SqlCountRateLimitCheck>();

        // 帧载体门面（测试专用——AddConstructibleService 守卫工厂，经 User.Use 帧内解析）
        services.AddConstructibleService<ITestRateLimitProbe, TestRateLimitProbe>();

        var provider = services.BuildServiceProvider();
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("rate-limit-test", "RateLimit 测试用户") };
        return new RateLimitTestHost(provider, user, dbPath);
    }

    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (_dbPath is not null && File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); }
            catch (IOException) { /* 连接未完全释放时忽略 */ }
        }
    }
}

/// <summary>
/// 帧载体门面——测试在 <c>user.Use&lt;ITestRateLimitProbe&gt;()</c> 域作用域帧内调用 SqlCountRateLimitCheck
/// （接线型 Provider 需帧内 CurrentAopUser 非空才能解析 IRateLimitCounterDataService 守卫工厂）。
/// </summary>
public interface ITestRateLimitProbe : IDomainService
{
    bool TryAcquire(string key, int maxAttempts, TimeSpan window, out int remaining);
    TimeSpan GetRetryAfter(string key, TimeSpan window);
    int GetRemaining(string key, int maxAttempts, TimeSpan window);

    /// <summary>已注入 IRateLimitCheck 实现类型（帧内解析链路断言——ctor 注入的 DI 实现）。</summary>
    Type CheckType { get; }
}

/// <summary>帧载体实现——ctor 注入 IRateLimitCheck（普通 DI 解析），方法直调（帧内执行）。</summary>
[DiContractIgnore]
internal sealed class TestRateLimitProbe : DomainServiceBase, ITestRateLimitProbe
{
    private readonly IRateLimitCheck _check;

    public TestRateLimitProbe(IDomainUser user, IRateLimitCheck check) : base(user)
        => _check = check ?? throw new ArgumentNullException(nameof(check));

    public bool TryAcquire(string key, int maxAttempts, TimeSpan window, out int remaining)
        => _check.TryAcquire(key, maxAttempts, window, out remaining);

    public TimeSpan GetRetryAfter(string key, TimeSpan window) => _check.GetRetryAfter(key, window);

    public int GetRemaining(string key, int maxAttempts, TimeSpan window) => _check.GetRemaining(key, maxAttempts, window);

    /// <summary>已注入 IRateLimitCheck 实现类型（帧内解析链路——ctor 注入的 DI 实现）。</summary>
    public Type CheckType => _check.GetType();
}

/// <summary>Noop 事务管理器（对齐 Calendar/OrganizationUnit 测试宿主）。</summary>
internal sealed class NoopTransactionManager : ITransactionManager
{
    public bool IsActive => false;
    public ITransactionScope Begin(System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.Serializable)
        => new NoopTransactionScope();
    public Task<ITransactionScope> BeginAsync(
        System.Data.IsolationLevel isolationLevel = System.Data.IsolationLevel.Serializable,
        CancellationToken ct = default)
        => Task.FromResult<ITransactionScope>(new NoopTransactionScope());
}

/// <summary>Noop 事务作用域。</summary>
internal sealed class NoopTransactionScope : ITransactionScope
{
    public bool IsActive => true;
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    public void Dispose() { }
    public Task CommitAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task RollbackAsync(CancellationToken ct = default) => Task.CompletedTask;
    public void Commit() { }
    public void Rollback() { }
}
