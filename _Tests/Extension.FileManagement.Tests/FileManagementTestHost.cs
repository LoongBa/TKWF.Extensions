using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.BlobStoring;
using TKWF.Ext.FileManagement;

namespace TKWF.Ext.FileManagement.Tests;

/// <summary>
/// 测试公共设施——SQLite 内存库创建 + 双实体表结构同步。
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

    /// <summary>同步三张表结构（FileFolder + ManagedFile + ManagedFileVersion——V0.2.0）。</summary>
    public static void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<FileFolderEntity>();
        fsql.CodeFirst.SyncStructure<ManagedFileEntity>();
        fsql.CodeFirst.SyncStructure<ManagedFileVersionEntity>();
    }
}

/// <summary>
/// 完整测试宿主——构建 DI 容器：真实 DataService 链（FreeSqlEntityDAC + UnitOfWorkManager 驱动）
/// + <b>真实 BlobStoring 链</b>（临时目录根 → <c>BlobStoringOptions</c> → <c>LocalStorageService</c> → <c>IBlobStorageService</c>，
/// 每用例独立临时目录，宿主 Dispose 时递归清理）+ StubDomainUser + Noop/Recording 事务管理 + FileManagement Options。
/// <para>每用例独立 <see cref="Create"/> 得到全新 SQLite 内存库 + 全新临时目录（用例隔离）。
/// <paramref name="configure"/> 回调在初始化器之后执行——事务记录型测试可覆盖 ITransactionManager
/// （RemoveAll + AddSingleton Recording，对齐 CalendarTransactionTests 模式）。</para>
/// </summary>
internal sealed class FileManagementTestHost : IDisposable
{
    private readonly ServiceProvider _serviceProvider;
    private readonly string _blobRoot;

    public IFreeSql Fsql { get; }

    public IFileManager Manager => _serviceProvider.GetRequiredService<IFileManager>();

    public IBlobStorageService BlobStorage => _serviceProvider.GetRequiredService<IBlobStorageService>();

    public FileFolderEntityDataService FolderDataService => _serviceProvider.GetRequiredService<FileFolderEntityDataService>();

    public ManagedFileEntityDataService FileDataService => _serviceProvider.GetRequiredService<ManagedFileEntityDataService>();

    private FileManagementTestHost(ServiceProvider serviceProvider, IFreeSql fsql, string blobRoot)
    {
        _serviceProvider = serviceProvider;
        Fsql = fsql;
        _blobRoot = blobRoot;
    }

    /// <summary>全新宿主（每次调用独立 SQLite 内存库 + 独立 Blob 临时目录根）。</summary>
    public static FileManagementTestHost Create(
        FileManagementOptions? options = null,
        Action<IServiceCollection>? configure = null)
    {
        var fsql = FileManagementTestSupport.CreateInMemoryFreeSql();
        FileManagementTestSupport.SyncStructure(fsql);

        // BlobStoring 真实链：临时目录根（每用例独立，宿主 Dispose 清理）
        var blobRoot = Path.Combine(Path.GetTempPath(), "tkfw-fm-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(blobRoot);

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(fsql);

        var stubUser = new StubDomainUser();
        services.AddSingleton<IDomainUser>(stubUser);

        // DataService 链（真实 FreeSql DAC——红线合规委托路径）
        services.AddSingleton(new FileFolderEntityDataService(
            stubUser, new FreeSqlEntityDAC<FileFolderEntity>(new UnitOfWorkManager(fsql))));
        services.AddSingleton(new ManagedFileEntityDataService(
            stubUser, new FreeSqlEntityDAC<ManagedFileEntity>(new UnitOfWorkManager(fsql))));
        services.AddSingleton(new ManagedFileVersionEntityDataService(
            stubUser, new FreeSqlEntityDAC<ManagedFileVersionEntity>(new UnitOfWorkManager(fsql))));   // V0.2.0

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

        // 扩展初始化器注册 Store/Manager/DataService（TryAddScoped 不覆盖已注册 DataService）
        new FileManagementExtensionInitializer<FileManagementUserInfo>().ConfigureServices(services);

        configure?.Invoke(services);

        return new FileManagementTestHost(services.BuildServiceProvider(), fsql, blobRoot);
    }

    /// <summary>解析服务（Scoped 服务经根容器解析，生命周期与宿主一致）。</summary>
    public T GetRequiredService<T>() where T : notnull
        => _serviceProvider.GetRequiredService<T>();

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
    /// 释放宿主 + 清理 Blob 临时目录（每用例独立根，递归删除）。
    /// </summary>
    public void Dispose()
    {
        _serviceProvider.Dispose();
        if (Directory.Exists(_blobRoot))
            Directory.Delete(_blobRoot, recursive: true);
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

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户）。</summary>
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