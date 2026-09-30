using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;

namespace TKWF.Ext.MFA.Tests;

/// <summary>
/// 测试公共设施——SQLite 内存库创建 + 三 MFA 实体表结构同步。
/// <para>数据访问红线合规：MfaService 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;(new UnitOfWorkManager(fsql))</c> 驱动（与 FileManagement/Calendar 测试宿主同模式）。</para>
/// </summary>
internal static class MfaTestSupport
{
    /// <summary>创建使用 SQLite 内存库的 IFreeSql 实例（自动同步表结构）。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
        => new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();

    /// <summary>
    /// 创建使用 SQLite <b>文件模式</b>的 IFreeSql 实例（自动同步表结构）。
    /// <para>与 <see cref="CreateInMemoryFreeSql"/>（:memory: 单连接独占）不同——文件模式允许多连接同时访问同一库文件，
    /// FreeSql 连接池（ObjectPool）可正常出借/归还连接，多线程并发写由 SQLite 文件锁协调。
    /// 用于并发测试（并发挑战单次消费——:memory: 单连接池在 CI 高负载下多线程争用 ObjectPool.Get()
    /// 可能超时 10s，见 FreeSql discussions/1081）。</para>
    /// <param name="dbPath">输出：库文件路径（调用方负责删除清理）。</param>
    /// </summary>
    public static IFreeSql CreateFileFreeSql(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), "tkfw-mfa-db-" + Guid.NewGuid().ToString("N") + ".db");
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>同步三张表结构（MfaSecret + MfaChallenge + MfaRecoveryCode）。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<MfaSecretEntity>();
        fsql.CodeFirst.SyncStructure<MfaChallengeEntity>();
        fsql.CodeFirst.SyncStructure<MfaRecoveryCodeEntity>();
    }
}

/// <summary>
/// 完整测试宿主——构建 DI 容器：真实 DataService 链（FreeSqlEntityDAC + UnitOfWorkManager 驱动）
/// + StubDomainUser + Noop 事务管理 + MFA Options + <c>MFAExtensionInitializer&lt;MfaUserInfo&gt;</c> 接线
/// + 记录型 <see cref="RecordingMfaSmsSender"/>（SMS 方法经 <see cref="IServiceProvider"/> 惰性解析，必须真实注册进容器）。
/// <para>每用例独立 <see cref="Create"/> 得到全新 SQLite 内存库（用例隔离）。
/// <paramref name="configure"/> 回调在初始化器之后执行。</para>
/// </summary>
internal sealed class MfaTestHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string? _dbPath;   // 文件模式 SQLite 库路径（若有，Dispose 清理）

    public IFreeSql Fsql { get; }

    /// <summary>根 ServiceProvider（公开——并发用例经 CreateScope 解析独立 Scoped 服务实例，消除根容器单例巧合）。</summary>
    public ServiceProvider ServiceProvider => _serviceProvider;

    /// <summary>MFA 门面（public 契约——所有业务断言经此驱动）。</summary>
    public IMfaService Mfa => _serviceProvider.GetRequiredService<IMfaService>();

    /// <summary>记录型 SMS 发送 Fake（捕获 <see cref="MfaSmsMessage"/>——测试提取验证码）。</summary>
    public RecordingMfaSmsSender SmsSender => GetRequiredService<IMfaSmsSender>() as RecordingMfaSmsSender
        ?? throw new InvalidOperationException("IMfaSmsSender 未注册为 RecordingMfaSmsSender——测试宿主必须注册记录型 Fake");

    public MfaSecretEntityDataService Secrets => GetRequiredService<MfaSecretEntityDataService>();

    public MfaChallengeEntityDataService Challenges => GetRequiredService<MfaChallengeEntityDataService>();

    public MfaRecoveryCodeEntityDataService RecoveryCodes => GetRequiredService<MfaRecoveryCodeEntityDataService>();

    private MfaTestHost(ServiceProvider serviceProvider, IFreeSql fsql, string? dbPath = null)
    {
        _serviceProvider = serviceProvider;
        Fsql = fsql;
        _dbPath = dbPath;
    }

    /// <summary>全新宿主（每次调用独立 SQLite 内存库）。</summary>
    public static MfaTestHost Create(
        MfaOptions? options = null,
        Action<IServiceCollection>? configure = null)
    {
        var fsql = MfaTestSupport.CreateInMemoryFreeSql();
        return BuildHost(fsql, dbPath: null, options, configure);
    }

    /// <summary>
    /// 文件模式 SQLite 宿主场（供并发测试用）——与 <see cref="Create"/> 相同注册，
    /// 但用文件模式库（多连接共享，FreeSql ObjectPool 正常出借，避免 :memory: 单连接池
    /// 在 CI 高负载并发下 ObjectPool.Get() 超时 10s——FreeSql discussions/1081）。
    /// <paramref name="dbPath"/> 输出库文件路径，由宿主 Dispose 清理。
    /// </summary>
    public static MfaTestHost CreateFile(
        out string dbPath,
        MfaOptions? options = null,
        Action<IServiceCollection>? configure = null)
    {
        var fsql = MfaTestSupport.CreateFileFreeSql(out dbPath);
        return BuildHost(fsql, dbPath, options, configure);
    }

    /// <summary>宿主构建公共路径（Create 内存模式与 CreateFile 文件模式共用；dbPath 非空=文件模式）。</summary>
    private static MfaTestHost BuildHost(
        IFreeSql fsql,
        string? dbPath,
        MfaOptions? options,
        Action<IServiceCollection>? configure)
    {
        MfaTestSupport.SyncStructure(fsql);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fsql);

        var stubUser = new StubDomainUser();
        services.AddSingleton<IDomainUser>(stubUser);

        // v4.10.8 (ADR61)：DataService 不再手动 new/注册——DI 兜底工厂（镜像生产 AddConstructibleDataService，
        // 用户源 = DI IDomainUser，免域作用域）；IEntityDAC<T> 基础设施注册同生产 Host。
        // 注：每 DataService 独立 UnitOfWorkManager（对齐 FileManagement 旧 Host 语义——并发用例依赖
        // 各 DataService 事务隔离，共享 UoW 会改变竞态时序）。
        services.AddScoped<IEntityDAC<MfaSecretEntity>>(sp => new FreeSqlEntityDAC<MfaSecretEntity>(new UnitOfWorkManager(sp.GetRequiredService<IFreeSql>())));
        services.AddScoped<IEntityDAC<MfaChallengeEntity>>(sp => new FreeSqlEntityDAC<MfaChallengeEntity>(new UnitOfWorkManager(sp.GetRequiredService<IFreeSql>())));
        services.AddScoped<IEntityDAC<MfaRecoveryCodeEntity>>(sp => new FreeSqlEntityDAC<MfaRecoveryCodeEntity>(new UnitOfWorkManager(sp.GetRequiredService<IFreeSql>())));
        AddTestConstructibleDataService<MfaSecretEntityDataService>(services);
        AddTestConstructibleDataService<MfaChallengeEntityDataService>(services);
        AddTestConstructibleDataService<MfaRecoveryCodeEntityDataService>(services);

        // ITransactionManager（默认 Noop——Create/Update/Delete 写路径事务包裹依赖空操作，
        // DataService 逐操作经 UnitOfWorkManager 持久化；Recording 由 configure 覆盖）
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();

        // MFA Options——先于初始化器注册（AddOptions 为 TryAdd 语义，不会覆盖测试实例；
        // 初始化器经 AddOptions/Option 绑定注册的默认配置对已存在 IOptions 实例不生效，测试以显式 options 为准）
        services.AddSingleton<IOptions<MfaOptions>>(Options.Create(options ?? new MfaOptions()));

        // 扩展初始化器注册 IMfaService + 双 IMfaMethod（TryAddEnumerable——Oracle C5）
        new MFAExtensionInitializer<MfaUserInfo>().ConfigureServices(services);

        // 记录型 SMS 发送 Fake（TryAddScoped——SMS 方法经 IServiceProvider 惰性解析 IMfaSmsSender，
        // 未装配会抛 MfaMockForbiddenException 503；必须真实注册进容器）
        services.TryAddScoped<IMfaSmsSender, RecordingMfaSmsSender>();

        configure?.Invoke(services);

        return new MfaTestHost(services.BuildServiceProvider(), fsql, dbPath);
    }

    /// <summary>解析服务（Scoped 服务经根容器解析，生命周期与宿主一致）。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

    /// <summary>v4.10.8 (ADR61) 迁移：测试版可构造 DataService 工厂——镜像生产 AddConstructibleDataService
    ///（ActivatorUtilities.CreateInstance + 域用户），用户源 = DI IDomainUser（免域作用域、xUnit 并行安全）。</summary>
    private static void AddTestConstructibleDataService<T>(IServiceCollection services)
        where T : class
    {
        services.AddScoped<T>(sp =>
        {
            var user = sp.GetRequiredService<IDomainUser>();
            return (T)ActivatorUtilities.CreateInstance(sp, typeof(T), user);
        });
    }

    /// <summary>释放宿主 + 清理文件模式 SQLite 库（宿主释放后连接池已归还，可安全删除）。</summary>
    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (_dbPath is not null && File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); }
            catch (IOException) { /* 连接未完全释放时忽略，由系统临时目录回收 */ }
        }
    }
}

/// <summary>
/// 记录型 SMS 发送 Fake——捕获 <see cref="MfaSmsMessage"/>（测试提取 6 位码断言）。
/// <para>对齐方案 §五「IMfaSmsSender 用记录型 Fake」；SendAsync 捕获消息 + 计数（发送成功才计数）。</para>
/// </summary>
internal sealed class RecordingMfaSmsSender : IMfaSmsSender
{
    private readonly object _gate = new();
    private readonly List<MfaSmsMessage> _messages = new();

    /// <summary>已发送消息（按发送顺序）。</summary>
    public IReadOnlyList<MfaSmsMessage> Messages
    {
        get { lock (_gate) return _messages.ToArray(); }
    }

    /// <summary>发送总次数。</summary>
    public int SendCount
    {
        get { lock (_gate) return _messages.Count; }
    }

    public Task SendAsync(MfaSmsMessage message, CancellationToken ct = default)
    {
        lock (_gate) _messages.Add(message);
        return Task.CompletedTask;
    }
}

/// <summary>MFA 扩展专用用户类型——继承 <see cref="SimpleUserInfo"/>（扩展不知道消费方 UserInfo 类型）。</summary>
public class MfaUserInfo : SimpleUserInfo
{
    public MfaUserInfo() : base() { }
}

/// <summary>Noop 事务管理器——BeginAsync/CommitAsync 空操作（对齐 Calendar/OrganizationUnit 测试宿主）。</summary>
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

/// <summary>Noop 事务作用域——CommitAsync/RollbackAsync 空操作。</summary>
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

/// <summary>测试用户桩——实现 IDomainUser 最小契约（默认匿名——MFA 按传入 userId 操作，不依赖当前用户）。</summary>
internal sealed class StubDomainUser : IDomainUser
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

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
        => throw new NotSupportedException("Stub: Use<T> not supported in unit tests");

    public TService GetService<TService>() where TService : notnull
        => throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
