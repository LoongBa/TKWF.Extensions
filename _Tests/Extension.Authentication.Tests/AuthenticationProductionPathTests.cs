using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.UserCenter;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>
/// Authentication 生产路径集成测试（V4.10.53 领域自治根治，ADR90，正确路线）——经<b>真实 DI</b>
/// （Initializer ConfigureServices + FreeSql SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c>
/// + <c>User.Use&lt;接口&gt;()</c>（AOP 路径：设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂 →
/// 经 ActivatorUtilities 直建实现）→ 断言令牌全链读写落库。
/// <para>对齐 Settings V0.3.0 / Account V0.5.0 / Identity V0.5.0 测试宿主范式。DataService 不手动注册——
/// 生产经 SG 消费方聚合自动注册；测试经 <c>Use&lt;具体类&gt;()</c> NoAop 路径直建（IEntityDAC 从 DI 解析，红线合规）。</para>
/// </summary>
public class AuthenticationProductionPathTests
{
    private static IFreeSql CreateInMemoryFreeSql()
        => AuthenticationTestHost.CreateInMemoryFreeSql();

    /// <summary>
    /// 构建消费方生产形态 DI：Initializer ConfigureServices + Options 值（RSA 密钥）+ FreeSql 基础设施。
    /// </summary>
    private static ServiceProvider CreateProvider(IFreeSql fsql)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // Options 值绑定（消费方 DomainHost 等价——TKWF:AuthCenter 节绑定 RSA 密钥 + Issuer）
        var opts = AuthenticationTestHost.CreateOptions();
        services.Configure<AuthCenterOptions>(o =>
        {
            o.Issuer = opts.Issuer;
            o.SigningKeyPath = opts.SigningKeyPath;
            o.PublicKeyPath = opts.PublicKeyPath;
            o.CurrentKid = opts.CurrentKid;
            o.SigningKeys = opts.SigningKeys;
            o.RedirectUriWhitelist = opts.RedirectUriWhitelist;
            o.LoginProtection = opts.LoginProtection;
        });

        // FreeSql 基础设施（消费方 DomainHost 等价注册）
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        AuthenticationTestHost.RegisterEntityDacs(services);

        return services.BuildServiceProvider();
    }

    /// <summary>生产路径：User.Use&lt;ITokenService&gt;()——AOP 路径（守卫工厂 → 直建 TokenService）→ 签发/验签/撤销全链落库。</summary>
    [Fact]
    public async Task Use_ITokenService_ThroughGuardFactory_IssueValidateRevoke()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };

        var tokenService = user.Use<ITokenService>();

        // 种子账号经 NoAop 直建 DataService（IEntityDAC 从 DI 解析，红线合规）
        var account = new AuthAccountEntity { UId = "u-1001", Phone = "13800138000", AuthLevel = 1, TokenVersion = 0 };
        await user.Use<AuthAccountEntityDataService>().CreateAsync(account, CancellationToken.None);

        var issued = await tokenService.IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false), CancellationToken.None);
        var validation = await tokenService.ValidateTokenAsync(issued.AccessToken, CancellationToken.None);
        Assert.Equal(account.UId, validation.UserId);
        Assert.Equal(AuthTypes.Sms, validation.AuthType);

        await tokenService.RevokeTokenAsync(validation.Jti, "logout", CancellationToken.None);
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => tokenService.ValidateTokenAsync(issued.AccessToken, CancellationToken.None));
        Assert.Equal("TOKEN_REVOKED", ex.Message);

        // 落库断言（DataService NoAop 直建路径——IEntityDAC 从 DI 解析）
        var refreshDs = user.Use<AuthRefreshTokenEntityDataService>();
        var row = await refreshDs.GetByTokenHashAsync(TokenService.Sha256Hex(issued.RefreshToken), CancellationToken.None);
        Assert.NotNull(row);
        Assert.Equal(account.UId, row!.UserId);
    }

    /// <summary>生产路径内部链：OAuthTicketService（守卫工厂）→ Use&lt;ITokenService&gt;()（AOP 守卫工厂链）→ 票据换令牌。</summary>
    [Fact]
    public async Task Use_IOAuthTicketService_ThroughGuardFactory_ExchangeIssuesTokens()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        DomainUser<TestUserInfo>.BindScope(sp);
        var user = new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo("user-42", "测试用户") };

        var account = new AuthAccountEntity { UId = "u-1001", Phone = "13800138000", AuthLevel = 1, TokenVersion = 0 };
        await user.Use<AuthAccountEntityDataService>().CreateAsync(account, CancellationToken.None);

        var ticketService = user.Use<IOAuthTicketService>();
        var ticket = await ticketService.IssueAsync(new OAuthTicketIssueRequest("app-1", "https://app.example.com/callback", State: "s1"), CancellationToken.None);

        // 绑定用户（签发方绑定后回填）
        var ticketDs = user.Use<OAuthTicketEntityDataService>();
        var entity = await ticketDs.GetByTicketAsync(ticket, CancellationToken.None);
        entity!.UserId = account.UId;
        await ticketDs.UpdateAsync(entity, CancellationToken.None);

        var result = await ticketService.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, null, "app-1", "s1"), CancellationToken.None);
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
    }

    /// <summary>AddConstructibleService 守卫——非 User.Use&lt;T&gt;() 调用链内解析接口必抛（DI004 运行期兜底）。</summary>
    [Fact]
    public void GuardFactory_OutsideUseScope_Throws()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);

        var ex = Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<ITokenService>());
        Assert.Contains("领域架构守卫", ex.Message);
        Assert.Contains("ITokenService", ex.Message);
    }

    /// <summary>实现类 throw-factory——禁止直接 DI 解析（门面必须经 User.Use&lt;接口&gt;()）。</summary>
    [Fact]
    public void Implementations_NotDirectlyResolvable()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);

        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<TokenService>());
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<OAuthTicketService>());
        Assert.Throws<InvalidOperationException>(() => sp.GetRequiredService<AuthAccountQueryService>());
    }

    /// <summary>
    /// 接线型契约（IUserProfileSource——UserCenter.Abstractions 非 IDomainService）经普通 DI 可构造——
    /// 旧 ctor(IDomainUser)（永不注册 DI——D01）致 UserCenterQueryService.GetService 构造失败、档案读取静默降级
    /// （真实生产故障）；改 ctor(IServiceProvider) 后全 DI 可解析（C1 延迟解析 IAuthAccountQueryService）。
    /// </summary>
    [Fact]
    public void WiringContract_Resolvable_From_PlainDi()
    {
        using var fsql = CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);

        // UserCenterQueryService 生产路径：sp.GetService<IUserProfileSource>()（普通 DI）
        var source = sp.GetService<IUserProfileSource>();
        Assert.NotNull(source);
        Assert.IsType<AuthAccountUserProfileSource>(source);
    }
}
