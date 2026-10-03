using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.BlobStoring;
using TKWF.Ext.FileManagement;

namespace TKWF.Ext.FileManagement.Tests;

/// <summary>
/// 测试公共设施——SQLite 内存库创建 + 三实体表结构同步。
/// <para>数据访问红线合规：Store/Manager 委托 SG1 DataService——测试用真实
/// <c>FreeSqlEntityDAC&lt;T&gt;(new UnitOfWorkManager(fsql))</c> 驱动（与 Calendar/OrganizationUnit 测试宿主同模式）。</para>
/// </summary>
internal static class FileManagementTestSupport
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
    /// 用于并发测试（如 FileVersionManager 并发版本上传——:memory: 单连接池在 CI 高负载下
    /// 多线程争用 ObjectPool.Get() 可能超时 10s，见 FreeSql discussions/1081）。</para>
    /// <param name="dbPath">输出：库文件路径（调用方负责删除清理）。</param>
    /// </summary>
    public static IFreeSql CreateFileFreeSql(out string dbPath)
    {
        dbPath = Path.Combine(Path.GetTempPath(), "tkfw-fm-db-" + Guid.NewGuid().ToString("N") + ".db");
        return new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
    }

    /// <summary>同步三张表结构（FileFolder + ManagedFile + ManagedFileVersion——V0.2.0）。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<FileFolderEntity>();
        fsql.CodeFirst.SyncStructure<ManagedFileEntity>();
        fsql.CodeFirst.SyncStructure<ManagedFileVersionEntity>();
    }
}

/// <summary>
/// 完整测试宿主——V4.10.53（领域自治根治，ADR90，正确路线）重写走<b>生产路径</b>：
/// <list type="bullet">
/// <item><b>真实 DI</b>（Initializer <c>ConfigureServices</c> + FreeSql 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c>；</item>
/// <item><b>门面经真实 <c>DomainUser&lt;TestUserInfo&gt;.Use&lt;IFileManager&gt;()</c> AOP 路径解析</b>（框架 Use 内部设 CurrentAopUser →
///     GetRequiredService → AddConstructibleService 守卫工厂 → ActivatorUtilities 直建实现）——<see cref="Manager"/>
///     （匿名——IDomainUser 永不注册 DI 语义保持，FileManager 经守卫工厂由 CurrentAopUser 传入，非 DI 解析）；</item>
/// <item><b>内部接线型 Store 普通 DI 解析</b>（TryAddScoped ImplementationType——FileManager 守卫工厂 ActivatorUtilities
///     ctor 注入解析；实现 ctor(<see cref="IServiceProvider"/>) + C1 延迟解析 DataService）；</item>
/// <item><b>DataService 可构造注册</b>（接线型 Store 的 C1 延迟解析源——wiring 边界测试镜像生产 ADR61 可构造工厂，
///     非 throw-factory；ctor 需 IDomainUser + IEntityDAC 均从 DI 解析）；</item>
/// <item><b>IBlobStorageService 真实链</b>（临时目录根 → <c>BlobStoringOptions</c> → <c>LocalStorageService</c>——
///     FileManagement 依赖 BlobStoring.Abstractions 契约，消费方启用 BlobStoring 扩展或自定义实现提供；测试直引实现项目注册）；</item>
/// <item><b>IDomainUser 注册（T3 桥接）</b>：DataService ctor 需 IDomainUser（测试注册 <see cref="TestDomainUser"/> 桩——
///     生产 DataService 经 Use&lt;具体类&gt;() NoAop 直建不经 DI；接线型 Store 的 GetRequiredService 需要）；</item>
/// <item><b>分层逻辑单测（V0.3.0 用户级配额/所有权）</b>——<see cref="ManagerFor(TestDomainUser)"/> 直构 FileManager
///     （internal 类 + IVT；可配置桩经基类 <c>User</c> 精确控制 IsAuthenticated/UserId/IsSystemActor——skill §4.5）。
///     注：测试项目无主框架 DomainUserContext IVT（框架仅授 FeatureManagement/Permissions 测试），可配置桩无法
///     经守卫工厂 AOP 注入——匿名 AOP 路径（<see cref="Manager"/>）覆盖生产解析语义，认证路径经直构等价验证。</item>
/// </list>
/// <para>每用例独立 <see cref="Create"/> 得到全新 SQLite 内存库 + 全新临时目录（用例隔离）。
/// <c>configure</c> 回调在扩展初始化器之后执行——可覆盖 ITransactionManager/FileManagementOptions
/// （对齐 Calendar/OrganizationUnit/FeatureManagement 宿主模式）。</para>
/// </summary>
internal sealed class FileManagementTestHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _blobRoot;
    private readonly string? _dbPath;   // 文件模式 SQLite 库路径（若有，Dispose 清理）

    /// <summary>生产 AOP 路径用户（真实 DomainUser&lt;TestUserInfo&gt;，匿名——经框架 Use 设 CurrentAopUser）。</summary>
    private readonly DomainUser<TestUserInfo> _domainUser = new();

    public IFreeSql Fsql { get; }

    /// <summary>可配置用户桩（默认匿名；AsAuthenticated 切换认证/UserId/SystemActor）——作 DI DataService 构造用户
    /// + <see cref="ManagerFor"/> 直构 FileManager 的用户上下文。</summary>
    public TestDomainUser User { get; }

    /// <summary>门面——生产 AOP 路径：真实 DomainUser&lt;TestUserInfo&gt;.Use&lt;IFileManager&gt;()（匿名——守卫工厂直建实现）。</summary>
    public IFileManager Manager => _domainUser.Use<IFileManager>();

    public IBlobStorageService BlobStorage => _serviceProvider.GetRequiredService<IBlobStorageService>();

    /// <summary>SG1 DataService（NoAop 路径直建——DataService 禁构造注入，DI004 零豁免；IEntityDAC 从 DI 解析）。</summary>
    public FileFolderEntityDataService FolderDataService => _domainUser.Use<FileFolderEntityDataService>();

    /// <summary>SG1 DataService（NoAop 路径直建——DataService 禁构造注入，DI004 零豁免；IEntityDAC 从 DI 解析）。</summary>
    public ManagedFileEntityDataService FileDataService => _domainUser.Use<ManagedFileEntityDataService>();

    private FileManagementTestHost(ServiceProvider serviceProvider, IFreeSql fsql, string blobRoot, TestDomainUser user, string? dbPath = null)
    {
        _serviceProvider = serviceProvider;
        Fsql = fsql;
        _blobRoot = blobRoot;
        User = user;
        _dbPath = dbPath;
    }

    /// <summary>全新宿主（每次调用独立 SQLite 内存库 + 独立 Blob 临时目录根）。</summary>
    public static FileManagementTestHost Create(
        FileManagementOptions? options = null,
        Action<IServiceCollection>? configure = null)
    {
        var fsql = FileManagementTestSupport.CreateInMemoryFreeSql();
        FileManagementTestSupport.SyncStructure(fsql);

        var blobRoot = Path.Combine(Path.GetTempPath(), "tkfw-fm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(blobRoot);

        return BuildHost(fsql, blobRoot, dbPath: null, options, configure);
    }

    /// <summary>
    /// 文件模式 SQLite 宿主场（供并发测试用）——与 <see cref="Create"/> 相同注册，
    /// 但用文件模式库（多连接共享，FreeSql ObjectPool 正常出借，避免 :memory: 单连接池
    /// 在 CI 高负载并发下 ObjectPool.Get() 超时 10s——FreeSql discussions/1081）。
    /// <paramref name="dbPath"/> 输出库文件路径，由宿主 Dispose 清理。
    /// </summary>
    public static FileManagementTestHost CreateFile(
        out string dbPath,
        FileManagementOptions? options = null,
        Action<IServiceCollection>? configure = null)
    {
        var fsql = FileManagementTestSupport.CreateFileFreeSql(out dbPath);
        FileManagementTestSupport.SyncStructure(fsql);

        var blobRoot = Path.Combine(Path.GetTempPath(), "tkfw-fm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(blobRoot);

        return BuildHost(fsql, blobRoot, dbPath, options, configure);
    }

    /// <summary>宿主构建公共路径（Create 内存模式与 CreateFile 文件模式共用；dbPath 非空=文件模式）。</summary>
    private static FileManagementTestHost BuildHost(
        IFreeSql fsql,
        string blobRoot,
        string? dbPath,
        FileManagementOptions? options,
        Action<IServiceCollection>? configure)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fsql);

        // T3 桥接：IDomainUser 注册保留（DataService ctor 需 IDomainUser——生产经 Use<具体类>() NoAop 直建
        // 不经 DI；接线型 Store 的 GetRequiredService<DataService>() 需 DI 可构造）。桩供可配置用户切换
        // （V0.3.0 用户级配额/所有权测试经 ManagerFor 直构）——生产永不注册 IDomainUser（D01）。
        var user = new TestDomainUser();
        services.AddSingleton<IDomainUser>(sp => { user.Provider = sp; return user; });

        // 接线型 Store 的 DataService 解析源：普通 DI 可构造（wiring 边界测试镜像生产 ADR61 可构造工厂，非 throw-factory）；
        // IEntityDAC<T> 基础设施注册同生产 Host。每 DataService 独立 UnitOfWorkManager（对齐旧 Host 语义——
        // 并发版本上传测试依赖各 DataService 事务隔离，共享 UoW 会改变竞态时序）。
        services.AddScoped<IEntityDAC<FileFolderEntity>>(sp => new FreeSqlEntityDAC<FileFolderEntity>(new UnitOfWorkManager(sp.GetRequiredService<IFreeSql>())));
        services.AddScoped<IEntityDAC<ManagedFileEntity>>(sp => new FreeSqlEntityDAC<ManagedFileEntity>(new UnitOfWorkManager(sp.GetRequiredService<IFreeSql>())));
        services.AddScoped<IEntityDAC<ManagedFileVersionEntity>>(sp => new FreeSqlEntityDAC<ManagedFileVersionEntity>(new UnitOfWorkManager(sp.GetRequiredService<IFreeSql>())));
        services.AddScoped<FileFolderEntityDataService>();
        services.AddScoped<ManagedFileEntityDataService>();
        services.AddScoped<ManagedFileVersionEntityDataService>();   // V0.2.0

        // ITransactionManager（默认 Noop——Create/Update/Delete 写路径事务包裹依赖空操作，
        // DataService 逐操作经 UnitOfWorkManager 持久化；Recording 由 configure 覆盖）
        services.AddSingleton<ITransactionManager, NoopTransactionManager>();

        // BlobStoring 真实链注册：BlobStoringOptions（RootPath=临时目录根）→ LocalStorageService → IBlobStorageService
        // 注：LocalStorageService 的防穿越校验在 BlobStoring 实现项目内（Abstractions 抽取后），直接 new 需引用实现项目
        var blobOptions = Options.Create(new BlobStoringOptions { RootPath = blobRoot, IsEnabled = true });
        services.AddSingleton<IOptions<BlobStoringOptions>>(blobOptions);
        services.AddSingleton<IBlobStorageService>(new LocalStorageService(blobOptions, NullLogger<LocalStorageService>.Instance));

        // FileManagement Options——先于初始化器注册（AddOptions 为 TryAdd 语义，不会覆盖测试实例；
        // 初始化器经 AddOptions/Option 绑定注册的默认配置对已存在 IOptions 实例不生效，测试以显式 options 为准）
        services.AddSingleton<IOptions<FileManagementOptions>>(Options.Create(options ?? new FileManagementOptions()));

        // 扩展初始化器注册 Store（TryAddScoped 接线型）/FileManager（AddConstructibleService 门面）
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        configure?.Invoke(services);

        var provider = services.BuildServiceProvider();
        // 生产路径：绑定当前异步流的解析作用域（DomainHost.NewDomainContext 等价——测试 BindScope）
        DomainUser<TestUserInfo>.BindScope(provider);
        return new FileManagementTestHost(provider, fsql, blobRoot, user, dbPath);
    }

    /// <summary>解析服务（Scoped 服务经根容器解析，生命周期与宿主一致）。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

    /// <summary>
    /// 分层逻辑单测专用：直构 FileManager（internal 类 + IVT）——可配置桩经基类 <c>User</c> 精确控制
    /// IsAuthenticated/UserId/IsSystemActor（V0.3.0 用户级配额/所有权测试；skill §4.5）。
    /// <para>经宿主 DI 解析其余参数（3 Store 接线型 TryAddScoped + IBlobStorageService + ITransactionManager +
    /// IOptions + ILogger）——与守卫工厂 ActivatorUtilities 相同的解析源，仅用户上下文经直构传入。</para>
    /// </summary>
    public IFileManager ManagerFor(TestDomainUser user)
        => new FileManager(
            user,
            _serviceProvider.GetRequiredService<IFileFolderStore>(),
            _serviceProvider.GetRequiredService<IManagedFileStore>(),
            _serviceProvider.GetRequiredService<IManagedFileVersionStore>(),
            _serviceProvider.GetRequiredService<IBlobStorageService>(),
            _serviceProvider.GetRequiredService<ITransactionManager>(),
            _serviceProvider.GetRequiredService<IOptions<FileManagementOptions>>(),
            NullLogger<FileManager>.Instance);

    /// <summary>快捷辅助：创建目录（委托 Manager）。</summary>
    public Task<FileFolderEntity> CreateFolderAsync(
        string code, string name, long? parentId = null, int? sortOrder = null, CancellationToken ct = default)
        => Manager.CreateFolderAsync(code, name, parentId, sortOrder, ct);

    /// <summary>快捷辅助：上传文件（内存内容 → Stream，委托 Manager）。</summary>
    public async Task<ManagedFileEntity> UploadFileAsync(
        long? folderId, string fileName, byte[] content, string? contentType = null, CancellationToken ct = default)
    {
        await using var stream = new MemoryStream(content, writable: false);
        return await Manager.UploadFileAsync(folderId, fileName, stream, contentType, ct);
    }

    /// <summary>快捷辅助：回滚文件到指定版本（委托 Manager，V0.2.0）。</summary>
    public Task<ManagedFileEntity> RollbackFileAsync(long fileId, int version, CancellationToken ct = default)
        => Manager.RollbackFileAsync(fileId, version, ct);

    /// <summary>Blob 临时目录根（断言物理文件落盘/补偿删除用）。</summary>
    public string BlobRoot => _blobRoot;

    /// <summary>Blob 临时目录物理文件总数（递归）——断言残留 Blob。</summary>
    public int BlobCount() => Directory.GetFiles(BlobRoot, "*", SearchOption.AllDirectories).Length;

    /// <summary>SHA256 十六进制（大写）——期望值手算。</summary>
    public static string Sha256Hex(byte[] content)
        => Convert.ToHexString(SHA256.HashData(content));

    /// <summary>
    /// 释放宿主 + 清理 Blob 临时目录（每用例独立根，递归删除）+ 解绑解析作用域。
    /// </summary>
    public void Dispose()
    {
        DomainUser<TestUserInfo>.UnBindScope();
        _serviceProvider.Dispose();
        if (Directory.Exists(_blobRoot))
            Directory.Delete(_blobRoot, recursive: true);
        // 文件模式 SQLite 库清理（宿主释放后连接池已归还，可安全删除）
        if (_dbPath is not null && File.Exists(_dbPath))
        {
            try { File.Delete(_dbPath); }
            catch (IOException) { /* 连接未完全释放时忽略，由系统临时目录回收 */ }
        }
    }
}

/// <summary>
/// 非 seekable 流包装——模拟不可定位上传流（网络流/管道流）。
/// <para>CanSeek=false，Length/Position/Seek 抛 <see cref="NotSupportedException"/>（强制走"边复制边计数"路径）；
/// Read/ReadAsync/CopyToAsync 转发内层实际读写。</para>
/// </summary>
public sealed class NonSeekableStream : Stream
{
    private readonly Stream _inner;

    public NonSeekableStream(Stream inner) => _inner = inner;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush() => _inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
    public override int Read(Span<byte> buffer) => _inner.Read(buffer);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => _inner.ReadAsync(buffer, cancellationToken);

    public override Task CopyToAsync(Stream destination, int bufferSize, CancellationToken cancellationToken)
        => _inner.CopyToAsync(destination, bufferSize, cancellationToken);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            _inner.Dispose();
        base.Dispose(disposing);
    }
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
/// 可配置测试用户桩——实现 IDomainUser 最小契约（匿名/认证切换、系统账号、UserId 注入）。
/// <para>V4.10.53（领域自治根治）：<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价（具体类 DataService——
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c>，IDomainUser 参数显式传 this，
/// IEntityDAC 等其余从 DI 解析——对齐 Settings/Tagging 测试桩）。
/// 认证路径（V0.3.0 用户级配额）经 <see cref="FileManagementTestHost.ManagerFor(TestDomainUser)"/> 直构
/// FileManager（经基类 <c>User</c> 读取上下文）——测试项目无主框架 DomainUserContext IVT，
/// 可配置桩的接口 AOP 解析（设 CurrentAopUser）不可用，故不实现。</para>
/// </summary>
internal sealed class TestDomainUser : IDomainUser
{
    private readonly object _gate = new();
    private readonly Dictionary<Type, object?> _cache = new();

    /// <summary>ServiceProvider（宿主工厂注入，构建后可用——Use&lt;T&gt; 解析源）。</summary>
    public IServiceProvider? Provider { get; set; }

    public string SessionKey { get; set; } = "test-session";

    public bool IsAuthenticated { get; set; } = false;

    public bool IsSystemActor { get; set; } = false;

    public IUserInfo? UserInfo { get; set; }

    public long? TenantId { get; set; }

    public bool IsNoAuditActive { get; set; } = false;

    public string? UserId { get; set; }

    public string? UserName { get; set; }

    public bool IsInRole(string role) => UserInfo?.IsInRole(role) ?? false;

    /// <summary>快捷配置：认证用户（UserId/TenantId/角色列表一步就位；系统账号经 IsSystemActor）。</summary>
    public TestDomainUser AsAuthenticated(string userId, long? tenantId = null, params string[] roles)
    {
        IsAuthenticated = true;
        UserId = userId;
        TenantId = tenantId;
        UserName = userId;
        UserInfo = new TestUserInfo(userId, userId, roles);
        return this;
    }

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
    {
        var provider = Provider ?? throw new NotSupportedException("Stub: Provider 未注入");

        // 具体类（DataService）：NoAop 等价（ActivatorUtilities 直建，IDomainUser 参数显式传 this——IEntityDAC 从 DI 解析）
        if (_cache.TryGetValue(typeof(TDomainService), out var cached) && cached is TDomainService svc)
            return svc;
        lock (_gate)
        {
            if (_cache.TryGetValue(typeof(TDomainService), out cached) && cached is TDomainService svc2)
                return svc2;
            var resolved = (TDomainService)ActivatorUtilities.CreateInstance(provider, typeof(TDomainService), this);
            _cache[typeof(TDomainService)] = resolved;
            return resolved;
        }
    }

    public TService GetService<TService>() where TService : notnull
        => (Provider ?? throw new NotSupportedException("Stub: Provider 未注入")).GetRequiredService<TService>();

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
