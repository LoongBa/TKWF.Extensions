using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Authentication;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// SSO 测试公共设施——SQLite 内存库（SsoClient + SsoAccessCode + 认证内核 AuthAccount/PlatformAccountMap）+ EC 测试密钥。
/// <para>对齐 AuthenticationTestHost 模式：分层单测用 StubDomainUser 直构门面；生产路径集成测试用
/// 真实 DI + Initializer ConfigureServices + BindScope + <c>User.Use&lt;接口&gt;()</c>。</para>
/// </summary>
internal static class SsoTestHost
{
    /// <summary>SSO 2 实体 + 认证内核 2 实体（SSO profile 消费 ISsoAccountQueryService 需 AuthAccount 表）。</summary>
    private static readonly Type[] EntityTypes =
    [
        typeof(SsoClientEntity), typeof(SsoAccessCodeEntity),
        typeof(AuthAccountEntity), typeof(PlatformAccountMapEntity)
    ];

    /// <summary>注册全部实体真实 FreeSqlEntityDAC（IEntityDAC&lt;T&gt; singleton）。</summary>
    public static void RegisterEntityDacs(IServiceCollection services)
    {
        foreach (var entityType in EntityTypes)
        {
            var dacType = typeof(IEntityDAC<>).MakeGenericType(entityType);
            var implType = typeof(FreeSqlEntityDAC<>).MakeGenericType(entityType);
            services.AddSingleton(dacType, implType);
        }
    }

    /// <summary>建表（SyncStructure——SQLite 方言）。</summary>
    public static void SyncSchema(IFreeSql fsql)
    {
        foreach (var entityType in EntityTypes)
            fsql.CodeFirst.SyncStructure(entityType);
    }

    /// <summary>创建 SQLite 内存库 + 建表。</summary>
    public static IFreeSql CreateInMemoryFreeSql()
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        SyncSchema(fsql);
        return fsql;
    }

    private static string? _ecDir;
    private static readonly object EcGate = new();

    /// <summary>生成 EC P-256 测试密钥 PEM（PKCS#8——临时目录，缓存供多测试复用）。</summary>
    public static (string PrivateKeyPath, string PublicKeyPath) CreateEcKeyFiles(string kid = "sso-test-key-1")
    {
        lock (EcGate)
        {
            _ecDir ??= Path.Combine(Path.GetTempPath(), "tkwf-sso-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_ecDir);
            var privatePath = Path.Combine(_ecDir, $"{kid}-private.pem");
            var publicPath = Path.Combine(_ecDir, $"{kid}-public.pem");

            if (File.Exists(privatePath)) return (privatePath, publicPath);

            using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
            File.WriteAllText(privatePath, ec.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(publicPath, ec.ExportSubjectPublicKeyInfoPem());
            return (privatePath, publicPath);
        }
    }

    /// <summary>构造测试用 FederationOptions（生产模式 + EC 密钥 + AES-GCM 密钥文件【实际写入】）。</summary>
    public static FederationOptions CreateOptions(string? kid = "sso-test-key-1")
    {
        var (privatePath, publicPath) = CreateEcKeyFiles(kid);
        var aesKeyPath = Path.Combine(Path.GetTempPath(), "tkwf-sso-tests-aes-" + Guid.NewGuid().ToString("N") + ".key");
        // 生产模式（IsProduction=true）要求密钥文件已预置——测试宿主实际写入 32 字节
        if (!File.Exists(aesKeyPath))
            File.WriteAllBytes(aesKeyPath, RandomNumberGenerator.GetBytes(32));
        return new FederationOptions
        {
            Issuer = "https://sso.test.local",
            SigningKeyPath = privatePath,
            CurrentKid = kid,
            SigningKeys = [new FederationSigningKeyConfig { Kid = kid, PrivateKeyPath = privatePath, PublicKeyPath = publicPath }],
            SecretEncryptionKeyPath = aesKeyPath,
            IsProduction = true,
        };
    }

    private static string? _rsaDir;
    private static readonly object RsaGate = new();

    /// <summary>生成 RSA 测试密钥 PEM（认证内核 AuthCenterOptions 用——临时目录，缓存复用）。</summary>
    public static (string PrivateKeyPath, string PublicKeyPath) CreateRsaKeyFiles()
    {
        lock (RsaGate)
        {
            _rsaDir ??= Path.Combine(Path.GetTempPath(), "tkwf-sso-tests-rsa-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_rsaDir);
            var privatePath = Path.Combine(_rsaDir, "auth-private.pem");
            var publicPath = Path.Combine(_rsaDir, "auth-public.pem");
            if (File.Exists(privatePath)) return (privatePath, publicPath);
            var rsa = RSA.Create(2048);
            File.WriteAllText(privatePath, rsa.ExportPkcs8PrivateKeyPem());
            File.WriteAllText(publicPath, rsa.ExportSubjectPublicKeyInfoPem());
            rsa.Dispose();
            return (privatePath, publicPath);
        }
    }

    /// <summary>构造测试用 AuthCenterOptions（认证内核——SSO profile 消费 ISsoAccountQueryService 实现）。</summary>
    public static AuthCenterOptions CreateAuthOptions()
    {
        var (privatePath, publicPath) = CreateRsaKeyFiles();
        return new AuthCenterOptions
        {
            Issuer = "auth-test",
            SigningKeyPath = privatePath,
            PublicKeyPath = publicPath,
            CurrentKid = "test-key-1",
            SigningKeys = [new SigningKeyConfig { Kid = "test-key-1", PrivateKeyPath = privatePath, PublicKeyPath = publicPath }],
            IsProduction = false,
            RedirectUriWhitelist = ["https://app.example.com/callback"],
        };
    }

    /// <summary>
    /// 构建生产路径等价测试桩——真实 FreeSqlEntityDAC + 可选附加服务注册。
    /// 门面直构传本桩（经基类 User 取上下文）；桩内 Use&lt;T&gt;() 走生产 NoAop 路径等价
    /// （具体类 ActivatorUtilities 直建，接口 GetRequiredService——对齐 AuthenticationTestHost.CreateStub）。
    /// </summary>
    public static StubDomainUser CreateStub(IFreeSql fsql, Action<IServiceCollection, StubDomainUser>? register = null)
    {
        var user = new StubDomainUser();
        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        RegisterEntityDacs(services);
        register?.Invoke(services, user);
        user.ServiceProvider = services.BuildServiceProvider();
        return user;
    }
}
