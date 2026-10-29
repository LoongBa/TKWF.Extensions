using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Exceptions;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// D03（1.4 §6.3 O2）：<see cref="WeakIdentityCleanupService"/> 弱身份清理测试——全账号表 best-effort 扫描 +
/// 组合探测（无 Password/Sms 凭据 + 无档案 + 未冻结 + CreateTime 超 inactiveDays 阈值）；软删 CAS / 物理删级联 +
/// 强身份守卫；统计为身份形态基线（忽略 InactiveDays）。
/// <para>生产路径等价：真实门面注册进 StubDomainUser DI（SQLite 内存库），DataService 经 <c>User.Use&lt;具体类&gt;()</c>
/// NoAop 直建。每测试独立内存库（AuthenticationTestHost.CreateInMemoryFreeSql）。</para>
/// </summary>
public class WeakIdentityCleanupServiceTests
{
    private static (WeakIdentityCleanupService Service, StubDomainUser User) CreateService()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql, register: (services, user) =>
        {
            // 生产等价注册（AuthRelationModelTests 同款真实门面——因素/冻结三件套 + 账号写契约
            //（FreezeService.ACCOUNT_NOT_FOUND 守卫依赖 IAuthAccountService））
            services.AddSingleton<IAuthFactorService>(new AuthFactorService(
                user, Options.Create(new SecurityLoggingOptions()), NullLogger<AuthFactorService>.Instance));
            services.AddSingleton<IFreezeService>(new FreezeService(
                user, Options.Create(new SecurityLoggingOptions()), NullLogger<FreezeService>.Instance));
            services.AddSingleton<IFreezeStateProvider>(new FreezeStateProvider(user));
            services.AddSingleton<IAuthAccountService>(new AuthAccountService(
                user, Options.Create(new DomainOptions()),
                Options.Create(AuthenticationTestHost.CreateOptions()), new FakeCredentialProtector()));
            services.AddSingleton<IAuthAccountQueryService>(new AuthAccountQueryService(user));
        });
        var service = new WeakIdentityCleanupService(stub, NullLogger<WeakIdentityCleanupService>.Instance);
        return (service, stub);
    }

    /// <summary>建账号。<paramref name="createTime"/> 指定时经全字段更新回写——DataService.CreateAsync 业务方法
    /// 固定盖 CreateTime=UtcNow（生产语义），测试需旧时刻做天数阈值判据（测试基建路径）。</summary>
    private static async Task SeedAccountAsync(StubDomainUser user, string uid,
        bool enabled = true, DateTime? createTime = null)
    {
        var ds = user.Use<AuthAccountEntityDataService>();
        var account = new AuthAccountEntity { UId = uid, IsEnabled = enabled };
        await ds.CreateAsync(account);
        if (createTime.HasValue)
        {
            account.CreateTime = createTime.Value;
            await ds.EntityUpdateAsync(account);
        }
    }

    /// <summary>联邦因素绑定（弱身份专属凭据——不算 Password/Sms 强身份）。</summary>
    private static Task BindFederatedFactorAsync(StubDomainUser user, string uid, string channelPrefix = "wechat_mp")
        => user.Use<IAuthFactorService>().BindAsync(uid, AuthFactorTypes.Federated,
            $"{channelPrefix}|openid-{Guid.NewGuid():N}", source: AuthFactorSources.Provisioning);

    /// <summary>静态旧时刻（90 天前——InactiveDays=30 必命中阈值）。</summary>
    private static DateTime OldCreateTime() => DateTime.UtcNow.AddDays(-90);

    // ── QueryWeakIdentitiesAsync ──

    /// <summary>(a) 仅联邦因素 + 无档案 + 未冻结 + 超阈值（90d &gt; 30d）→ 列入（IsDisabled=false）。</summary>
    [Fact]
    public async Task Query_FederatedOnly_NoProfile_NotFrozen_Old_Listed()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-weak-a", createTime: OldCreateTime());
        await BindFederatedFactorAsync(user, "u-weak-a");

        var page = await service.QueryWeakIdentitiesAsync(
            new WeakIdentityCriteria(InactiveDays: 30), TestContext.Current.CancellationToken);

        Assert.Equal(1, page.Total);
        var item = Assert.Single(page.Items);
        Assert.Equal("u-weak-a", item.UId);
        Assert.False(item.IsDisabled);
        Assert.False(page.HasMore);
    }

    /// <summary>(b) 有密码凭据因素（FactorType=1）→ 强身份排除。</summary>
    [Fact]
    public async Task Query_WithPasswordFactor_Excluded()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-strong-pwd", createTime: OldCreateTime());
        await user.Use<IAuthFactorService>().BindAsync("u-strong-pwd", AuthFactorTypes.Password, "u-strong-pwd", secretHash: "hash");

        var page = await service.QueryWeakIdentitiesAsync(
            new WeakIdentityCriteria(InactiveDays: 30), TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.Total);
    }

    /// <summary>(c) 有档案行（UserProfile 1:1）→ 排除（判据 ②）。</summary>
    [Fact]
    public async Task Query_WithProfileRow_Excluded()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-profiled", createTime: OldCreateTime());
        await user.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(
            new UserProfileEntity { UId = "u-profiled", Nickname = "有档案" }, TestContext.Current.CancellationToken);

        var page = await service.QueryWeakIdentitiesAsync(
            new WeakIdentityCriteria(InactiveDays: 30), TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
    }

    /// <summary>(d) 冻结账号（活跃冻结行）→ 排除（判据 ③）。</summary>
    [Fact]
    public async Task Query_FrozenAccount_Excluded()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-frozen-weak", createTime: OldCreateTime());
        await user.Use<IFreezeService>().FreezeAsync("u-frozen-weak", "manual", null, null);

        var page = await service.QueryWeakIdentitiesAsync(
            new WeakIdentityCriteria(InactiveDays: 30), TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
    }

    /// <summary>(e) 新近账号（CreateTime=now &lt; 阈值 30d）→ 排除（判据 ④）。</summary>
    [Fact]
    public async Task Query_RecentAccount_Excluded()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-recent-weak");

        var page = await service.QueryWeakIdentitiesAsync(
            new WeakIdentityCriteria(InactiveDays: 30), TestContext.Current.CancellationToken);

        Assert.Empty(page.Items);
    }

    /// <summary>(f) 已软删账号（IsEnabled=false + 超阈值）→ 仍列入（恢复窗口对账），IsDisabled 标注 true。</summary>
    [Fact]
    public async Task Query_DisabledAccount_Listed_WithIsDisabledFlag()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-disabled-weak", enabled: false, createTime: OldCreateTime());
        await user.Use<IAuthFactorService>().BindAsync("u-disabled-weak", AuthFactorTypes.Federated, "wechat_mp|openid-disabled");

        var page = await service.QueryWeakIdentitiesAsync(
            new WeakIdentityCriteria(InactiveDays: 30), TestContext.Current.CancellationToken);

        var item = Assert.Single(page.Items);
        Assert.Equal("u-disabled-weak", item.UId);
        Assert.True(item.IsDisabled);
    }

    /// <summary>(g) 分页：PageIndex/PageSize 生效 + Total 全集 + HasMore 正确（1/2 → 2 行 HasMore；2/2 → 1 行无更多）。</summary>
    [Fact]
    public async Task Query_Paging_ReturnsPageTotalHasMore()
    {
        var (service, user) = CreateService();
        for (var i = 1; i <= 3; i++)
        {
            await SeedAccountAsync(user, $"u-weak-page-{i}", createTime: OldCreateTime());
            await user.Use<IAuthFactorService>().BindAsync($"u-weak-page-{i}", AuthFactorTypes.Federated, $"wechat_mp|openid-page-{i}");
        }

        var first = await service.QueryWeakIdentitiesAsync(
            new WeakIdentityCriteria(InactiveDays: 30, PageIndex: 1, PageSize: 2), TestContext.Current.CancellationToken);
        Assert.Equal(2, first.Items.Count);
        Assert.Equal(3, first.Total);
        Assert.True(first.HasMore);

        var second = await service.QueryWeakIdentitiesAsync(
            new WeakIdentityCriteria(InactiveDays: 30, PageIndex: 2, PageSize: 2), TestContext.Current.CancellationToken);
        Assert.Single(second.Items);
        Assert.Equal(3, second.Total);
        Assert.False(second.HasMore);
    }

    /// <summary>(h) InactiveDays &lt; 0 → DomainException INVALID_ARGUMENT（契约防线）。</summary>
    [Fact]
    public async Task Query_NegativeInactiveDays_ThrowsInvalidArgument()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-weak-neg");

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.QueryWeakIdentitiesAsync(
                new WeakIdentityCriteria(InactiveDays: -1), TestContext.Current.CancellationToken));

        Assert.Equal("INVALID_ARGUMENT", ex.ErrorCode);
    }

    // ── PurgeAsync ──

    /// <summary>(a) 软删：IsEnabled=false（CAS），返回 1。</summary>
    [Fact]
    public async Task Purge_SoftDelete_SetsDisabled_Returns1()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-purge-soft", createTime: OldCreateTime());
        await user.Use<IAuthFactorService>().BindAsync("u-purge-soft", AuthFactorTypes.Federated, "wechat_mp|openid-soft");

        var processed = await service.PurgeAsync(
            ["u-purge-soft"], PurgeMode.SoftDelete, TestContext.Current.CancellationToken);

        Assert.Equal(1, processed);
        var account = await user.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-purge-soft", TestContext.Current.CancellationToken);
        Assert.NotNull(account);
        Assert.False(account!.IsEnabled);
    }

    /// <summary>(b) 软删已禁用账号 → 跳过（0，不重复计数）。</summary>
    [Fact]
    public async Task Purge_SoftDelete_AlreadyDisabled_Returns0()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-purge-soft2", enabled: false, createTime: OldCreateTime());

        var processed = await service.PurgeAsync(
            ["u-purge-soft2"], PurgeMode.SoftDelete, TestContext.Current.CancellationToken);

        Assert.Equal(0, processed);
    }

    /// <summary>(c) 物理删弱账号（GDPR）：账号 + 因素 + 档案 + 冻结行全级联删除，返回 1。</summary>
    [Fact]
    public async Task Purge_PhysicalDelete_WeakAccount_CascadeDeletesAll_Returns1()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-purge-phys", createTime: OldCreateTime());
        await BindFederatedFactorAsync(user, "u-purge-phys");
        await user.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(
            new UserProfileEntity { UId = "u-purge-phys", Nickname = "待删" }, TestContext.Current.CancellationToken);
        await user.Use<IFreezeService>().FreezeAsync("u-purge-phys", "manual", null, null);

        var processed = await service.PurgeAsync(
            ["u-purge-phys"], PurgeMode.PhysicalDelete, TestContext.Current.CancellationToken);

        Assert.Equal(1, processed);
        Assert.Null(await user.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-purge-phys", TestContext.Current.CancellationToken));
        Assert.Empty(await user.Use<AuthFactorEntityDataService>()
            .EntitySelectAsync(f => f.UId == "u-purge-phys", ct: TestContext.Current.CancellationToken));
        Assert.Null(await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-purge-phys", TestContext.Current.CancellationToken));
        Assert.Empty(await user.Use<AuthAccountFreezeEntityDataService>()
            .EntitySelectAsync(f => f.UId == "u-purge-phys", ct: TestContext.Current.CancellationToken));
    }

    /// <summary>(d) 物理删强账号（有 Password 因素）→ 守卫跳过（0），账号与因素完好（fail-safe）。</summary>
    [Fact]
    public async Task Purge_PhysicalDelete_StrongAccount_Skipped_AccountIntact()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-strong-purge", createTime: OldCreateTime());
        await user.Use<IAuthFactorService>().BindAsync("u-strong-purge", AuthFactorTypes.Password, "u-strong-purge", secretHash: "hash");

        var processed = await service.PurgeAsync(
            ["u-strong-purge"], PurgeMode.PhysicalDelete, TestContext.Current.CancellationToken);

        Assert.Equal(0, processed);
        Assert.NotNull(await user.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-strong-purge", TestContext.Current.CancellationToken));
        Assert.Single(await user.Use<AuthFactorEntityDataService>()
            .EntitySelectAsync(f => f.UId == "u-strong-purge", ct: TestContext.Current.CancellationToken));
    }

    /// <summary>(e) 空列表幂等 → 0（不发任何删除）。</summary>
    [Fact]
    public async Task Purge_EmptyList_Returns0()
    {
        var (service, _) = CreateService();

        var processed = await service.PurgeAsync([], PurgeMode.PhysicalDelete, TestContext.Current.CancellationToken);

        Assert.Equal(0, processed);
    }

    /// <summary>(f) 未知 uid → 安全跳过（0，best-effort 不抛）。</summary>
    [Fact]
    public async Task Purge_UnknownUid_SkippedReturns0()
    {
        var (service, _) = CreateService();

        var processed = await service.PurgeAsync(
            ["u-ghost-1"], PurgeMode.PhysicalDelete, TestContext.Current.CancellationToken);

        Assert.Equal(0, processed);
    }

    // ── GetWeakStatsAsync（身份形态基线——忽略 InactiveDays）──

    /// <summary>(a) 混合账号：3 弱（含新近——证明忽略天数阈值） + 1 强 → WeakCount=3 / AccountTotal=4 / WeakRatio=0.75。</summary>
    [Fact]
    public async Task Stats_MixedAccounts_CountsAndRatio()
    {
        var (service, user) = CreateService();
        // 弱身份×3：含无因素（u-stat-w1）+ 联邦因素（u-stat-w2）+ 新近账号（u-stat-w3, CreateTime=now——
        // 基线与天数无关的实证——GetWeakStatsAsync 忽略 InactiveDays）
        await SeedAccountAsync(user, "u-stat-w1", createTime: OldCreateTime());
        await user.Use<IAuthFactorService>().BindAsync("u-stat-w1", AuthFactorTypes.Federated, "wechat_mp|openid-stat1");
        await SeedAccountAsync(user, "u-stat-w2");
        await user.Use<IAuthFactorService>().BindAsync("u-stat-w2", AuthFactorTypes.Federated, "wechat_mp|openid-stat2", source: AuthFactorSources.Provisioning);
        await SeedAccountAsync(user, "u-stat-w3");
        await user.Use<IAuthFactorService>().BindAsync("u-stat-w3", AuthFactorTypes.Federated, "wechat_mp|openid-stat3");
        // 强身份×1
        await SeedAccountAsync(user, "u-stat-s1");
        await user.Use<IAuthFactorService>().BindAsync("u-stat-s1", AuthFactorTypes.Password, "u-stat-s1", secretHash: "hash");

        var stats = await service.GetWeakStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(3, stats.WeakCount);
        Assert.Equal(4, stats.AccountTotal);
        Assert.Equal(0.75d, stats.WeakRatio, precision: 10);
    }

    /// <summary>(b) 全强账号 → WeakCount=0（Ratio 0）。</summary>
    [Fact]
    public async Task Stats_AllStrong_WeakCountZero()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-stat-strong-1");
        await user.Use<IAuthFactorService>().BindAsync("u-stat-strong-1", AuthFactorTypes.Password, "u-stat-strong-1", secretHash: "hash");
        await SeedAccountAsync(user, "u-stat-strong-2");
        await user.Use<IAuthFactorService>().BindAsync("u-stat-strong-2", AuthFactorTypes.Sms, "13800001234");

        var stats = await service.GetWeakStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, stats.WeakCount);
        Assert.Equal(2, stats.AccountTotal);
        Assert.Equal(0d, stats.WeakRatio);
    }

    /// <summary>(c) 空库 → AccountTotal=0 / WeakRatio=0（防除零）。</summary>
    [Fact]
    public async Task Stats_Empty_AccountTotalZero_RatioZero()
    {
        var (service, _) = CreateService();

        var stats = await service.GetWeakStatsAsync(TestContext.Current.CancellationToken);

        Assert.Equal(0, stats.WeakCount);
        Assert.Equal(0, stats.AccountTotal);
        Assert.Equal(0d, stats.WeakRatio);
    }

    /// <summary>
    /// 审核 P1-2 回归：非 Federated 因素（Email=3）即为强身份——不得判弱、不得被清理（此前黑名单只认 Password/Sms）。
    /// </summary>
    [Fact]
    public async Task Purge_EmailFactorAccount_Skipped_AndNotListed()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-email-1", createTime: OldCreateTime());
        await user.Use<IAuthFactorService>().BindAsync("u-email-1", AuthFactorTypes.Email, "a@b.c");

        var page = await service.QueryWeakIdentitiesAsync(
            new WeakIdentityCriteria(InactiveDays: 30), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(page.Items, i => i.UId == "u-email-1");

        var purged = await service.PurgeAsync(["u-email-1"], PurgeMode.PhysicalDelete, TestContext.Current.CancellationToken);
        Assert.Equal(0, purged);
        Assert.NotNull(await user.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-email-1", TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// 审核 P1-3 回归：零因素账号（含消费方影子管理员）无联邦建号证据——不得判弱、不得被清理。
    /// </summary>
    [Fact]
    public async Task Purge_ZeroFactorAccount_Skipped_AndNotListed()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-nofactor-1", createTime: OldCreateTime());   // 无任何因素

        var page = await service.QueryWeakIdentitiesAsync(
            new WeakIdentityCriteria(InactiveDays: 30), TestContext.Current.CancellationToken);
        Assert.DoesNotContain(page.Items, i => i.UId == "u-nofactor-1");

        var purged = await service.PurgeAsync(["u-nofactor-1"], PurgeMode.SoftDelete, TestContext.Current.CancellationToken);
        Assert.Equal(0, purged);
        var account = await user.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-nofactor-1", TestContext.Current.CancellationToken);
        Assert.True(account!.IsEnabled);
    }

    /// <summary>审核 P2-2 回归：非法 PurgeMode 显式拒绝（不得落入物理删除分支）。</summary>
    [Fact]
    public async Task Purge_InvalidMode_ThrowsInvalidArgument()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-badmode-1", createTime: OldCreateTime());
        await user.Use<IAuthFactorService>().BindAsync("u-badmode-1", AuthFactorTypes.Federated, "wechat_mp|openid-bm");

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.PurgeAsync(
            ["u-badmode-1"], (PurgeMode)99, TestContext.Current.CancellationToken));
        Assert.Equal("INVALID_ARGUMENT", ex.ErrorCode);
        Assert.NotNull(await user.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-badmode-1", TestContext.Current.CancellationToken));
    }
}