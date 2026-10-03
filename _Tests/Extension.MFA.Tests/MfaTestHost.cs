using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
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
/// 完整测试宿主（V4.10.53 领域自治根治后重写——走生产路径）。
/// <para><b>门面（MfaService）</b>经真实 AOP 路径解析：真实 DI（扩展 ConfigureServices + FreeSql 基础设施 + AddLogging）
/// → <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> → <c>host.Mfa = user.Use&lt;IMfaService&gt;()</c>
/// （设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂 → ActivatorUtilities 直建，
/// CurrentAopUser = 真实 DomainUser）；DataService 经基类 <c>User.Use&lt;XxxDataService&gt;()</c> NoAop 直建
/// （IEntityDAC 从 DI 解析——与 Settings/Account 生产路径宿主同模式）。</para>
/// <para><b>IMfaMethod 实现（Totp/Sms）</b>本批整改范围外（已裁定方案仅列 MfaService）——仍构造注入 IDomainUser；
/// 测试宿主为其注册可配置 <see cref="StubDomainUser"/>（IDomainUser 单例——门面本身经守卫工厂走真实 DomainUser，
/// 桩仅供未整改方法经 DI 解析）；其 <c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价
/// （<c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c>，IEntityDAC 从 DI 解析）。</para>
/// <para>每用例独立 <see cref="Create"/> 得到全新 SQLite 内存库（用例隔离）。
/// <paramref name="configure"/> 回调在初始化器之后执行。</para>
/// </summary>
internal sealed class MfaTestHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly DomainUser<TestUserInfo> _user;
    private readonly string? _dbPath;   // 文件模式 SQLite 库路径（若有，Dispose 清理）

    public IFreeSql Fsql { get; }

    /// <summary>根 ServiceProvider（公开——并发用例经 <see cref="BuildIsolatedMfaService"/> 构建独立实例）。</summary>
    public ServiceProvider ServiceProvider => _serviceProvider;

    /// <summary>MFA 门面（public 契约——生产 AOP 路径：User.Use&lt;IMfaService&gt;() 经守卫工厂创建实例）。</summary>
    public IMfaService Mfa => _user.Use<IMfaService>();

    /// <summary>记录型 SMS 发送 Fake（捕获 <see cref="MfaSmsMessage"/>——测试提取验证码）。</summary>
    public RecordingMfaSmsSender SmsSender => GetRequiredService<IMfaSmsSender>() as RecordingMfaSmsSender
        ?? throw new InvalidOperationException("IMfaSmsSender 未注册为 RecordingMfaSmsSender——测试宿主必须注册记录型 Fake");

    public MfaSecretEntityDataService Secrets => _user.Use<MfaSecretEntityDataService>();

    public MfaChallengeEntityDataService Challenges => _user.Use<MfaChallengeEntityDataService>();

    public MfaRecoveryCodeEntityDataService RecoveryCodes => _user.Use<MfaRecoveryCodeEntityDataService>();

    private MfaTestHost(ServiceProvider serviceProvider, DomainUser<TestUserInfo> user, IFreeSql fsql, string? dbPath = null)
    {
        _serviceProvider = serviceProvider;
        _user = user;
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
    /// 自定义 SMS 渠道宿主——以指定 <see cref="IMfaSmsSender"/> 替换记录型 Recording Fake
    /// （发送失败清理孤儿挑战行用例注入抛异常/可切换 Fake——Oracle7 C1 缺陷复现与验收）。
    /// <paramref name="sender"/> 经 Scoped 工厂注册，每次解析返回同一实例（测试持有引用切换行为）。
    /// </summary>
    public static MfaTestHost CreateWithSmsSender(
        IMfaSmsSender sender,
        MfaOptions? options = null,
        Action<IServiceCollection>? configure = null)
    {
        if (sender is null) throw new ArgumentNullException(nameof(sender));
        var fsql = MfaTestSupport.CreateInMemoryFreeSql();
        return BuildHost(fsql, dbPath: null, options, configure, smsSender: sender);
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
        Action<IServiceCollection>? configure,
        IMfaSmsSender? smsSender = null)
    {
        MfaTestSupport.SyncStructure(fsql);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fsql);

        // IEntityDAC<T> 基础设施注册（消费方 DomainHost 等价注册——Singleton + 共享 UnitOfWorkManager，
        // 对齐 Settings/Account 生产路径宿主）
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<MfaSecretEntity>, FreeSqlEntityDAC<MfaSecretEntity>>();
        services.AddSingleton<IEntityDAC<MfaChallengeEntity>, FreeSqlEntityDAC<MfaChallengeEntity>>();
        services.AddSingleton<IEntityDAC<MfaRecoveryCodeEntity>, FreeSqlEntityDAC<MfaRecoveryCodeEntity>>();

        // ITransactionManager（默认 Noop——Create/Update/Delete 写路径事务包裹依赖空操作，
        // DataService 逐操作经 UnitOfWorkManager 持久化；Recording 由 configure 覆盖）
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();

        // MFA Options——先于初始化器注册（AddOptions 为 TryAdd 语义，不会覆盖测试实例；
        // 初始化器经 AddOptions/Option 绑定注册的默认配置对已存在 IOptions 实例不生效，测试以显式 options 为准）
        services.AddSingleton<IOptions<MfaOptions>>(Options.Create(options ?? new MfaOptions()));

        // ⚠️ IMfaMethod 实现（Totp/Sms）本批整改范围外——仍 ctor 注入 IDomainUser；宿主为其注册可配置 stub
        //（门面 MfaService 经守卫工厂走真实 DomainUser，此桩仅供未整改方法经 DI 解析——过渡期记录在整改日志）
        var stubUser = new StubDomainUser();
        services.AddSingleton<IDomainUser>(sp => { stubUser.ServiceProvider = sp; return stubUser; });

        // 扩展初始化器注册 IMfaService（AddConstructibleService——V4.10.53 领域自治）+ 双 IMfaMethod（TryAddEnumerable——Oracle C5）
        new MFAExtensionInitializer<MfaUserInfo>().ConfigureServices(services);

        // 记录型 SMS 发送 Fake（TryAddScoped——SMS 方法经 IServiceProvider 惰性解析 IMfaSmsSender，
        // 未装配会抛 MfaMockForbiddenException 503；必须真实注册进容器）
        // 自定义渠道（CreateWithSmsSender）显式注册注入 sender——发送失败清理孤儿挑战行用例（Oracle7 C1）
        if (smsSender is not null)
            services.AddScoped(_ => smsSender);
        else
            services.TryAddScoped<IMfaSmsSender, RecordingMfaSmsSender>();

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();

        // 生产路径：绑定解析作用域 + 真实 DomainUser（AOP 路径 CurrentAopUser 来源）
        DomainUser<TestUserInfo>.BindScope(provider);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("mfa-test-user", "MFA 测试用户") };

        return new MfaTestHost(provider, user, fsql, dbPath);
    }

    /// <summary>
    /// 构建独立 MfaService 实例（并发测试用——消除根容器单一实例→串行化→假绿）。
    /// <para>生产 AOP 路径解析的并发等价：两实例各自独立 stub 用户 + 独立 IMfaMethod 实例 + 独立
    /// DataService/UoW 链（每实例独立 UnitOfWorkManager——共享 UoW 会改变竞态时序；
    /// 文件模式 SQLite 多连接共享库文件）。</para>
    /// </summary>
    public static MfaService BuildIsolatedMfaService(IFreeSql fsql, MfaOptions? options = null)
    {
        // 独立 stub 容器（每实例独立 IEntityDAC + UnitOfWorkManager——镜像旧宿主 per-scope 隔离语义）
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddScoped<IEntityDAC<MfaSecretEntity>>(_ => new FreeSqlEntityDAC<MfaSecretEntity>(new UnitOfWorkManager(fsql)));
        services.AddScoped<IEntityDAC<MfaChallengeEntity>>(_ => new FreeSqlEntityDAC<MfaChallengeEntity>(new UnitOfWorkManager(fsql)));
        services.AddScoped<IEntityDAC<MfaRecoveryCodeEntity>>(_ => new FreeSqlEntityDAC<MfaRecoveryCodeEntity>(new UnitOfWorkManager(fsql)));
        var stub = new StubDomainUser { ServiceProvider = services.BuildServiceProvider() };
        var opt = Options.Create(options ?? new MfaOptions());

        // 独立方法实例（stub 用户直构——方法与 DataService 同链隔离）
        IEnumerable<IMfaMethod> methods =
        [
            new TotpMfaMethod(stub, opt),
            new SmsMfaMethod(stub, opt, stub.ServiceProvider!),
        ];
        return new MfaService(stub, methods, opt, NullLogger<MfaService>.Instance);
    }

    /// <summary>解析服务（Scoped 服务经根容器解析，生命周期与宿主一致）。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

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

/// <summary>
/// 测试用户桩——实现 <see cref="IDomainUser"/> 最小契约（默认匿名——MFA 按传入 userId 操作，不依赖当前用户）。
/// <para>V4.10.53（领域自治根治）：<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价实现——具体类经
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c> 直建（IDomainUser 参数显式传 this，
/// IEntityDAC 等其余从 DI 解析——对齐 Settings/Tagging 测试桩）。</para>
/// <para>⚠️ 本桩供未整改的 <see cref="IMfaMethod"/> 实现（Totp/Sms 仍 ctor 注入 IDomainUser）经 DI 解析；
/// 门面 <see cref="IMfaService"/> 经真实 <see cref="DomainUser{TUserInfo}"/> + 守卫工厂 AOP 路径解析（不触此桩）。</para>
/// </summary>
internal sealed class StubDomainUser : IDomainUser
{
    private IServiceProvider? _provider;

    /// <summary>ServiceProvider（宿主注册时注入——懒加载 Use&lt;T&gt; 解析源）。</summary>
    public IServiceProvider? ServiceProvider
    {
        set { _provider = value; }
        get => _provider;
    }

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
    {
        if (_provider is null)
            throw new InvalidOperationException("Stub: ServiceProvider 未注入——Use<T> 解析不可用");
        // 生产 NoAop 路径等价：ActivatorUtilities 直建，IDomainUser 参数显式传 this，其余从 DI 解析
        return (TDomainService)ActivatorUtilities.CreateInstance(_provider, typeof(TDomainService), this);
    }

    public TService GetService<TService>() where TService : notnull
    {
        if (_provider is null)
            throw new InvalidOperationException("Stub: GetService<T> 未注入 ServiceProvider");
        return _provider.GetRequiredService<TService>();
    }

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
