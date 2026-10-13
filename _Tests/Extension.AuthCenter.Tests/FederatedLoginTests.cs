using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.AuthController;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Session;
using TKW.Framework.Domain.Testing.Session;
using TKW.Framework.Domain.Ticket;
using TKW.Framework.Enumerations;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// E1（外部身份联邦与会话绑定守卫-开发方案 §五 E1 + ADR-Federation E1）：AuthCenter
/// <see cref="AuthenticationUserHelperBase{TUserInfo}.OnLoginByFederatedAsync"/> 钩子 override 测试——
/// FederatedIdentity → <see cref="IAuthAccountQueryService.GetByUIdAsync"/> → 账号存在/启用/冻结检查 →
/// <see cref="IAuthorizationMapper{TUserInfo}"/> 角色映射（合成 <see cref="TokenValidationResult"/>）→
/// <see cref="AuthenticationUserHelperBase{TUserInfo}.CreateUserInfoFromAccount"/> 工厂。
/// <para>生产路径全链：真实 DI（AuthCenter ConfigureServices + FreeSql SQLite 基础设施 + TestSessionManager）+
/// <see cref="DomainHost{TUserInfo}"/>（D5 钩子需 AttachedHost.UserHelper——<c>BindScope</c> 无宿主桩不可达）+
/// D3 <see cref="InMemorySealedTicketService{T}"/> 票据 + <c>user.LoginAsFederatedAsync</c> 兑换→守卫→绑定 全链。</para>
/// <para>⚠️ 专用 <see cref="FederatedTestUserInfo"/> 泛型：<c>DomainHost&lt;TestUserInfo&gt;.Root</c> 已被
/// <see cref="AuthCenterEndpointHost"/>（Web 宿主）占用——"DomainHost 不能重复初始化"，联邦宿主须独立泛型（对齐
/// 框架 FederatedLoginTests 多 TUserInfo 模式）。</para>
/// </summary>
public class FederatedLoginTests
{
    // ────────────────────────────────────────────────
    // ① 全链：InMemory 票据 → LoginAsFederatedAsync → 钩子映射（合成 TokenValidationResult → mapper）→ TUserInfo
    // ────────────────────────────────────────────────

    /// <summary>全链成功：票据兑换 → uid 一致 → 钩子查号（启用账号）→ mapper 角色映射 → CreateUserInfoFromAccount → D1 守卫绑定。</summary>
    [Fact]
    public async Task LoginAsFederatedAsync_FullChain_MapsAccountWithRoles()
    {
        var host = await FederatedHost.HostAsync();
        await using var scope = await host.CreateSessionScopeAsync();
        var user = scope.User;

        // 种子账号（DataService NoAop 直建——IEntityDAC 从 DI 解析，红线合规）
        var account = new AuthAccountEntity
        {
            UId = "u-fed-full",
            Phone = "13800138001",
            AuthLevel = (int)AuthLevel.Federated,
            IsEnabled = true,
            TokenVersion = 0
        };
        await user.Use<AuthAccountEntityDataService>().CreateAsync(account, CancellationToken.None);

        // 签发密封票据（D3 InMemory 后端 Singleton——跨作用域可兑换）
        var identity = new FederatedIdentity("u-fed-full", "ch-wechat", "openid-1", "wechat_event", 2);
        var issued = await user.GetService<ISealedTicketService<FederatedIdentity>>()
            .IssueAsync(identity, TimeSpan.FromMinutes(5));

        // 全链登录（期望身份 uid 一致）
        await user.LoginAsFederatedAsync(issued.Ticket, identity with { ChannelType = "wechat_event" }, EnumLoginFrom.PcWeb);

        Assert.True(user.IsAuthenticated);
        Assert.Equal("u-fed-full", user.UserInfo!.UserIdString);
        Assert.Equal("13800138001", user.UserInfo.UserName);              // 工厂：Phone ?? UId
        Assert.Equal(EnumLoginFrom.PcWeb, user.UserInfo.LoginFrom);
        // 角色映射生效（合成 TokenValidationResult 路径——mapper 捕获 sub/AuthType/ChannelType 断言）
        Assert.Equal(new[] { "role-a", "role-b" }, user.UserInfo.Roles);
        Assert.Equal("u-fed-full", FederatedHost.Mapper.LastSub);
        Assert.Equal(AuthTypes.Federated, FederatedHost.Mapper.LastAuthType);
        Assert.Equal("wechat_event", FederatedHost.Mapper.LastChannelType);
    }

    // ────────────────────────────────────────────────
    // ② 账号不存在 → ACCOUNT_NOT_FOUND
    // ────────────────────────────────────────────────

    /// <summary>钩子查号未命中 → ACCOUNT_NOT_FOUND（错误码与短信钩子 L43-46 先例一致）。</summary>
    [Fact]
    public async Task LoginAsFederatedAsync_AccountNotFound_ThrowsAccountNotFound()
    {
        var host = await FederatedHost.HostAsync();
        await using var scope = await host.CreateSessionScopeAsync();
        var user = scope.User;

        var identity = new FederatedIdentity("u-fed-missing", "ch-wechat", "openid-missing", "wechat_event", 2);
        var issued = await user.GetService<ISealedTicketService<FederatedIdentity>>()
            .IssueAsync(identity, TimeSpan.FromMinutes(5));

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            user.LoginAsFederatedAsync(issued.Ticket, identity with { ChannelType = "wechat_event" }, EnumLoginFrom.PcWeb));

        Assert.Equal("ACCOUNT_NOT_FOUND", ex.Message);
        Assert.False(user.IsAuthenticated);
    }

    // ────────────────────────────────────────────────
    // ③ 账号禁用 → ACCOUNT_DISABLED
    // ────────────────────────────────────────────────

    /// <summary>钩子查号命中但 IsEnabled=false → ACCOUNT_DISABLED。</summary>
    [Fact]
    public async Task LoginAsFederatedAsync_AccountDisabled_ThrowsAccountDisabled()
    {
        var host = await FederatedHost.HostAsync();
        await using var scope = await host.CreateSessionScopeAsync();
        var user = scope.User;

        var account = new AuthAccountEntity
        {
            UId = "u-fed-disabled",
            Phone = "13800138002",
            AuthLevel = (int)AuthLevel.Federated,
            IsEnabled = false,
            TokenVersion = 0
        };
        await user.Use<AuthAccountEntityDataService>().CreateAsync(account, CancellationToken.None);

        var identity = new FederatedIdentity("u-fed-disabled", "ch-wechat", "openid-disabled", "wechat_event", 2);
        var issued = await user.GetService<ISealedTicketService<FederatedIdentity>>()
            .IssueAsync(identity, TimeSpan.FromMinutes(5));

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            user.LoginAsFederatedAsync(issued.Ticket, identity with { ChannelType = "wechat_event" }, EnumLoginFrom.PcWeb));

        Assert.Equal("ACCOUNT_DISABLED", ex.Message);
        Assert.False(user.IsAuthenticated);
    }

    // ────────────────────────────────────────────────
    // ④ 账号冻结 → ACCOUNT_FROZEN
    // ────────────────────────────────────────────────

    /// <summary>钩子查号命中但 IsFrozenEffective（IsFrozen=true 永久冻结）→ ACCOUNT_FROZEN。</summary>
    [Fact]
    public async Task LoginAsFederatedAsync_AccountFrozen_ThrowsAccountFrozen()
    {
        var host = await FederatedHost.HostAsync();
        await using var scope = await host.CreateSessionScopeAsync();
        var user = scope.User;

        var account = new AuthAccountEntity
        {
            UId = "u-fed-frozen",
            Phone = "13800138003",
            AuthLevel = (int)AuthLevel.Federated,
            IsEnabled = true,
            IsFrozen = true,        // 永久冻结（FreezeEnd=null）
            TokenVersion = 0
        };
        await user.Use<AuthAccountEntityDataService>().CreateAsync(account, CancellationToken.None);

        var identity = new FederatedIdentity("u-fed-frozen", "ch-wechat", "openid-frozen", "wechat_event", 2);
        var issued = await user.GetService<ISealedTicketService<FederatedIdentity>>()
            .IssueAsync(identity, TimeSpan.FromMinutes(5));

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            user.LoginAsFederatedAsync(issued.Ticket, identity with { ChannelType = "wechat_event" }, EnumLoginFrom.PcWeb));

        Assert.Equal("ACCOUNT_FROZEN", ex.Message);
        Assert.False(user.IsAuthenticated);
    }

    // ════════════════════════════════════════════════
    // 测试基建——专用 TUserInfo 独立 DomainHost（Root 静态单例，仅本类消费）
    // ════════════════════════════════════════════════

    /// <summary>联邦测试专用 UserInfo（独立泛型——避免与 Web 宿主 TestUserInfo 的 DomainHost Root 冲突）。</summary>
    public sealed class FederatedTestUserInfo : SimpleUserInfo
    {
        public FederatedTestUserInfo() { }

        public FederatedTestUserInfo(string userIdString, string userName) : base(userIdString, userName) { }
    }

    /// <summary>联邦测试 UserHelper——继承 AuthCenter 预实现基类（OnLoginByFederatedAsync 为基类 override，消费方仅实现两工厂）。</summary>
    private sealed class FederatedAuthUserHelper : AuthenticationUserHelperBase<FederatedTestUserInfo>
    {
        protected override Task<FederatedTestUserInfo> OnNewGuestSessionCreatedAsync(SessionInfo session)
            => Task.FromResult(new FederatedTestUserInfo("guest", "Guest"));

        protected override FederatedTestUserInfo CreateUserInfoFromAccount(AuthAccountEntity account, IReadOnlyList<string> roles)
            => new(account.UId, account.Phone ?? account.UId) { Roles = roles.ToList() };

        protected override FederatedTestUserInfo CreateUserInfoFromToken(TokenValidationResult token, IReadOnlyList<string> roles)
            => new(token.UserId, token.UserId) { Roles = roles.ToList() };
    }

    /// <summary>联邦测试宿主初始化器（白名单声明 + 元数据空桩——测试项目无 SG 元数据，扩展接线经手动 ConfigureServices）。</summary>
    [TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer<>))]
    private sealed class FederatedInitializer : DomainHostInitializerBase<FederatedTestUserInfo>
    {
        protected override IProjectMetaContext OnRegisterInfrastructureServices(
            IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration? configuration, IDomainHostOptions options)
            => new TestMetaContext();

        protected override DomainUserHelperBase<FederatedTestUserInfo> OnRegisterDomainServices(
            IServiceCollection services, Microsoft.Extensions.Configuration.IConfiguration? configuration)
            => new FederatedAuthUserHelper();
    }

    /// <summary>
    /// 联邦全链测试宿主——真实 DI + FreeSql SQLite + TestSessionManager + D3 票据后端 +
    /// AuthCenter ConfigureServices（手动接线——测试项目无 SG 元数据，扩展注册表为空，三钩子不自动触发）。
    /// 静态单例（<c>DomainHost&lt;FederatedTestUserInfo&gt;.Root</c> 不可二次初始化），测试类内顺序执行。
    /// </summary>
    private static class FederatedHost
    {
        public static DomainHost<FederatedTestUserInfo>? Host { get; private set; }

        /// <summary>Fake 业务角色映射器（消费方实现——钩子 GetService 判空消费；LastSub/LastAuthType/LastChannelType 断言合成 TokenValidationResult）。</summary>
        public static FakeAuthorizationMapper Mapper { get; } = new();

        public static async Task<DomainHost<FederatedTestUserInfo>> HostAsync()
        {
            if (Host == null)
            {
                var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
                var services = new ServiceCollection();
                services.AddLogging();
                // 测试会话管理器（AsyncLocal 隔离——CreateSessionScopeAsync 恢复/绑定守卫依赖）
                services.AddSingleton<ISessionManager<FederatedTestUserInfo>, TestSessionManager<FederatedTestUserInfo>>();
                // FreeSql 基础设施（消费方 DomainHost 等价）
                services.AddSingleton<IFreeSql>(fsql);
                services.AddSingleton<UnitOfWorkManager>();
                AuthenticationTestHost.RegisterEntityDacs(services);
                // D3 密封票据内存后端 Singleton（LoginAsFederatedAsync Use<ISealedTicketService<FederatedIdentity>> 帧内解析）
                services.AddSingleton<ISealedTicketService<FederatedIdentity>, InMemorySealedTicketService<FederatedIdentity>>();
                // 凭据保护器（Fake——守卫工厂 ActivatorUtilities ctor 依赖可解析）
                services.AddSingleton<ICredentialProtector, FakeCredentialProtector>();
                // 业务角色映射器（IAuthorizationMapper 消费方实现——TryAdd 语义普通 DI，钩子 GetService 判空）
                services.AddSingleton<IAuthorizationMapper<FederatedTestUserInfo>>(Mapper);
                // AuthCenter 扩展接线（手动——测试项目无 SG 元数据，扩展注册表为空，三钩子不自动触发）
                new AuthCenterExtensionInitializer<FederatedTestUserInfo>().ConfigureServices(services);
                // Options 值绑定（AuthCenterOptions——守卫工厂 ctor 可解析；TokenService 未被测试消费，注册保真）
                var opts = AuthenticationTestHost.CreateOptions();
                services.Configure<AuthCenterOptions>(o =>
                {
                    o.Issuer = opts.Issuer;
                    o.SigningKeyPath = opts.SigningKeyPath;
                    o.PublicKeyPath = opts.PublicKeyPath;
                    o.CurrentKid = opts.CurrentKid;
                    o.SigningKeys = opts.SigningKeys;
                    o.RedirectUriWhitelist = opts.RedirectUriWhitelist;
                });

                var options = new DomainOptions { IsDevelopment = true };
                var host = DomainHost<FederatedTestUserInfo>.Initialize<FederatedInitializer>(services, options);
                var sp = services.BuildServiceProvider();
                await sp.GetRequiredService<DomainHostInitializerBase<FederatedTestUserInfo>>()
                    .ServiceProviderBuiltCallbackAsync(sp);
                Host = host;
            }
            return Host;
        }
    }

    /// <summary>Fake IAuthorizationMapper——捕获合成 TokenValidationResult 载荷（sub/AuthType/ChannelType 断言）+ 可配置角色。</summary>
    internal sealed class FakeAuthorizationMapper : IAuthorizationMapper<FederatedTestUserInfo>
    {
        public string? LastSub { get; private set; }
        public string? LastAuthType { get; private set; }
        public string? LastChannelType { get; private set; }
        public IReadOnlyList<string> Result { get; set; } = ["role-a", "role-b"];

        public Task<IReadOnlyList<string>> MapRolesAsync(string sub, TokenValidationResult token, CancellationToken ct = default)
        {
            LastSub = sub;
            LastAuthType = token.AuthType;
            LastChannelType = token.ChannelType;
            return Task.FromResult(Result);
        }
    }
}
