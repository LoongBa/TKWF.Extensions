using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.FreeSql;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// 多通道联邦 Phase 2 测试（DB 动态权威层）——<see cref="SsoChannelRegistryEntity"/> 实体 + 注册表读写加密 +
/// <see cref="CompositeChannelRegistry"/> 组合回退链（DB 命中优先 → Static 回退）。
/// <para>生产路径宿主模式（对齐 SsoProductionPathTests）：真实 DI（FederationExtensionInitializer.ConfigureServices +
/// FreeSql SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> +
/// <c>User.Use&lt;接口&gt;()</c>（AOP 路径——设 CurrentAopUser → GetRequiredService → 守卫工厂）。</para>
/// <para>加解密断言：实体行直接查（经 <c>User.Use&lt;SsoChannelRegistryEntityDataService&gt;()</c> NoAop 直建）
/// 断言密文非明文；经 registry 回读断言解密后明文一致。数据访问红线合规（零 IFreeSql/IEntityDAC 直注入）。</para>
/// </summary>
public class SsoChannelRegistryTests : TestHostBase
{
    protected override TestUserInfo CreateUser() => new("registry-user", "通道注册表测试用户");

    protected override void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<SsoChannelRegistryEntity>();
    }

    protected override void ConfigureHost(IServiceCollection services)
    {
        // Federation 扩展初始器（守卫工厂 + keyed 对称密钥 + 注册表门面）
        new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // Options 值绑定（keyed FileSymmetricKeyProvider 依赖 SecretEncryptionKeyPath——测试宿主写密钥文件）
        var opts = SsoTestHost.CreateOptions();
        services.Configure<FederationOptions>(o =>
        {
            o.Issuer = opts.Issuer;
            o.SigningKeyPath = opts.SigningKeyPath;
            o.CurrentKid = opts.CurrentKid;
            o.SigningKeys = opts.SigningKeys;
            o.SecretEncryptionKeyPath = opts.SecretEncryptionKeyPath;
            o.IsProduction = opts.IsProduction;
        });

        // 静态通道来源 stub（Composite 回退链断言——DB 无命中时回退静态）
        services.AddSingleton<IChannelSource>(new StubStaticSource(
            new ChannelConfig
            {
                ChannelId = "static-mp-1",
                PlatformType = "wechat",
                AppId = "wx-static-app",
                AppSecret = "static-secret-1",
                IsEnabled = true,
            },
            new ChannelConfig
            {
                ChannelId = "shared-mp",
                PlatformType = "wechat",
                AppId = "wx-static-app",
                AppSecret = "static-secret-shared", // 与 DB 注册同 id——DB 命中优先断言
                IsEnabled = true,
            }));

        // 实体 DAC（真实 FreeSqlEntityDAC——全部实体含 SsoChannelRegistryEntity）
        SsoTestHost.RegisterEntityDacs(services);
    }

    // ── 1. Register → GetAsync 回读（AppSecret/Extra 加密落库 + 回读解密）──

    /// <summary>生产路径：Register 加密落库（密文非明文）→ registry 回读解密一致（AOP 帧内）。</summary>
    [Fact]
    public async Task Register_EncryptedInDb_DecryptsThroughRegistry()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var svc = user.Use<ISsoChannelRegistryService>();
        await svc.RegisterAsync(new ChannelConfig
        {
            ChannelId = "db-mp-1",
            PlatformType = "wechat",
            AppId = "wx-db-app",
            AppSecret = "db-secret-1",
            Extra = new Dictionary<string, string?> { ["Token"] = "tok-1", ["EncodingAESKey"] = "aes-1" },
            IsEnabled = true,
        }, CancellationToken.None);

        // 直接查实体行——密文列非明文（AES-GCM 密文）
        var row = await user.Use<SsoChannelRegistryEntityDataService>()
            .EntityGetAsync(m => m.ChannelId == "db-mp-1", CancellationToken.None);
        Assert.NotNull(row);
        Assert.Equal("db-mp-1", row!.ChannelId);
        Assert.NotNull(row.AppSecretEncrypted);
        Assert.DoesNotContain("db-secret-1", row.AppSecretEncrypted);
        Assert.DoesNotContain("tok-1", row.ExtraJsonEncrypted);

        // 经 registry（Composite）回读——解密后明文一致
        var registry = user.Use<IChannelRegistry>();
        var cfg = await registry.GetAsync("db-mp-1", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("wx-db-app", cfg!.AppId);
        Assert.Equal("db-secret-1", cfg.AppSecret);
        Assert.Equal("tok-1", cfg.Extra["Token"]);
        Assert.Equal("aes-1", cfg.Extra["EncodingAESKey"]);
        Assert.True(cfg.IsEnabled);
    }

    // ── 2. UpdateAsync 重写（IsEnabled 翻转）──

    /// <summary>生产路径：Update 重写——IsEnabled 翻转 + AppSecret 重加密后回读一致。</summary>
    [Fact]
    public async Task Update_RewritesIsEnabled_DecryptsNewSecret()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var svc = user.Use<ISsoChannelRegistryService>();
        await svc.RegisterAsync(new ChannelConfig
        {
            ChannelId = "db-mp-2",
            PlatformType = "wechat",
            AppId = "wx-db-app",
            AppSecret = "old-secret",
            Extra = new Dictionary<string, string?> { ["Token"] = "tok-2" },
            IsEnabled = true,
        }, CancellationToken.None);

        // 重写：AppId 变更 + IsEnabled 翻转 + AppSecret 更换
        var updated = await svc.UpdateAsync("db-mp-2", new ChannelConfig
        {
            ChannelId = "db-mp-2",
            PlatformType = "wechat",
            AppId = "wx-db-app-v2",
            AppSecret = "new-secret",
            Extra = new Dictionary<string, string?> { ["Token"] = "tok-2", ["Tenant"] = "corp-9" },
            IsEnabled = false,
        }, CancellationToken.None);
        Assert.True(updated);

        var cfg = await user.Use<IChannelRegistry>().GetAsync("db-mp-2", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("wx-db-app-v2", cfg!.AppId);
        Assert.False(cfg.IsEnabled);          // IsEnabled 翻转生效
        Assert.Equal("new-secret", cfg.AppSecret);
        Assert.Equal("tok-2", cfg.Extra["Token"]);
        Assert.Equal("corp-9", cfg.Extra["Tenant"]);
    }

    /// <summary>Update 不存在的 channelId → 返回 false（CHANNEL_NOT_FOUND 语义）。</summary>
    [Fact]
    public async Task Update_ChannelNotFound_ReturnsFalse()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var svc = user.Use<ISsoChannelRegistryService>();
        var updated = await svc.UpdateAsync("nonexistent", new ChannelConfig
        {
            ChannelId = "nonexistent",
            PlatformType = "wechat",
            AppId = "wx",
            AppSecret = "s",
            IsEnabled = true,
        }, CancellationToken.None);
        Assert.False(updated);
    }

    // ── 3. UnregisterAsync 删除 + 再查 null（CHANNEL_NOT_FOUND）──

    /// <summary>生产路径：Unregister 删行 → registry 回读 null（CHANNEL_NOT_FOUND）。</summary>
    [Fact]
    public async Task Unregister_DeletesRow_RegistryReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var svc = user.Use<ISsoChannelRegistryService>();
        await svc.RegisterAsync(new ChannelConfig
        {
            ChannelId = "db-mp-3",
            PlatformType = "wechat",
            AppId = "wx-db-app",
            AppSecret = "secret-3",
            IsEnabled = true,
        }, CancellationToken.None);

        var removed = await svc.UnregisterAsync("db-mp-3", CancellationToken.None);
        Assert.True(removed);

        // 再次查询 → null（CHANNEL_NOT_FOUND）
        Assert.Null(await user.Use<IChannelRegistry>().GetAsync("db-mp-3", CancellationToken.None));
        // 行已物理删除
        var row = await user.Use<SsoChannelRegistryEntityDataService>()
            .EntityGetAsync(m => m.ChannelId == "db-mp-3", CancellationToken.None);
        Assert.Null(row);
    }

    /// <summary>Unregister 不存在的 channelId → 返回 false。</summary>
    [Fact]
    public async Task Unregister_ChannelNotFound_ReturnsFalse()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        Assert.False(await user.Use<ISsoChannelRegistryService>().UnregisterAsync("nonexistent", CancellationToken.None));
    }

    // ── 4. Composite 回退链（DB 命中优先 → Static 回退）──

    /// <summary>DB 无该 channel → Static 命中（回退链）。</summary>
    [Fact]
    public async Task Composite_DbMiss_FallsBackToStatic()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var cfg = await user.Use<IChannelRegistry>().GetAsync("static-mp-1", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("static-secret-1", cfg!.AppSecret);
        Assert.Equal("wechat", cfg.PlatformType);
    }

    /// <summary>DB 有该 channel（与 Static 同 id 但值不同）→ DB 命中优先。</summary>
    [Fact]
    public async Task Composite_DbHit_PrefersDbOverStatic()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        // DB 注册同 id "shared-mp（静态层 AppSecret=static-secret-shared）——DB 值优先
        var svc = user.Use<ISsoChannelRegistryService>();
        await svc.RegisterAsync(new ChannelConfig
        {
            ChannelId = "shared-mp",
            PlatformType = "wechat",
            AppId = "wx-db-app",
            AppSecret = "db-secret-shared",
            IsEnabled = true,
        }, CancellationToken.None);

        var cfg = await user.Use<IChannelRegistry>().GetAsync("shared-mp", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("db-secret-shared", cfg!.AppSecret);   // DB 优先（非静态 static-secret-shared）
        Assert.Equal("wx-db-app", cfg.AppId);
    }

    /// <summary>Composite GetAllAsync——DB + Static 合并，同 id 去重保留 DB 值。</summary>
    [Fact]
    public async Task Composite_GetAll_MergesDbAndStatic_DbDedupWins()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        // DB 注册两个通道（其一与静态同 id）
        var svc = user.Use<ISsoChannelRegistryService>();
        await svc.RegisterAsync(new ChannelConfig
        {
            ChannelId = "db-only-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "db-only", IsEnabled = true,
        }, CancellationToken.None);
        await svc.RegisterAsync(new ChannelConfig
        {
            ChannelId = "shared-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "db-secret-shared", IsEnabled = true,
        }, CancellationToken.None);

        var all = await user.Use<IChannelRegistry>().GetAllAsync(CancellationToken.None);
        var byId = all.ToDictionary(c => c.ChannelId!, StringComparer.Ordinal);
        Assert.Contains("static-mp-1", byId);                     // 静态回退项仍枚举
        Assert.Equal("static-secret-1", byId["static-mp-1"].AppSecret);
        Assert.Contains("db-only-mp", byId);                      // 仅 DB 项
        Assert.Contains("shared-mp", byId);
        Assert.Equal("db-secret-shared", byId["shared-mp"].AppSecret); // 同 id → DB 值优先
    }

    /// <summary>Composite GetDefaultAsync——DB IsDefault 优先；无 DB 默认 → 静态默认回退。</summary>
    [Fact]
    public async Task Composite_GetDefault_DbDefaultPreferred_StaticFallback()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        // 无 DB 默认标记 → 回退静态默认（StubStaticSource 无 IsDefault → 静态首项）
        var def = await user.Use<IChannelRegistry>().GetDefaultAsync(CancellationToken.None);
        Assert.NotNull(def);
        Assert.Equal("static-mp-1", def!.ChannelId);

        // DB 注册 IsDefault 通道 → DB 默认优先
        await user.Use<ISsoChannelRegistryService>().RegisterAsync(new ChannelConfig
        {
            ChannelId = "db-default-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "db-def", IsDefault = true, IsEnabled = true,
        }, CancellationToken.None);
        var dbDef = await user.Use<IChannelRegistry>().GetDefaultAsync(CancellationToken.None);
        Assert.NotNull(dbDef);
        Assert.Equal("db-default-mp", dbDef!.ChannelId);
    }

    // ── 5. 注册冲突语义（Register 同 ChannelId → DomainException CONFLICT）──

    /// <summary>Register 同 ChannelId 冲突 → DomainException（CONFLICT，fail-closed）。</summary>
    [Fact]
    public async Task Register_DuplicateChannelId_ThrowsConflict()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var svc = user.Use<ISsoChannelRegistryService>();
        var cfg = new ChannelConfig
        {
            ChannelId = "dup-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "s1", IsEnabled = true,
        };
        await svc.RegisterAsync(cfg, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => svc.RegisterAsync(new ChannelConfig
            {
                ChannelId = "dup-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "s2", IsEnabled = true,
            }, CancellationToken.None));
        Assert.Equal(DomainException.ErrorCodes.Conflict, ex.ErrorCode);
    }

    // ── 无 Db 注册降级（未接线 DB 动态权威层 → Composite 纯净回退静态） ──

    /// <summary>仅静态层注册（无 DbChannelRegistry）——Composite 降级静态，零破坏（对齐平台库测试宿主形态）。</summary>
    [Fact]
    public async Task Composite_NoDbRegistered_FallsBackToStaticOnly()
    {
        using var host = new StaticOnlyHost();
        var (_, user) = host.Bind();

        Assert.NotNull(await user.Use<IChannelRegistry>().GetAsync("static-mp-1", CancellationToken.None));
        Assert.Null(await user.Use<IChannelRegistry>().GetAsync("no-such-channel", CancellationToken.None));
    }

    /// <summary>仅静态注册的专用宿主（复刻平台库测试宿主最小面——不注册 DbChannelRegistry 与注册表实体 DAC）。</summary>
    private sealed class StaticOnlyHost : TestHostBase
    {
        protected override TestUserInfo CreateUser() => new("static-only", "仅静态宿主");

        /// <summary>public 包装 BindTestScope（protected——外部测试类不可直接访问）。</summary>
        public (ServiceProvider Provider, DomainUser<TestUserInfo> User) Bind() => BindTestScope();

        protected override void ConfigureHost(IServiceCollection services)
        {
            // 对齐平台库测试宿主最小面（OidcTestHost/WeChatTestHost 等）：不含 DbChannelRegistry/注册表实体
            services.AddOptions<FederationStaticChannelOptions>();
            services.AddScoped<StaticChannelRegistry>();
            services.AddConstructibleService<IChannelRegistry, CompositeChannelRegistry>();
            services.AddSingleton<IChannelSource>(new StubStaticSource(
                new ChannelConfig
                {
                    ChannelId = "static-mp-1", PlatformType = "wechat", AppId = "wx-static", AppSecret = "static-secret-1", IsEnabled = true,
                }));
        }
    }

    /// <summary>静态通道来源 stub（IChannelSource——StatịcChannelRegistry 聚合投影）。</summary>
    private sealed class StubStaticSource : IChannelSource
    {
        private readonly ChannelConfig[] _channels;

        public StubStaticSource(params ChannelConfig[] channels) => _channels = channels;

        public string PlatformType => "stub";
        public IReadOnlyList<ChannelConfig> GetChannels() => _channels;
    }
}