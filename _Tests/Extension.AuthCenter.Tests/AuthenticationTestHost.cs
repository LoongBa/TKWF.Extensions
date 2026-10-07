using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.AuthController;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// 测试公共设施——SQLite 内存库 + 9 实体 SyncStructure + RSA 测试密钥 + 生产路径等价测试桩。
/// <para>V4.10.53（领域自治根治后重写）：门面现继承 <see cref="TKW.Framework.Domain.DomainServiceBase"/>，DataService 经基类
/// <c>User</c> 懒加载（NoAop 路径）——分层单测用可配置 <see cref="StubDomainUser"/> 直构门面（经基类 User 取上下文），
/// 其 <c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价实现：具体类（DataService）经
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c> 直建（IEntityDAC 从 DI 解析），
/// 接口（IDomainService）经 <c>provider.GetRequiredService(t)</c> 解析（守卫工厂在集成测试经真实 DomainUser 验证，桩内不重复守卫）。</para>
/// <para>集成测试（真实 DI + BindScope + User.Use&lt;接口&gt;()）见 <see cref="AuthenticationProductionPathTests"/>。</para>
/// </summary>
internal static class AuthenticationTestHost
{
    /// <summary>全部 12 实体类型——统一注册真实 FreeSqlEntityDAC（分层单测/生产路径 Use&lt;DataService&gt; 直建源）。
    /// <para>V0.9.0（T2/T8）：+UserProfileEntity（凭据/档案分离）+PasswordResetCodeEntity（找回链路底册）
    /// +PasswordHistoryEntity（密码历史防重用——SetPasswordAsync 历史追加/裁剪依赖）。</para></summary>
    private static readonly Type[] AllEntityTypes =
    [
        typeof(AuthAccountEntity), typeof(AuthLoginAttemptEntity), typeof(SmsRecordEntity),
        typeof(AuthRefreshTokenEntity), typeof(AuthTokenBlacklistEntity), typeof(OAuthTicketEntity),
        typeof(PlatformAccountMapEntity), typeof(PlatformCredentialEntity),
        typeof(AuthGrantEntity),   // V0.8.0：应用授权实体（/grants 数据底座）
        typeof(UserProfileEntity), typeof(PasswordResetCodeEntity),   // V0.9.0：档案 1:1 + 重置码
        typeof(PasswordHistoryEntity)   // V0.9.0：密码历史（历史防重用比对源）
    ];

    /// <summary>注册全部 11 实体真实 FreeSqlEntityDAC（IEntityDAC&lt;T&gt; singleton）——CreateStub 与生产路径 Provider 共用。</summary>
    public static void RegisterEntityDacs(IServiceCollection services)
    {
        foreach (var entityType in AllEntityTypes)
        {
            var dacType = typeof(IEntityDAC<>).MakeGenericType(entityType);
            var implType = typeof(FreeSqlEntityDAC<>).MakeGenericType(entityType);
            services.AddSingleton(dacType, implType);
        }
    }

    private static string? _rsaDir;
    private static readonly object RsaGate = new();

    /// <summary>建表（11 实体 SyncStructure——SQLite 方言）。</summary>
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
        fsql.CodeFirst.SyncStructure<AuthGrantEntity>();
        fsql.CodeFirst.SyncStructure<UserProfileEntity>();          // V0.9.0：档案 1:1
        fsql.CodeFirst.SyncStructure<PasswordResetCodeEntity>();    // V0.9.0：重置码
        fsql.CodeFirst.SyncStructure<PasswordHistoryEntity>();      // V0.9.0：密码历史（防重用）
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

    /// <summary>
    /// 构建生产路径等价测试桩——真实 FreeSqlEntityDAC（全部 9 实体）+ 可选附加服务注册。
    /// 门面直构传本桩（经基类 User 取上下文）；桩内 Use&lt;T&gt;() 走生产 NoAop 路径等价（具体类 ActivatorUtilities 直建，
    /// 接口 GetRequiredService——分层单测需在 <paramref name="register"/> 中注册接口实例，如 ITokenService 供
    /// OAuthTicketService/WeChatApiClient 懒加载）。
    /// <paramref name="user"/> 可选（默认匿名 StubDomainUser）——BindTicketAsync 已认证帧测试传
    /// <see cref="AuthenticatedStubUser"/>（经基类 User 供给 UserId）。
    /// </summary>
    public static StubDomainUser CreateStub(IFreeSql fsql, StubDomainUser? user = null, Action<IServiceCollection, StubDomainUser>? register = null)
    {
        user ??= new StubDomainUser();
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        RegisterEntityDacs(services);
        register?.Invoke(services, user);
        user.ServiceProvider = services.BuildServiceProvider();
        return user;
    }
}

/// <summary>
/// 测试用户桩——实现 <see cref="IDomainUser"/> 最小契约（User/Tenant/认证可配置）。
/// <para>V4.10.53（领域自治根治）：<c>Use&lt;T&gt;()</c> 按生产 NoAop 路径等价实现——具体类（DataService）经
/// <c>ActivatorUtilities.CreateInstance(provider, typeof(T), this)</c> 直建（IDomainUser 参数显式传 this，
/// IEntityDAC 等其余从 DI 解析——对齐 Settings/Account/Identity 测试桩）；接口（IDomainService，如
/// <c>Use&lt;ITokenService&gt;()</c>）经 <c>provider.GetRequiredService(t)</c> 解析（测试注册的门面实例；
/// 守卫工厂在集成测试经真实 DomainUser 验证，桩内不重复守卫）。</para>
/// </summary>
internal class StubDomainUser(string? userId = null, long? tenantId = null, bool isAuthenticated = false) : IDomainUser
{
    /// <summary>ServiceProvider（测试工厂注册时注入——User.Use&lt;T&gt;() 解析源）。</summary>
    public IServiceProvider? ServiceProvider { get; set; }

    public string SessionKey => "test-session";
    public bool IsAuthenticated => isAuthenticated;
    public bool IsSystemActor => false;
    public IUserInfo? UserInfo => null;
    public long? TenantId => tenantId;
    public bool IsNoAuditActive => false;
    public string? UserId => userId;
    public string? UserName => "test";
    public bool IsInRole(string role) => false;

    public TDomainService Use<TDomainService>() where TDomainService : IDomainService
    {
        if (ServiceProvider is null)
            throw new NotSupportedException("Stub: Use<T> 未注入 ServiceProvider");

        // 接口（IDomainService）：DI 解析（测试注册的门面实例——OAuthTicketService.Use<ITokenService>() 等 AOP 懒加载路径）
        if (typeof(TDomainService).IsInterface)
            return ServiceProvider.GetRequiredService<TDomainService>();

        // 具体类：生产 NoAop 路径等价（ActivatorUtilities 直建，IDomainUser 参数显式传 this——IEntityDAC 从 DI 解析）
        return (TDomainService)ActivatorUtilities.CreateInstance(ServiceProvider, typeof(TDomainService), this);
    }

    public TService GetService<TService>() where TService : notnull
    {
        if (ServiceProvider is null)
            throw new NotSupportedException("Stub: GetService<T> 未注入 ServiceProvider");
        return ServiceProvider.GetRequiredService<TService>();
    }

    public TService GetOptionalService<TService>() where TService : class => null!;
    public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
}

/// <summary>认证用户桩——具 userId 的 IDomainUser（仅本人/上下文读取测试用）。</summary>
internal sealed class AuthenticatedStubUser(string userId) : StubDomainUser(userId: userId, isAuthenticated: true);

/// <summary>
/// 测试凭据保护器（Fake——实现 public <see cref="ICredentialProtector"/>，base64 包装往返；
/// 业务验证聚焦组装/比对逻辑，加密细节归框架 <c>AesGcmCredentialProtector</c> 自有测试。
/// ⚠️ 主框架 AesGcmCredentialProtector 为 internal（测试 IVT 不可访问），故测试自建等价桩）。</summary>
internal sealed class FakeCredentialProtector : ICredentialProtector
{
    public string Protect(byte[] clientHash) => Convert.ToBase64String(clientHash);

    public byte[] Unprotect(string protectedBlob)
    {
        try { return Convert.FromBase64String(protectedBlob); }
        catch (FormatException) { throw new CryptographicException("FakeProtector: 非法 blob"); }
    }
}
