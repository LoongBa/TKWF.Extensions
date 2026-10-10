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
        // ⚠️ TrustCenter 剥离：FederationOptions 迁 TrustCenter（TrustCenterOptions）——连接层通道注册表
        // 密钥独立承载于 FederationChannelRegistryOptions（TKWF:Federation:ChannelRegistry 节）
        var aesKeyPath = FederationTestHost.CreateAesKeyPath();
        services.Configure<FederationChannelRegistryOptions>(o =>
        {
            o.SecretEncryptionKeyPath = aesKeyPath;
            o.IsProduction = true;
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
            },
            new ChannelConfig
            {
                ChannelId = "static-alias-mp",
                PlatformType = "wechat",
                AppId = "wx-static-app",
                AppSecret = "static-secret-alias",
                Alias = "merchant-a",   // Phase 3（§3.7 双键）——静态 alias 对外名解析用例
                IsEnabled = true,
            }));

        // 实体 DAC（真实 FreeSqlEntityDAC——SsoChannelRegistryEntity 等连接层实体）
        FederationTestHost.RegisterEntityDacs(services);
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

    // ── 6. ChannelAlias 对外别名双键（Phase 3，方案 §3.7——GetByAliasOrIdAsync 统一解析入口） ──

    /// <summary>静态 Channels 配 Alias → GetByAliasOrIdAsync(alias) 返回对应通道（对外别名精确匹配）。</summary>
    [Fact]
    public async Task Registry_GetByAliasOrId_Static_AliasMatches()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var cfg = await user.Use<IChannelRegistry>().GetByAliasOrIdAsync("merchant-a", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("static-alias-mp", cfg!.ChannelId);       // alias → 对应内部 ChannelId
        Assert.Equal("merchant-a", cfg.Alias);                 // 对外别名回读一致
        Assert.Equal("static-secret-alias", cfg.AppSecret);
    }

    /// <summary>无 Alias 通道 → GetByAliasOrIdAsync(channelId) 命中——缺省 = ChannelId 对外（存量零迁移）。</summary>
    [Fact]
    public async Task Registry_GetByAliasOrId_FallsBackToChannelId()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        // static-mp-1 无 Alias（缺省 = 用 ChannelId 对外）——按内部键解析命中
        var cfg = await user.Use<IChannelRegistry>().GetByAliasOrIdAsync("static-mp-1", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("static-mp-1", cfg!.ChannelId);
        Assert.Null(cfg.Alias);   // 未配别名——回退 ChannelId 匹配
        Assert.Equal("static-secret-1", cfg.AppSecret);

        // 未知 key → null（CHANNEL_NOT_FOUND 语义）
        Assert.Null(await user.Use<IChannelRegistry>().GetByAliasOrIdAsync("no-such-alias", CancellationToken.None));
    }

    /// <summary>DB 注册 Alias → Composite 命中 DB alias 优先于静态同 ChannelId（DB 动态权威层对外名选区）。</summary>
    [Fact]
    public async Task Registry_GetByAliasOrId_Db_PrefersAlias()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        // DB 注册通道：Alias = "static-mp-1"（与静态层 ChannelId 同名）——解析该 key 应命中 DB alias 行，而非静态回退
        await user.Use<ISsoChannelRegistryService>().RegisterAsync(new ChannelConfig
        {
            ChannelId = "db-pri-mp",
            PlatformType = "wechat",
            AppId = "wx-db-app",
            AppSecret = "db-secret-pri",
            Alias = "static-mp-1",
            IsEnabled = true,
        }, CancellationToken.None);

        var cfg = await user.Use<IChannelRegistry>().GetByAliasOrIdAsync("static-mp-1", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("db-pri-mp", cfg!.ChannelId);       // DB alias 命中（非静态 ChannelId 回退）
        Assert.Equal("db-secret-pri", cfg.AppSecret);    // DB 值优先（静态 static-secret-1 被覆盖）
        Assert.Equal("static-mp-1", cfg.Alias);
    }

    /// <summary>同 Alias 二次注册（不同 ChannelId）→ DomainException CONFLICT（fail-closed，方案 P3-3）。</summary>
    [Fact]
    public async Task Register_DuplicateAlias_ThrowsConflict()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var svc = user.Use<ISsoChannelRegistryService>();
        await svc.RegisterAsync(new ChannelConfig
        {
            ChannelId = "alias-owner-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "s1",
            Alias = "merchant-b", IsEnabled = true,
        }, CancellationToken.None);

        // 第二通道复用同 alias → Conflict
        var ex = await Assert.ThrowsAsync<DomainException>(
            () => svc.RegisterAsync(new ChannelConfig
            {
                ChannelId = "alias-intruder-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "s2",
                Alias = "merchant-b", IsEnabled = true,
            }, CancellationToken.None));
        Assert.Equal(DomainException.ErrorCodes.Conflict, ex.ErrorCode);

        // 自身保留 alias 合法（Update 排除本行）——同 ChannelId 重写不冲突
        var updated = await svc.UpdateAsync("alias-owner-mp", new ChannelConfig
        {
            ChannelId = "alias-owner-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "s1-v2",
            Alias = "merchant-b", IsEnabled = true,
        }, CancellationToken.None);
        Assert.True(updated);
    }

    /// <summary>Alias null 重写 → ChannelAlias 清空（清空语义）+ 再按旧 alias 解析 null（缺省回落 ChannelId）。</summary>
    [Fact]
    public async Task Update_ClearsAlias()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var svc = user.Use<ISsoChannelRegistryService>();
        await svc.RegisterAsync(new ChannelConfig
        {
            ChannelId = "clear-alias-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "s1",
            Alias = "old-alias", IsEnabled = true,
        }, CancellationToken.None);

        // 确认 alias 已落库可解析
        Assert.NotNull(await user.Use<IChannelRegistry>().GetByAliasOrIdAsync("old-alias", CancellationToken.None));

        // 重写：Alias = null → ChannelAlias 清空
        var updated = await svc.UpdateAsync("clear-alias-mp", new ChannelConfig
        {
            ChannelId = "clear-alias-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "s2",
            Alias = null, IsEnabled = true,
        }, CancellationToken.None);
        Assert.True(updated);

        // 旧 alias 解析 → null；按内部 ChannelId 仍命中（缺省 = ChannelId 对外）
        Assert.Null(await user.Use<IChannelRegistry>().GetByAliasOrIdAsync("old-alias", CancellationToken.None));
        var cfg = await user.Use<IChannelRegistry>().GetByAliasOrIdAsync("clear-alias-mp", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("clear-alias-mp", cfg!.ChannelId);
        Assert.Null(cfg.Alias);
        Assert.Equal("s2", cfg.AppSecret);   // 清空同时全量重写生效
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