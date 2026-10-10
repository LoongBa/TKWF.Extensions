using System;
using System.IO;
using System.Security.Cryptography;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// Federation 连接层测试公共设施——SQLite 内存库（SsoChannelRegistryEntity + 认证内核 AuthAccount/PlatformAccountMap/UserProfile）+ 密钥。
/// <para><b>TrustCenter 剥离（2026-10-09）</b>：原 SsoTestHost 迁 TrustCenter.Tests 改名 TrustCenterTestHost（信任内核 2 实体）；
/// 本类为 Federation.Tests 自持连接层设施——SsoChannelRegistryEntity（多通道配置持久化）+ 认证内核实体
/// （SsoProfileService 消费 ISsoAccountQueryService 需 AuthAccount + UserProfile）+ 密钥助手（RSA 认证内核 / AES 通道注册表）。</para>
/// </summary>
internal static class FederationTestHost
{
    /// <summary>连接层实体（SsoChannelRegistryEntity）+ 认证内核实体（SsoProfileService 跨扩展消费）。</summary>
    private static readonly Type[] EntityTypes =
    [
        typeof(SsoChannelRegistryEntity),
        typeof(AuthAccountEntity), typeof(PlatformAccountMapEntity), typeof(UserProfileEntity)
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

    private static string? _rsaDir;
    private static readonly object RsaGate = new();

    /// <summary>生成 RSA 测试密钥 PEM（认证内核 AuthCenterOptions 用——临时目录，缓存复用）。</summary>
    public static (string PrivateKeyPath, string PublicKeyPath) CreateRsaKeyFiles()
    {
        lock (RsaGate)
        {
            _rsaDir ??= Path.Combine(Path.GetTempPath(), "tkwf-federation-tests-rsa-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>构造测试用 AuthCenterOptions（认证内核——SsoProfileService 消费 ISsoAccountQueryService 实现）。</summary>
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

    private static string? _aesDir;
    private static readonly object AesGate = new();

    /// <summary>构造测试用 AES-GCM 密钥文件（通道注册表 FederationChannelRegistryOptions 用——临时目录，缓存复用）。</summary>
    public static string CreateAesKeyPath()
    {
        lock (AesGate)
        {
            _aesDir ??= Path.Combine(Path.GetTempPath(), "tkwf-federation-tests-aes-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_aesDir);
            var keyPath = Path.Combine(_aesDir, "channel-registry-aes.key");
            if (!File.Exists(keyPath))
                File.WriteAllBytes(keyPath, RandomNumberGenerator.GetBytes(32));
            return keyPath;
        }
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
