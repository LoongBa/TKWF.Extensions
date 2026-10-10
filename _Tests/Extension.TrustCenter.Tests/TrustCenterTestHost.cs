using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.TrustCenter.Tests;

/// <summary>
/// TrustCenter 测试公共设施——SQLite 内存库（SsoClient + AccessCode）+ EC 测试密钥。
/// <para>对齐 AuthenticationTestHost 模式：分层单测用 StubDomainUser 直构门面；生产路径集成测试用
/// 真实 DI + Initializer ConfigureServices + BindScope + <c>User.Use&lt;接口&gt;()</c>。</para>
/// <para><b>TrustCenter 剥离（2026-10-09）</b>：自 Federation.Tests SsoTestHost 迁入——实体裁剪为
/// TrustCenter 2 实体（SsoClientEntity + AccessCodeEntity）；AuthCenter 实体（AuthAccount/PlatformAccountMap/
/// UserProfile——SsoProfileService 消费）与 SsoChannelRegistryEntity（连接层）均留 Federation.Tests。</para>
/// </summary>
internal static class TrustCenterTestHost
{
    /// <summary>TrustCenter 2 实体（信任内核）。</summary>
    private static readonly Type[] EntityTypes =
    [
        typeof(SsoClientEntity), typeof(AccessCodeEntity)
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
            _ecDir ??= Path.Combine(Path.GetTempPath(), "tkwf-trustcenter-tests-" + Guid.NewGuid().ToString("N"));
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

    /// <summary>构造测试用 TrustCenterOptions（生产模式 + EC 密钥 + AES-GCM 密钥文件【实际写入】）。</summary>
    public static TrustCenterOptions CreateOptions(string? kid = "sso-test-key-1")
    {
        var (privatePath, publicPath) = CreateEcKeyFiles(kid);
        var aesKeyPath = Path.Combine(Path.GetTempPath(), "tkwf-trustcenter-tests-aes-" + Guid.NewGuid().ToString("N") + ".key");
        // 生产模式（IsProduction=true）要求密钥文件已预置——测试宿主实际写入 32 字节
        if (!File.Exists(aesKeyPath))
            File.WriteAllBytes(aesKeyPath, RandomNumberGenerator.GetBytes(32));
        return new TrustCenterOptions
        {
            Issuer = "https://sso.test.local",
            SigningKeyPath = privatePath,
            CurrentKid = kid,
            SigningKeys = [new FederationSigningKeyConfig { Kid = kid, PrivateKeyPath = privatePath, PublicKeyPath = publicPath }],
            SecretEncryptionKeyPath = aesKeyPath,
            IsProduction = true,
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
