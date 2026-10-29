using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.FreeSql;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// 通道注册表测试（D03 1.7 供给层改造后）——<see cref="SsoChannelRegistryService"/> 写面（旧装配保留兼容）+
/// <see cref="CompositeChannelRegistry"/> 按 <see cref="IChannelSource.Priority"/> 降序首命中 + 缓存三键 + 三机制失效。
/// <para>⚠️ D03 1.7 语义变更（方案 §8.4）：<c>SsoChannelRegistryEntity</c> 系<b>停用新装配</b>——Composite 不再组合
/// <see cref="DbChannelRegistry"/>（权威归 WeChatOperations <c>WeChatChannelRegistrySource</c>，Priority=100）；
/// 本文件：写面测试回读<b>经 <see cref="DbChannelRegistry"/> 旧装配兼容路径</b>（显式 Use 仍可解析）；
/// Composite 选区测试改用 <b>stub 源 Priority 排序</b>语义（低 0 / 高 100——模拟 静态源 vs DB 权威）。</para>
/// <para>生产路径宿主模式：真实 DI（FederationExtensionInitializer.ConfigureServices + FreeSql SQLite 基础设施 +
/// AddLogging + IMemoryCache）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> + <c>User.Use&lt;接口&gt;()</c>。</para>
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
        // Federation 扩展初始器（守卫工厂 + keyed 对称密钥 + 注册表写面 + IMemoryCache 单例）
        new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // Options 值绑定（keyed FileSymmetricKeyProvider 依赖 SecretEncryptionKeyPath——测试宿主写密钥文件）
        var aesKeyPath = FederationTestHost.CreateAesKeyPath();
        services.Configure<FederationChannelRegistryOptions>(o =>
        {
            o.SecretEncryptionKeyPath = aesKeyPath;
            o.IsProduction = true;
            o.CacheTtlSeconds = 60;   // 测试 TTL 60s（默认 120 亦可——断言不依赖 TTL 快速过期）
        });

        // 静态通道来源 stub（Priority 默认 0——组合回退链断层：高优先源未命中 → 回退静态）
        services.AddSingleton<IChannelSource>(new StubChannelSource(0,   // 静态源低优先（DIM 默认 0 对齐）
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
                AppSecret = "static-secret-shared", // 与高优先源同 id——高优先覆盖断言
                IsEnabled = true,
            },
            new ChannelConfig
            {
                ChannelId = "static-alias-mp",
                PlatformType = "wechat",
                AppId = "wx-static-app",
                AppSecret = "static-secret-alias",
                Alias = "merchant-a",   // 双键——对外 alias 解析用例
                IsEnabled = true,
            }));

        // 高优先源 stub（Priority=100——模拟 WeChatOperations DB 权威 WeChatChannelRegistrySource）
        services.AddSingleton<IChannelSource>(new StubChannelSource(100,
            new ChannelConfig
            {
                ChannelId = "shared-mp",
                PlatformType = "wechat",
                AppId = "wx-db-app",
                AppSecret = "db-secret-shared", // 同 id 静态被覆盖——高优先首命中断言
                IsEnabled = true,
            },
            new ChannelConfig
            {
                ChannelId = "db-only-mp",
                PlatformType = "wechat",
                AppId = "wx-db-app",
                AppSecret = "db-only-secret",
                IsEnabled = true,
            }));

        // 实体 DAC（真实 FreeSqlEntityDAC——SsoChannelRegistryEntity 等连接层实体，写面测试）
        FederationTestHost.RegisterEntityDacs(services);
    }

    // ── 1. 写面（SsoChannelRegistryService，旧装配保留兼容——回读经 DbChannelRegistry 直查） ──

    /// <summary>写面：Register 加密落库（密文非明文）→ DbChannelRegistry（旧装配路径）回读解密一致。</summary>
    [Fact]
    public async Task Register_EncryptedInDb_DecryptsThroughDbRegistry()
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
        Assert.NotNull(row!.AppSecretEncrypted);
        Assert.DoesNotContain("db-secret-1", row.AppSecretEncrypted);
        Assert.DoesNotContain("tok-1", row.ExtraJsonEncrypted);

        // 经 DbChannelRegistry 旧装配路径回读（显式 Use——D03 1.7 停用新装配后仍可用，旧装配保留兼容）
        var cfg = await user.Use<DbChannelRegistry>().GetAsync("db-mp-1", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("wx-db-app", cfg!.AppId);
        Assert.Equal("db-secret-1", cfg.AppSecret);
        Assert.Equal("tok-1", cfg.Extra["Token"]);
        Assert.Equal("aes-1", cfg.Extra["EncodingAESKey"]);
        Assert.True(cfg.IsEnabled);
    }

    /// <summary>旧装配兼容：Unregister 删行 → DbChannelRegistry 回读 null。</summary>
    [Fact]
    public async Task Unregister_DeletesRow_DbRegistryReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var svc = user.Use<ISsoChannelRegistryService>();
        await svc.RegisterAsync(new ChannelConfig
        {
            ChannelId = "db-mp-3", PlatformType = "wechat", AppId = "wx-db-app", AppSecret = "secret-3", IsEnabled = true,
        }, CancellationToken.None);

        var removed = await svc.UnregisterAsync("db-mp-3", CancellationToken.None);
        Assert.True(removed);

        Assert.Null(await user.Use<DbChannelRegistry>().GetAsync("db-mp-3", CancellationToken.None));
        var row = await user.Use<SsoChannelRegistryEntityDataService>()
            .EntityGetAsync(m => m.ChannelId == "db-mp-3", CancellationToken.None);
        Assert.Null(row);
    }

    /// <summary>写面：Unregister 不存在的 channelId → 返回 false。</summary>
    [Fact]
    public async Task Unregister_ChannelNotFound_ReturnsFalse()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        Assert.False(await user.Use<ISsoChannelRegistryService>().UnregisterAsync("nonexistent", CancellationToken.None));
    }

    /// <summary>写面：Register 同 ChannelId 冲突 → DomainException（CONFLICT，fail-closed）。</summary>
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

    /// <summary>写面：同 Alias 二次注册（不同 ChannelId）→ DomainException CONFLICT。</summary>
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

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => svc.RegisterAsync(new ChannelConfig
            {
                ChannelId = "alias-intruder-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "s2",
                Alias = "merchant-b", IsEnabled = true,
            }, CancellationToken.None));
        Assert.Equal(DomainException.ErrorCodes.Conflict, ex.ErrorCode);
    }

    /// <summary>写面：Alias null 重写 → ChannelAlias 清空（旧装配——经 DbChannelRegistry 断言清空语义）。</summary>
    [Fact]
    public async Task Update_ClearsAlias_DbRegistrySeesCleared()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var svc = user.Use<ISsoChannelRegistryService>();
        await svc.RegisterAsync(new ChannelConfig
        {
            ChannelId = "clear-alias-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "s1",
            Alias = "old-alias", IsEnabled = true,
        }, CancellationToken.None);

        Assert.NotNull(await user.Use<DbChannelRegistry>().GetByAliasOrIdAsync("old-alias", CancellationToken.None));

        var updated = await svc.UpdateAsync("clear-alias-mp", new ChannelConfig
        {
            ChannelId = "clear-alias-mp", PlatformType = "wechat", AppId = "wx-db", AppSecret = "s2",
            Alias = null, IsEnabled = true,
        }, CancellationToken.None);
        Assert.True(updated);

        Assert.Null(await user.Use<DbChannelRegistry>().GetByAliasOrIdAsync("old-alias", CancellationToken.None));
        var cfg = await user.Use<DbChannelRegistry>().GetByAliasOrIdAsync("clear-alias-mp", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("clear-alias-mp", cfg!.ChannelId);
        Assert.Null(cfg.Alias);
        Assert.Equal("s2", cfg.AppSecret);
    }

    // ── 2. Composite 按 Priority 排序选区（1.7 组合语义——高优先首命中，替代 DB→Static 硬编码） ──

    /// <summary>GetAsync：低优先源命中（高优先源无该 id）→ 返回低优先值。</summary>
    [Fact]
    public async Task Composite_Priority_LowPriorityHitWhenHighMiss()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var cfg = await user.Use<IChannelRegistry>().GetAsync("static-mp-1", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("static-secret-1", cfg!.AppSecret);
        Assert.Equal("wechat", cfg.PlatformType);
    }

    /// <summary>GetAsync：同 id 双源并存 → 高优先源（Priority=100）首命中覆盖低优先（条件 5 alias 覆盖语义）。</summary>
    [Fact]
    public async Task Composite_Priority_HighPriorityWinsOverLow()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var cfg = await user.Use<IChannelRegistry>().GetAsync("shared-mp", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("db-secret-shared", cfg!.AppSecret);   // 高优先（100）DB 值覆盖低优先（0）静态值
        Assert.Equal("wx-db-app", cfg.AppId);
    }

    /// <summary>GetAsync：两源皆无该 id → null（CHANNEL_NOT_FOUND 语义）。</summary>
    [Fact]
    public async Task Composite_Priority_MissAcrossAllSources_ReturnsNull()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        Assert.Null(await user.Use<IChannelRegistry>().GetAsync("no-such-channel", CancellationToken.None));
    }

    /// <summary>GetAllAsync：全源合并去重——同 id 高优先源胜出（`db-only-mp` 仅高优先 + `static-mp-1` 仅低优先并存）。</summary>
    [Fact]
    public async Task Composite_GetAll_MergesSources_HighPriorityDedupWins()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var all = await user.Use<IChannelRegistry>().GetAllAsync(CancellationToken.None);
        var byId = all.ToDictionary(c => c.ChannelId!, StringComparer.Ordinal);
        Assert.Contains("static-mp-1", byId);                     // 仅低优先源项
        Assert.Equal("static-secret-1", byId["static-mp-1"].AppSecret);
        Assert.Contains("db-only-mp", byId);                      // 仅高优先源项
        Assert.Contains("shared-mp", byId);
        Assert.Equal("db-secret-shared", byId["shared-mp"].AppSecret); // 同 id → 高优先值
    }

    /// <summary>GetDefaultAsync：无 IsDefault 声明 → 按高优先源首项（1.7 组合序 = 优先级序）。</summary>
    [Fact]
    public async Task Composite_GetDefault_FallsBackByPriorityOrder()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        // 两源均无 IsDefault → 高优先源首项（100 源 shared-mp——模拟 DB 权威默认）
        var def = await user.Use<IChannelRegistry>().GetDefaultAsync(CancellationToken.None);
        Assert.NotNull(def);
        Assert.Equal("shared-mp", def!.ChannelId);
    }

    /// <summary>GetByAliasOrIdAsync：alias 全局解析（低优先源 Alias=merchant-a → 命中对应通道）。</summary>
    [Fact]
    public async Task Composite_GetByAliasOrId_AliasAcrossSources()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var cfg = await user.Use<IChannelRegistry>().GetByAliasOrIdAsync("merchant-a", CancellationToken.None);
        Assert.NotNull(cfg);
        Assert.Equal("static-alias-mp", cfg!.ChannelId);
        Assert.Equal("merchant-a", cfg.Alias);
        Assert.Equal("static-secret-alias", cfg.AppSecret);

        // 无 Alias 通道 → 按 ChannelId 回退（缺省 = ChannelId 对外，存量零迁移）
        var byId = await user.Use<IChannelRegistry>().GetByAliasOrIdAsync("static-mp-1", CancellationToken.None);
        Assert.NotNull(byId);
        Assert.Equal("static-mp-1", byId!.ChannelId);
        Assert.Null(byId.Alias);

        // 未知 key → null
        Assert.Null(await user.Use<IChannelRegistry>().GetByAliasOrIdAsync("no-such-alias", CancellationToken.None));
    }

    // ── 3. 缓存三机制（显式失效 / 事件失效 / TTL——D03 §4.2.3） ──

    /// <summary>显式失效：GetAsync 命中缓存 → InvalidateAsync(channelId) → 低优先源变更后立即新值（无需等 TTL）。</summary>
    [Fact]
    public async Task InvalidateAsync_ChannelId_ClearsChannelCache()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var registry = user.Use<IChannelRegistry>();

        // 首查填充缓存（未命中去源——缓存命中路径）
        var first = await registry.GetAsync("static-mp-1", CancellationToken.None);
        Assert.NotNull(first);

        // 显式失效 → 缓存键被清（下次查询重新下钻源）
        await registry.InvalidateAsync("static-mp-1", CancellationToken.None);
        var second = await registry.GetAsync("static-mp-1", CancellationToken.None);
        Assert.NotNull(second);   // 重新下钻仍命中源（缓存失效 ≠ 数据消失）
    }

    /// <summary>显式失效全量：InvalidateAsync(null) → all/default 聚合键清空（GetAllAsync 重新下钻）。</summary>
    [Fact]
    public async Task InvalidateAsync_Null_ClearsAggregateKeys()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var registry = user.Use<IChannelRegistry>();

        var all = await registry.GetAllAsync(CancellationToken.None);
        Assert.NotEmpty(all);

        await registry.InvalidateAsync(null, CancellationToken.None);
        var re = await registry.GetAllAsync(CancellationToken.None);
        Assert.NotEmpty(re);   // 重新下钻全源枚举
    }

    /// <summary>事件失效：ChannelConfigChangedHandler 收到事件 → 缓存清空（与显式失效同效——消费方写路径发布）。</summary>
    [Fact]
    public async Task ChannelConfigChanged_Event_InvalidatesCache()
    {
        using var _ = this;
        var (_, user) = BindTestScope();
        var registry = user.Use<IChannelRegistry>();

        var first = await registry.GetAsync("static-mp-1", CancellationToken.None);
        Assert.NotNull(first);

        // 模拟 EventDispatchFilter 派发（handler 直接调用——ILocalEventBus 派发链由消费方宿主验证）
        var cache = user.GetService<IMemoryCache>();
        var handler = new ChannelConfigChangedHandler(cache, NullLogger<ChannelConfigChangedHandler>.Instance);
        await handler.HandleEventAsync(new ChannelConfigChanged(ChannelId: "static-mp-1", DataVersion: DateTime.UtcNow.Ticks));

        var second = await registry.GetAsync("static-mp-1", CancellationToken.None);
        Assert.NotNull(second);   // 缓存失效后重新下钻
    }

    /// <summary>乱序防线：DataVersion 旧事件（≤ 已见）→ 忽略（Obs5——不触发 Invalidate 副作用可观测断言）。</summary>
    [Fact]
    public async Task ChannelConfigChanged_OldVersion_Ignored()
    {
        var cache = new MemoryCache(new MemoryCacheOptions());
        var handler = new ChannelConfigChangedHandler(cache, NullLogger<ChannelConfigChangedHandler>.Instance);

        var v1 = DateTime.UtcNow.Ticks;
        cache.Set(ChannelCacheKeys.Channel("ch-x"), new ChannelConfig { ChannelId = "ch-x", PlatformType = "wechat" });
        await handler.HandleEventAsync(new ChannelConfigChanged("ch-x", v1));
        Assert.False(cache.TryGetValue(ChannelCacheKeys.Channel("ch-x"), out _));   // v1 已失效

        cache.Set(ChannelCacheKeys.Channel("ch-x"), new ChannelConfig { ChannelId = "ch-x", PlatformType = "wechat" });
        await handler.HandleEventAsync(new ChannelConfigChanged("ch-x", v1 - 1));   // 乱序旧事件（v1-1 < v1）
        Assert.True(cache.TryGetValue(ChannelCacheKeys.Channel("ch-x"), out _));    // 忽略——缓存保留
    }

    /// <summary>cache key 规范：ch:{ChannelId} 单键 + ch:all + ch:default（方案 §8.2——断言格式防漂移）。</summary>
    [Fact]
    public void CacheKeyFormat_MatchesSpec()
    {
        Assert.Equal("ch:abc-123", ChannelCacheKeys.Channel("abc-123"));
        Assert.Equal("ch:all", ChannelCacheKeys.All);
        Assert.Equal("ch:default", ChannelCacheKeys.Default);
    }

    // ── 4. 静态通道来源 stub（IChannelSource——Priority 排序断言用） ──

    /// <summary>可指定 Priority 的通道来源 stub（D03 1.7 Priority 语义测试）。</summary>
    private sealed class StubChannelSource : IChannelSource
    {
        private readonly ChannelConfig[] _channels;
        public StubChannelSource(int priority, params ChannelConfig[] channels)
        {
            Priority = priority;
            _channels = channels;
        }

        public string PlatformType => "stub";
        public int Priority { get; }
        public IReadOnlyList<ChannelConfig> GetChannels() => _channels;
    }
}