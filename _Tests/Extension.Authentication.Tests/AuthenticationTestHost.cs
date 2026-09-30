using System;
using System.IO;
using System.Security.Cryptography;
using FreeSql;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Authentication;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>
/// 测试公共设施——SQLite 内存库 + 8 实体 SyncStructure + RSA 测试密钥 + DataService/服务工厂。
/// <para>红线合规测试模式：真实 <see cref="FreeSqlEntityDAC{TEntity}"/> 驱动（对齐既有扩展测试先例）；
/// 测试 Host <b>不手动注册 DataService</b>——经 SG1 生成 ProjectMetaContext（消费方真实形态，ADR61）。</para>
/// </summary>
internal static class AuthenticationTestHost
{
    private static string? _rsaDir;
    private static readonly object RsaGate = new();

    /// <summary>建表（8 实体 SyncStructure——SQLite 方言）。</summary>
    public static void SyncSchema(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<AuthAccountEntity>();
        fsql.CodeFirst.SyncStructure<AuthLoginAttemptEntity>();
        fsql.CodeFirst.SyncStructure<SmsRecordEntity>();
        fsql.CodeFirst.SyncStructure<AuthRefreshTokenEntity>();
        fsql.CodeFirst.SyncStructure<AuthTokenBlacklistEntity>();
        fsql.CodeFirst.SyncStructure<OAuthTicketEntity>();
        fsql.CodeFirst.SyncStructure<PlatformAccountMapEntity>();
        fsql.CodeFirst.SyncStructure<PlatformCredentialEntity>();
    }

    /// <summary>创建使用 SQLite 内存库的 IFreeSql + 建表（每次调用新连接 = 独立内存库）。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        SyncSchema(fsql);
        return fsql;
    }

    /// <summary>生成 RSA 测试密钥 PEM（临时目录，缓存供多测试复用——kid 轮换场景用 SigningKeys）。
    /// ⚠️ xunit 并行测试类并发写入同一文件 → 静态锁串行化（对齐 FileManagement 已知竞态处理）。</summary>
    public static (string PrivateKeyPath, string PublicKeyPath) CreateRsaKeyFiles(string kid = "test-key-1")
    {
        lock (RsaGate)
        {
            _rsaDir ??= Path.Combine(Path.GetTempPath(), "tkwf-auth-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rsaDir);
            var privatePath = Path.Combine(_rsaDir, $"{kid}-private.pem");
            var publicPath = Path.Combine(_rsaDir, $"{kid}-public.pem");
            if (!File.Exists(privatePath))
            {
                var rsa = RSA.Create(2048);
                File.WriteAllText(privatePath, rsa.ExportPkcs8PrivateKeyPem());
                File.WriteAllText(publicPath, rsa.ExportSubjectPublicKeyInfoPem());
                rsa.Dispose();
            }
            return (privatePath, publicPath);
        }
    }

    /// <summary>清理 RSA 临时目录（测试收尾）。</summary>
    public static void CleanupRsaKeyFiles()
    {
        if (_rsaDir != null && Directory.Exists(_rsaDir))
        {
            Directory.Delete(_rsaDir, true);
            _rsaDir = null;
        }
    }

    /// <summary>标准测试 AuthCenterOptions（Issuer + RSA 密钥 + 生产 fail-fast 验证用配置）。</summary>
    public static AuthCenterOptions CreateOptions(string? issuer = "auth-test", bool isProduction = false)
    {
        var (privatePath, publicPath) = CreateRsaKeyFiles();
        return new AuthCenterOptions
        {
            Issuer = issuer ?? "auth-test",
            SigningKeyPath = privatePath,
            PublicKeyPath = publicPath,
            CurrentKid = "test-key-1",
            SigningKeys =
            [
                new SigningKeyConfig { Kid = "test-key-1", PrivateKeyPath = privatePath, PublicKeyPath = publicPath }
            ],
            IsProduction = isProduction,
            RedirectUriWhitelist = ["https://app.example.com/callback"]
        };
    }
}

/// <summary>测试用户桩——实现 IDomainUser 最小契约（匿名用户，无租户；对齐既有扩展测试 StubDomainUser）。
/// <para>可注入 <see cref="IOptions{AuthCenterOptions}"/>（GetOptionalService 解析）——早期 PlatformCredentialEntityDataService
/// 经 GetOptionalService 加载密钥路径所需；Oracle M4 合并后 DataService 改经 PlatformCredentialKeyStore（Service 构造初始化），
/// 此注入仅为兼容保留（PlatformCredentialServiceTests 仍传 Options——无副作用）。</para></summary>
internal sealed class StubDomainUser : IDomainUser
{
    private readonly IOptions<AuthCenterOptions>? _options;

    public StubDomainUser(IOptions<AuthCenterOptions>? options = null)
    {
        _options = options;
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
        => throw new NotSupportedException("Stub: Use<T> not supported in unit tests");

    public TService GetService<TService>() where TService : notnull
        => throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");

    public TService GetOptionalService<TService>() where TService : class
    {
        if (typeof(TService) == typeof(IOptions<AuthCenterOptions>) && _options != null)
            return (TService)(object)_options;
        return null!;
    }
    public System.Collections.Generic.IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}
