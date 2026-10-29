using System;
using System.Linq;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain.Exceptions;
using TKWF.Ext.Federation;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// D03（1.4 §6.1/§6.2）：<see cref="ExternalIdentityProvisioningService"/> 事件路径 L2 预置 + 档案合并测试——
/// 与登录路径 <see cref="FederatedLoginOrchestrator"/> 同内核（真实 <see cref="PlatformAccountMapService"/> 映射 +
/// AuthFactor 唯一约束兜底），生产路径等价：真实门面注册进 StubDomainUser DI，DataService 经
/// <c>User.Use&lt;具体类&gt;()</c> NoAop 直建（SQLite 内存库）。
/// <para>ProvisionAsync：UId 直读复用 / 映射复用 / AutoProvision 门控建号 + 因素行 / 并发冲突回滚孤儿重读胜者。</para>
/// <para>MergeProfileAsync：优先级合并（补空/同源刷新/user_filled 保护/默认昵称保护/来源戳）——D03 §5.2。</para>
/// </summary>
public class ExternalIdentityProvisioningServiceTests
{
    private const string ChannelId = "wechat_mp";
    private const string ExternalUserId = "openid-prov-1";
    private const string ChannelIdentifier = ChannelId + "|" + ExternalUserId;

    private static (ExternalIdentityProvisioningService Service, StubDomainUser User) CreateService(
        AuthCenterOptions? options = null, Action<IServiceCollection, StubDomainUser>? register = null)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql, register: (services, user) =>
        {
            // 生产等价注册（AuthRelationModelTests/PlatformAccountMapServiceTests 同款真实门面）
            services.AddSingleton<IAuthFactorService>(new AuthFactorService(
                user, Options.Create(new SecurityLoggingOptions()), NullLogger<AuthFactorService>.Instance));
            services.AddSingleton<IFreezeService>(new FreezeService(
                user, Options.Create(new SecurityLoggingOptions()), NullLogger<FreezeService>.Instance));
            services.AddSingleton<IFreezeStateProvider>(new FreezeStateProvider(user));
            services.AddSingleton<IAuthAccountService>(new AuthAccountService(
                user, Options.Create(new DomainOptions()),
                Options.Create(AuthenticationTestHost.CreateOptions()), new FakeCredentialProtector()));
            services.AddSingleton<IAuthAccountQueryService>(new AuthAccountQueryService(user));
            services.AddSingleton<IExternalIdentityService>(new PlatformAccountMapService(
                user, NullLogger<PlatformAccountMapService>.Instance));
            register?.Invoke(services, user);
        });

        var service = new ExternalIdentityProvisioningService(
            stub, NullLogger<ExternalIdentityProvisioningService>.Instance,
            Options.Create(options ?? new AuthCenterOptions()));
        return (service, stub);
    }

    private static FederatedTicketPayload Payload(string? platformUid = null)
        => new(Provider: "wechat", ChannelType: "mp_subscription", ChannelId: ChannelId,
            ChannelAlias: null, ExternalUserId: ExternalUserId, UnionId: null,
            PlatformUid: platformUid, Assurance: "event", Iat: 0, Exp: 0, Jti: "jti-prov-1");

    private static Task SeedAccountAsync(StubDomainUser user, string uid, DateTime? createTime = null)
        => user.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity
        {
            UId = uid,
            CreateTime = createTime ?? DateTime.UtcNow
        });

    // ── ProvisionAsync：账号回读/映射/建号 fallback（与登录编排同内核）──

    /// <summary>(a) UId 直读命中：payload.PlatformUid 非空 + 账号存在 → 复用（IsNew=false）——不查映射（无因素行副作用）。</summary>
    [Fact]
    public async Task Provision_UidDirectHit_Reuses_NoMappingSideEffect()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-direct-1");

        var result = await service.ProvisionAsync(Payload(platformUid: "u-direct-1"), TestContext.Current.CancellationToken);

        Assert.Equal("u-direct-1", result.PlatformUid);
        Assert.False(result.IsNew);
        // 直读命中不触碰映射面：通道因素行零写入
        var factors = await user.Use<AuthFactorEntityDataService>()
            .EntitySelectAsync(f => f.Identifier == ChannelIdentifier, ct: TestContext.Current.CancellationToken);
        Assert.Empty(factors);
    }

    /// <summary>(a2) UId 非空但直读未命中 → fallthrough 到映射复用（IsNew=false）。</summary>
    [Fact]
    public async Task Provision_UidDirectMiss_FallsBackToMapHit_Reuses()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-mapped-1");
        await user.Use<IExternalIdentityService>().LinkAsync("u-mapped-1", ChannelId, ExternalUserId, ct: TestContext.Current.CancellationToken);

        var result = await service.ProvisionAsync(Payload(platformUid: "u-nonexist"), TestContext.Current.CancellationToken);

        Assert.Equal("u-mapped-1", result.PlatformUid);
        Assert.False(result.IsNew);
    }

    /// <summary>(b) UId 空 + 映射命中 → 复用（IsNew=false，不建号）。</summary>
    [Fact]
    public async Task Provision_NoPlatformUid_MapHit_Reuses()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-mapped-2");
        await user.Use<IExternalIdentityService>().LinkAsync("u-mapped-2", ChannelId, ExternalUserId, ct: TestContext.Current.CancellationToken);
        var before = await user.Use<AuthAccountEntityDataService>()
            .EntitySelectAsync(predicate: null, ct: TestContext.Current.CancellationToken);

        var result = await service.ProvisionAsync(Payload(platformUid: null), TestContext.Current.CancellationToken);

        Assert.Equal("u-mapped-2", result.PlatformUid);
        Assert.False(result.IsNew);
        // 不建号——账号总数不变
        var after = await user.Use<AuthAccountEntityDataService>()
            .EntitySelectAsync(predicate: null, ct: TestContext.Current.CancellationToken);
        Assert.Equal(before.Count, after.Count);
    }

    /// <summary>(c) 无映射 + AutoProvision=true → 建号 + 联邦因素 Link（IsNew=true；因素行 FactorType=4 + Identifier 规范）。</summary>
    [Fact]
    public async Task Provision_NoMapping_AutoProvision_CreatesAccount_AndLinksFactor()
    {
        var (service, user) = CreateService();

        var result = await service.ProvisionAsync(Payload(platformUid: null), TestContext.Current.CancellationToken);

        Assert.True(result.IsNew);
        Assert.False(string.IsNullOrEmpty(result.PlatformUid));
        var account = await user.Use<AuthAccountEntityDataService>().GetByUIdAsync(result.PlatformUid, TestContext.Current.CancellationToken);
        Assert.NotNull(account);
        Assert.True(account!.IsEnabled);
        // 联邦因素行：FactorType=4（Federated）+ Identifier = {channelId}|{externalUserId}
        var factors = await user.Use<AuthFactorEntityDataService>()
            .EntitySelectAsync(f => f.UId == result.PlatformUid, ct: TestContext.Current.CancellationToken);
        var factor = Assert.Single(factors);
        Assert.Equal((int)AuthFactorTypes.Federated, factor.FactorType);
        Assert.Equal(ChannelIdentifier, factor.Identifier);
    }

    /// <summary>(d) 无映射 + AutoProvision=false → DomainException ACCOUNT_NOT_PROVISIONED（不建号不绑定）。</summary>
    [Fact]
    public async Task Provision_NoMapping_AutoProvisionDisabled_ThrowsNotProvisioned()
    {
        var options = new AuthCenterOptions();
        options.FederatedProvisioning.AutoProvision = false;
        var (service, user) = CreateService(options: options);

        var ex = await Assert.ThrowsAsync<DomainException>(
            () => service.ProvisionAsync(Payload(platformUid: null), TestContext.Current.CancellationToken));

        Assert.Equal("ACCOUNT_NOT_PROVISIONED", ex.ErrorCode);
        Assert.Empty(await user.Use<AuthAccountEntityDataService>()
            .EntitySelectAsync(predicate: null, ct: TestContext.Current.CancellationToken));
        Assert.Empty(await user.Use<AuthFactorEntityDataService>()
            .EntitySelectAsync(predicate: null, ct: TestContext.Current.CancellationToken));
    }

    /// <summary>(e) 并发冲突模拟（同登录编排内核）：LinkAsync 抛 FACTOR_ALREADY_BOUND → 回滚空军账号 → 重读胜者复用
    /// （IsNew=false；库中仅剩胜者账号——孤儿不留行）。</summary>
    [Fact]
    public async Task Provision_ConcurrentConflict_RollsBackOrphan_ReusesWinner()
    {
        // 胜者账号预先存在（模拟并发首登先完成方）；映射 Fake 在 LinkAsync 冲突后暴露胜者映射
        const string winnerUid = "u-winner-1";
        var map = new ConflictingMapFake(winnerUid);
        var (service, user) = CreateService(register: (services, _) =>
            services.AddSingleton<IExternalIdentityService>(map));
        await SeedAccountAsync(user, winnerUid);

        var result = await service.ProvisionAsync(Payload(platformUid: null), TestContext.Current.CancellationToken);

        Assert.Equal(1, map.LinkCalls);                       // 仅一次 Link（冲突失败不重试 Link）
        Assert.True(map.ConflictThrown);
        Assert.Equal(winnerUid, result.PlatformUid);
        Assert.False(result.IsNew);
        // 孤儿账号已回滚：库中仅胜者一行
        var accounts = await user.Use<AuthAccountEntityDataService>()
            .EntitySelectAsync(predicate: null, ct: TestContext.Current.CancellationToken);
        var account = Assert.Single(accounts);
        Assert.Equal(winnerUid, account.UId);
    }

    // ── MergeProfileAsync：档案优先级合并（D03 §5.2）──

    /// <summary>(f) 新档案 → 字段写入 + ProfileSource/LastProfileSourceAt 落戳（创建路径全量持久化）。</summary>
    [Fact]
    public async Task MergeProfile_NewProfile_WritesFieldsAndSourceStamp()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-profile-new");

        await service.MergeProfileAsync("u-profile-new",
            new ExternalProfile("昵称", "https://cdn/x.png", "male", null, null, null, null, null, null, null),
            ProfileSource.Event, TestContext.Current.CancellationToken);

        var profile = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-new", TestContext.Current.CancellationToken);
        Assert.NotNull(profile);
        Assert.Equal("昵称", profile!.Nickname);
        Assert.Equal("https://cdn/x.png", profile.Avatar);
        Assert.Equal("male", profile.Gender);
        Assert.Equal(ProfileSource.Event, profile.ProfileSource);
        Assert.NotNull(profile.LastProfileSourceAt);
    }

    /// <summary>(g) 既有档案 + 高优先级源（oauth_userinfo &gt; event）→ 仅补空字段：昵称既有不被覆盖，头像 null 被填。</summary>
    [Fact]
    public async Task MergeProfile_Existing_HigherPriority_FillsNullFieldsOnly()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-profile-null");
        await user.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-profile-null",
            Nickname = "既有昵称",
            ProfileSource = ProfileSource.Event,
            LastProfileSourceAt = DateTime.UtcNow.AddDays(-1)
        }, TestContext.Current.CancellationToken);

        await service.MergeProfileAsync("u-profile-null",
            new ExternalProfile("oauth昵称", "https://cdn/y.png", null, null, null, null, null, null, null, null),
            ProfileSource.OAuthUserInfo, TestContext.Current.CancellationToken);

        var profile = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-null", TestContext.Current.CancellationToken);
        Assert.Equal("既有昵称", profile!.Nickname);          // 非空 + 非默认 → 不覆盖
        Assert.Equal("https://cdn/y.png", profile.Avatar);    // null 字段补填
        Assert.Equal(ProfileSource.OAuthUserInfo, profile.ProfileSource);   // 有字段写入 → 落戳更新
        Assert.NotNull(profile.LastProfileSourceAt);
    }

    /// <summary>(h) user_filled 绝对保护：oauth_userinfo 不覆盖用户自填值（非空 + 高优先级仍拒）。</summary>
    [Fact]
    public async Task MergeProfile_UserFilled_NotOverwrittenByOAuth()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-profile-uf");
        await user.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-profile-uf",
            Nickname = "用户自填",
            ProfileSource = ProfileSource.UserFilled,
            LastProfileSourceAt = DateTime.UtcNow.AddDays(-2)
        }, TestContext.Current.CancellationToken);

        await service.MergeProfileAsync("u-profile-uf",
            new ExternalProfile("oauth值", null, null, null, null, null, null, null, null, null),
            ProfileSource.OAuthUserInfo, TestContext.Current.CancellationToken);

        var profile = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-uf", TestContext.Current.CancellationToken);
        Assert.Equal("用户自填", profile!.Nickname);
        Assert.Equal(ProfileSource.UserFilled, profile.ProfileSource);   // 无字段写入 → 来源戳不动
        var stamp = profile.LastProfileSourceAt;
        Assert.NotNull(stamp);
    }

    /// <summary>(i) 同源刷新以新为准：event 源昵称变化 → 覆盖既有 event 值（写 + 来源戳刷新）。</summary>
    [Fact]
    public async Task MergeProfile_SameSourceRefresh_OverwritesChangedValue()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-profile-same");
        await user.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-profile-same",
            Nickname = "旧昵称",
            ProfileSource = ProfileSource.Event,
            LastProfileSourceAt = DateTime.UtcNow.AddDays(-1)
        }, TestContext.Current.CancellationToken);

        await service.MergeProfileAsync("u-profile-same",
            new ExternalProfile("新昵称", null, null, null, null, null, null, null, null, null),
            ProfileSource.Event, TestContext.Current.CancellationToken);

        var profile = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-same", TestContext.Current.CancellationToken);
        Assert.Equal("新昵称", profile!.Nickname);
        Assert.Equal(ProfileSource.Event, profile.ProfileSource);
    }

    /// <summary>(j) "微信用户"默认昵称：低优先级（oauth_userinfo → event 下探）不覆盖；仅高→低升级路径可写。</summary>
    [Fact]
    public async Task MergeProfile_DefaultWechatNickname_NotOverwritten_ExceptUpgrade()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-profile-wx");
        await user.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-profile-wx",
            Nickname = "微信用户",
            ProfileSource = ProfileSource.OAuthUserInfo,
            LastProfileSourceAt = DateTime.UtcNow.AddDays(-1)
        }, TestContext.Current.CancellationToken);

        // 低优先级（event=1 &lt; oauth_userinfo=2）事件昵称 → 默认昵称不被覆盖，来源戳不动
        await service.MergeProfileAsync("u-profile-wx",
            new ExternalProfile("事件昵称", null, null, null, null, null, null, null, null, null),
            ProfileSource.Event, TestContext.Current.CancellationToken);
        var afterEvent = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-wx", TestContext.Current.CancellationToken);
        Assert.Equal("微信用户", afterEvent!.Nickname);
        var stampBefore = afterEvent.LastProfileSourceAt;
        Assert.Equal(ProfileSource.OAuthUserInfo, afterEvent.ProfileSource);

        // 高优先级 oauth_userinfo（同源刷新语义——oauth 默认值被 oauth 新值刷新）：默认昵称让位真实值
        await service.MergeProfileAsync("u-profile-wx",
            new ExternalProfile("微信真实昵称", null, null, null, null, null, null, null, null, null),
            ProfileSource.OAuthUserInfo, TestContext.Current.CancellationToken);
        var afterOAuth = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-wx", TestContext.Current.CancellationToken);
        Assert.Equal("微信真实昵称", afterOAuth!.Nickname);
    }

    /// <summary>(k) 无字段写入（新值为 null / 与既有相同）→ 来源戳保持不动（不刷新 ProfileSource/LastProfileSourceAt）。
    /// <para>SQLite 往返带 Kind/时区偏移——对账用「合并前后戳等值」比较，不做历法日期断言。</para></summary>
    [Fact]
    public async Task MergeProfile_NoFieldWritten_KeepsExistingSourceStamp()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-profile-stamp");
        await user.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-profile-stamp",
            Nickname = "保持昵称",
            Avatar = "https://cdn/keep.png",
            ProfileSource = ProfileSource.OAuthUserInfo,
            LastProfileSourceAt = DateTime.UtcNow.AddDays(-3)
        }, TestContext.Current.CancellationToken);
        var stampBefore = (await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-stamp", TestContext.Current.CancellationToken))!.LastProfileSourceAt;

        // 同值 + null 字段 → 无实际写入
        await service.MergeProfileAsync("u-profile-stamp",
            new ExternalProfile("保持昵称", null, null, null, null, null, null, null, null, null),
            ProfileSource.Event, TestContext.Current.CancellationToken);

        var profile = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-stamp", TestContext.Current.CancellationToken);
        Assert.Equal("保持昵称", profile!.Nickname);
        Assert.Equal(ProfileSource.OAuthUserInfo, profile.ProfileSource);   // 未写字段 → 来源戳保留原值
        Assert.Equal(stampBefore, profile.LastProfileSourceAt);
    }

    /// <summary>
    /// 审核 P1-1 回归：user_filled 写昵称后，低优先级 event 写其它字段（头像）**不得把来源戳降级**，
    /// 否则后续 event 同源写入会击穿 user_filled 保护。
    /// </summary>
    [Fact]
    public async Task MergeProfile_UserFilled_ProtectedAfterLowerSourceWritesOtherField()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-profile-uf2");
        await user.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-profile-uf2",
            Nickname = "用户自填",
            ProfileSource = ProfileSource.UserFilled,
            LastProfileSourceAt = DateTime.UtcNow.AddDays(-2)
        }, TestContext.Current.CancellationToken);

        // 低优先级 event 补空头像（无昵称）
        await service.MergeProfileAsync("u-profile-uf2",
            new ExternalProfile(null, "https://cdn/a.png", null, null, null, null, null, null, null, null),
            ProfileSource.Event, TestContext.Current.CancellationToken);

        var afterAvatar = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-uf2", TestContext.Current.CancellationToken);
        Assert.Equal("https://cdn/a.png", afterAvatar!.Avatar);
        Assert.Equal(ProfileSource.UserFilled, afterAvatar.ProfileSource);   // 来源戳永不降级

        // 再来一次 event（同源）改昵称 → 仍不得覆盖 user_filled
        await service.MergeProfileAsync("u-profile-uf2",
            new ExternalProfile("事件昵称", null, null, null, null, null, null, null, null, null),
            ProfileSource.Event, TestContext.Current.CancellationToken);

        var final = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-uf2", TestContext.Current.CancellationToken);
        Assert.Equal("用户自填", final!.Nickname);
    }

    /// <summary>审核 P2-7 回归：新值为「微信用户」默认昵称时不得覆盖真实值（即使同源）。</summary>
    [Fact]
    public async Task MergeProfile_DefaultNickname_NewValueDoesNotOverwriteReal()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-profile-wx2");
        await user.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-profile-wx2",
            Nickname = "真实昵称",
            ProfileSource = ProfileSource.Event,
            LastProfileSourceAt = DateTime.UtcNow.AddDays(-1)
        }, TestContext.Current.CancellationToken);

        await service.MergeProfileAsync("u-profile-wx2",
            new ExternalProfile("微信用户", null, null, null, null, null, null, null, null, null),
            ProfileSource.Event, TestContext.Current.CancellationToken);

        var profile = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-wx2", TestContext.Current.CancellationToken);
        Assert.Equal("真实昵称", profile!.Nickname);   // 默认值不反向污染真实值
    }

    /// <summary>审核 P1-2 回归：更高优先级源提交同值也应认领来源戳（防后续低优先级同源写入绕过保护）。</summary>
    [Fact]
    public async Task MergeProfile_HigherSourceSameValue_ClaimsStamp()
    {
        var (service, user) = CreateService();
        await SeedAccountAsync(user, "u-profile-claim");
        await user.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-profile-claim",
            Nickname = "同一昵称",
            ProfileSource = ProfileSource.Event,
            LastProfileSourceAt = DateTime.UtcNow.AddDays(-1)
        }, TestContext.Current.CancellationToken);

        // user_filled 提交同值（无字段变更）→ 应认领戳
        await service.MergeProfileAsync("u-profile-claim",
            new ExternalProfile("同一昵称", null, null, null, null, null, null, null, null, null),
            ProfileSource.UserFilled, TestContext.Current.CancellationToken);

        var claimed = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-claim", TestContext.Current.CancellationToken);
        Assert.Equal(ProfileSource.UserFilled, claimed!.ProfileSource);

        // 之后 event 想改昵称 → 被 user_filled 保护拦截
        await service.MergeProfileAsync("u-profile-claim",
            new ExternalProfile("事件值", null, null, null, null, null, null, null, null, null),
            ProfileSource.Event, TestContext.Current.CancellationToken);
        var final = await user.Use<UserProfileEntityDataService>().GetByUIdAsync("u-profile-claim", TestContext.Current.CancellationToken);
        Assert.Equal("同一昵称", final!.Nickname);
    }

    /// <summary>并发冲突映射桩：GetByExternalIdAsync 初始 null（无映射）；LinkAsync 抛 FACTOR_ALREADY_BOUND
    /// （模拟并发首登唯一约束兜底）后暴露胜者映射——ProvisionAsync catch 路径回滚孤儿并重读胜者。</summary>
    internal sealed class ConflictingMapFake : IExternalIdentityService
    {
        private readonly string _winnerUid;
        public int LinkCalls { get; private set; }
        public bool ConflictThrown { get; private set; }
        private ExternalIdentityDto? _mapping;

        public ConflictingMapFake(string winnerUid) => _winnerUid = winnerUid;

        public Task<ExternalIdentityDto?> GetByExternalIdAsync(string channelId, string externalUserId, CancellationToken ct = default)
            => Task.FromResult(_mapping);

        public Task<ExternalIdentityDto> LinkAsync(string uid, string channelId, string externalUserId,
            string? operatorName = null, CancellationToken ct = default)
        {
            LinkCalls++;
            _mapping = new ExternalIdentityDto(_winnerUid, channelId, externalUserId, DateTime.UtcNow);
            ConflictThrown = true;
            throw new AuthenticationException(IAuthFactorService.FACTOR_ALREADY_BOUND);
        }

        public Task UnlinkAsync(string uid, string channelId, string externalUserId,
            string? operatorName = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ExternalIdentityDto>> GetByAccountAsync(string uid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExternalIdentityDto>>(Array.Empty<ExternalIdentityDto>());
    }
}