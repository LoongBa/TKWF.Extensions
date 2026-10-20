using System;
using System.Linq;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain.AuthController;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>D11 / V0.11.0（方案 §4.10 D6、T4）：<see cref="PlatformAccountMapService"/> 双职责——
/// <para>① <c>IPlatformAccountMapService</c>：纯业务系统映射（平台内部 id ↔ 业务 app + 业务本地 id，三键幂等写入）；</para>
/// <para>② <c>IExternalIdentityService</c>：外部身份统一契约四方法——权威为 <see cref="AuthFactorEntity"/> 的 Federated
/// 因素行（<c>Identifier = {channelId}|{externalUserId}</c>），全部委托 <see cref="IAuthFactorService"/>，
/// 本类零数据面读写（旧 UnionId 映射列与 <c>GetByUnionIdAsync</c> 已随 T1/T4 退役）。</para>
/// <para>V4.10.53（领域自治根治后重写）：门面继承 DomainServiceBase——StubDomainUser 直构（经基类 User 取上下文）；
/// <c>IAuthFactorService</c>/<c>IAuthAccountService</c> 经接口解析（测试桩注册**真实实现**——生产
/// <c>AddConstructibleService</c> 等价形态，非 Fake），DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 直建。</para></summary>
public class PlatformAccountMapServiceTests
{
    private static (PlatformAccountMapService Service, StubDomainUser Stub) CreateService()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql, register: (services, user) =>
        {
            // 生产等价：因素服务 + 账号写契约经接口解析（Unbind 的 TokenVersion++ 走 IAuthAccountService）
            services.AddSingleton<IAuthFactorService>(new AuthFactorService(
                user, Options.Create(new SecurityLoggingOptions()), NullLogger<AuthFactorService>.Instance));
            services.AddSingleton<IAuthAccountService>(new AuthAccountService(
                user,
                Options.Create(new DomainOptions()),
                Options.Create(AuthenticationTestHost.CreateOptions()),
                new FakeCredentialProtector()));
        });
        return (new PlatformAccountMapService(stub, NullLogger<PlatformAccountMapService>.Instance), stub);
    }

    private static DateTime AsUtc(DateTime value) => value.Kind == DateTimeKind.Utc ? value : value.ToUniversalTime();

    // ── ① 业务系统映射（IPlatformAccountMapService）──

    [Fact]
    public async Task Link_CreatesAndIsIdempotent()
    {
        var (service, _) = CreateService();

        var created = await service.LinkAsync("u-100", "app-1", "local-1");
        Assert.True(created.Id > 0);

        // 同三键（唯一索引）存在 → 幂等返回既有行，不重复写入
        var again = await service.LinkAsync("u-100", "app-1", "local-1");
        Assert.Equal(created.Id, again.Id);
        Assert.Equal(created.BusinessLocalId, again.BusinessLocalId);
    }

    [Fact]
    public async Task GetByPlatform_And_GetByBusiness_Bidirectional()
    {
        var (service, _) = CreateService();
        await service.LinkAsync("u-100", "app-1", "local-1");

        var byPlatform = await service.GetByPlatformAsync("u-100", "app-1");
        Assert.NotNull(byPlatform);
        Assert.Equal("local-1", byPlatform!.BusinessLocalId);

        var byBusiness = await service.GetByBusinessAsync("app-1", "local-1");
        Assert.NotNull(byBusiness);
        Assert.Equal("u-100", byBusiness!.PlatformAccountId);
    }

    [Fact]
    public async Task Link_SamePlatformAccount_DifferentBusinessApps_CreateSeparateRows()
    {
        var (service, _) = CreateService();

        await service.LinkAsync("u-100", "app-1", "local-1");
        await service.LinkAsync("u-100", "app-2", "local-2");

        Assert.Equal("local-1", (await service.GetByPlatformAsync("u-100", "app-1"))!.BusinessLocalId);
        Assert.Equal("local-2", (await service.GetByPlatformAsync("u-100", "app-2"))!.BusinessLocalId);
    }

    [Fact]
    public async Task GetByPlatform_Unknown_ReturnsNull()
    {
        var (service, _) = CreateService();
        Assert.Null(await service.GetByPlatformAsync("u-404", "app-1"));
        Assert.Null(await service.GetByBusinessAsync("app-1", "local-404"));
    }

    // ── ② 外部身份统一契约（IExternalIdentityService——因素行为权威）──

    [Fact]
    public async Task ExternalLink_ProjectsFactorRow_AndDoesNotTouchBusinessMap()
    {
        var (service, _) = CreateService();
        var ext = (IExternalIdentityService)service;

        var linked = await ext.LinkAsync("u-100", "wechat_mp", "openid-1");
        Assert.Equal("u-100", linked.UId);
        Assert.Equal("wechat_mp", linked.ChannelId);
        Assert.Equal("openid-1", linked.ExternalUserId);
        Assert.NotEqual(default, linked.LinkedAt);

        // 权威在因素行：Identifier 规范化为 {channelId}|{externalUserId}
        var factor = await ext.GetByExternalIdAsync("wechat_mp", "openid-1");
        Assert.NotNull(factor);
        Assert.Equal(linked.LinkedAt, AsUtc(factor!.LinkedAt));

        // 业务映射表不被联邦绑定污染（映射列与联邦列已解耦）
        Assert.Null(await service.GetByPlatformAsync("u-100", "wechat_mp"));
    }

    [Fact]
    public async Task ExternalLink_SameIdentity_IsIdempotent()
    {
        var (service, _) = CreateService();
        var ext = (IExternalIdentityService)service;

        var first = await ext.LinkAsync("u-100", "wechat_mp", "openid-1");
        var second = await ext.LinkAsync("u-100", "wechat_mp", "openid-1");

        Assert.Equal(first.LinkedAt, AsUtc(second.LinkedAt));   // BoundAt 只写一次，未重建
    }

    [Fact]
    public async Task ExternalLink_CrossAccount_SameIdentity_ThrowsFactorAlreadyBound()
    {
        var (service, _) = CreateService();
        var ext = (IExternalIdentityService)service;
        await ext.LinkAsync("u-100", "wechat_mp", "openid-1");

        // fail-closed：同一外部身份不得绑到第二个账号（唯一约束 → 重查跨 UId → FACTOR_ALREADY_BOUND）
        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => ext.LinkAsync("u-200", "wechat_mp", "openid-1"));
        Assert.Equal(IAuthFactorService.FACTOR_ALREADY_BOUND, ex.Message);
    }

    [Fact]
    public async Task GetByAccount_ProjectsOnlyFederatedFactors()
    {
        var (service, stub) = CreateService();
        var ext = (IExternalIdentityService)service;
        await ext.LinkAsync("u-100", "wechat_mp", "openid-1");
        await ext.LinkAsync("u-100", "wechat_web", "openid-2");

        // 非联邦因素（密码：Identifier 恒为 UId）不进入外部身份投影
        await stub.Use<IAuthFactorService>().BindAsync(
            "u-100", AuthFactorTypes.Password, "u-100", secretHash: "hash");

        var identities = await ext.GetByAccountAsync("u-100");
        Assert.Equal(2, identities.Count);
        Assert.Equal(["wechat_mp", "wechat_web"], identities.Select(x => x.ChannelId).OrderBy(x => x).ToArray());
        Assert.All(identities, x => Assert.Equal("u-100", x.UId));
    }

    [Fact]
    public async Task GetByAccount_OtherAccount_ReturnsEmpty()
    {
        var (service, _) = CreateService();
        var ext = (IExternalIdentityService)service;
        await ext.LinkAsync("u-100", "wechat_mp", "openid-1");

        Assert.Empty(await ext.GetByAccountAsync("u-404"));
    }

    [Fact]
    public async Task Unlink_RemovesFactor_KeepsOtherIdentities()
    {
        var (service, stub) = CreateService();
        var ext = (IExternalIdentityService)service;
        await stub.Use<IAuthAccountService>().CreateAsync(new AuthAccountEntity { UId = "u-100", IsEnabled = true });
        await ext.LinkAsync("u-100", "wechat_mp", "openid-1");
        await ext.LinkAsync("u-100", "wechat_web", "openid-2");

        await ext.UnlinkAsync("u-100", "wechat_mp", "openid-1");

        Assert.Null(await ext.GetByExternalIdAsync("wechat_mp", "openid-1"));
        Assert.NotNull(await ext.GetByExternalIdAsync("wechat_web", "openid-2"));
    }

    [Fact]
    public async Task Unlink_UnknownOrForeign_IsIdempotent()
    {
        var (service, _) = CreateService();
        var ext = (IExternalIdentityService)service;
        await ext.LinkAsync("u-100", "wechat_mp", "openid-1");

        // 不存在 → 幂等返回
        await ext.UnlinkAsync("u-100", "wechat_mp", "openid-404");
        // 归属不符（属 u-100）→ 幂等返回，不泄露、不误删
        await ext.UnlinkAsync("u-200", "wechat_mp", "openid-1");

        Assert.NotNull(await ext.GetByExternalIdAsync("wechat_mp", "openid-1"));
    }

    [Fact]
    public async Task Unlink_LastFactor_Throws_NotSwallowed()
    {
        var (service, _) = CreateService();
        var ext = (IExternalIdentityService)service;
        await ext.LinkAsync("u-100", "wechat_mp", "openid-1");

        // 唯一可登录凭据 → 守卫硬失败（fail-closed：不静默留下零凭据账号）
        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => ext.UnlinkAsync("u-100", "wechat_mp", "openid-1"));
        Assert.Equal(IAuthFactorService.LAST_FACTOR_CANNOT_UNBIND, ex.Message);
        Assert.NotNull(await ext.GetByExternalIdAsync("wechat_mp", "openid-1"));
    }
}
