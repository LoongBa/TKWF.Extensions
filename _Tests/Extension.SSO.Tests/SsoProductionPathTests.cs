using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Authentication;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.SSO.Tests;

/// <summary>
/// SSO 生产路径集成测试（V4.10.53 领域自治根治，ADR90 正确路线）——经<b>真实 DI</b>
/// （双 Initializer ConfigureServices + FreeSql SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c>
/// + <c>User.Use&lt;接口&gt;()</c>（AOP 路径：设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂）——
/// 验证 SSO 三钩子 + SSO 经 Abstractions 契约消费认证内核（ISsoAccountQueryService）完整链路。
/// <para>对齐 AuthenticationProductionPathTests 范式。DataService 不手动注册——测试经 <c>Use&lt;具体类&gt;()</c>
/// NoAop 路径直建（IEntityDAC 从 DI 解析，红线合规）。</para>
/// </summary>
public class SsoProductionPathTests : TestHostBase
{
    protected override TestUserInfo CreateUser() => new("user-42", "测试用户");

    protected override void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<SsoClientEntity>();
        fsql.CodeFirst.SyncStructure<SsoAccessCodeEntity>();
        fsql.CodeFirst.SyncStructure<AuthAccountEntity>();
        fsql.CodeFirst.SyncStructure<PlatformAccountMapEntity>();
    }

    protected override void ConfigureHost(IServiceCollection services)
    {
        // 双扩展 Initializer（SSO + 认证内核——SSO 消费 ISsoAccountQueryService 实现）
        new SsoExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // Options 值绑定（消费方 DomainHost 等价）
        var ssoOpts = SsoTestHost.CreateOptions();
        services.Configure<SsoOptions>(o =>
        {
            o.Issuer = ssoOpts.Issuer;
            o.SigningKeyPath = ssoOpts.SigningKeyPath;
            o.CurrentKid = ssoOpts.CurrentKid;
            o.SigningKeys = ssoOpts.SigningKeys;
            o.SecretEncryptionKeyPath = ssoOpts.SecretEncryptionKeyPath;
            o.IsProduction = ssoOpts.IsProduction;
        });
        var authOpts = SsoTestHost.CreateAuthOptions();
        services.Configure<AuthCenterOptions>(o =>
        {
            o.Issuer = authOpts.Issuer;
            o.SigningKeyPath = authOpts.SigningKeyPath;
            o.PublicKeyPath = authOpts.PublicKeyPath;
            o.CurrentKid = authOpts.CurrentKid;
            o.SigningKeys = authOpts.SigningKeys;
            o.RedirectUriWhitelist = authOpts.RedirectUriWhitelist;
        });

        // 实体 DAC（真实 FreeSqlEntityDAC）
        SsoTestHost.RegisterEntityDacs(services);
    }

    /// <summary>生产路径：User.Use&lt;IToken2Service&gt;()——AOP 路径（守卫工厂 → Token2Service）→ 签发/验签。</summary>
    [Fact]
    public async Task Use_IToken2Service_ThroughGuardFactory_IssueValidate()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var service = user.Use<IToken2Service>();
        var issued = await service.IssueToken2Async(new Token2IssueRequest("u-100", "app-1", "profile:basic"), CancellationToken.None);
        var validated = await service.ValidateToken2Async(issued.Token, CancellationToken.None);
        Assert.Equal("u-100", validated.UId);
        Assert.Equal("app-1", validated.TargetAppId);
    }

    /// <summary>生产路径：User.Use&lt;ISsoClientService&gt;()——注册 + origin 校验全链落库。</summary>
    [Fact]
    public async Task Use_ISsoClientService_ThroughGuardFactory_RegisterAndValidate()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var clientService = user.Use<ISsoClientService>();
        var client = await clientService.RegisterAsync(["https://app.example.com"], ["profile:basic"], CancellationToken.None);

        Assert.StartsWith("app-", client.AppId);
        Assert.True(await clientService.IsOriginAllowedAsync(client.AppId, "https://app.example.com", CancellationToken.None));
        Assert.False(await clientService.IsOriginAllowedAsync(client.AppId, "https://evil.example.com", CancellationToken.None));
    }

    /// <summary>生产路径：SSO accesscode 全链——签发 → 消费 → 重放拒（原子 CAS）。</summary>
    [Fact]
    public async Task Use_ISsoAccessCodeService_IssueConsumeReplay()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var accessCodeService = user.Use<ISsoAccessCodeService>();
        var issued = await accessCodeService.IssueAsync(
            new SsoAccessCodeIssueRequest("mp-1", "u-100", "app-1", "profile:basic", OpenId: "openid-xxx"), CancellationToken.None);

        var consumed = await accessCodeService.ConsumeAsync(issued.Code, default, "1.2.3.4", CancellationToken.None);
        Assert.Equal("u-100", consumed.UId);
        Assert.Equal("app-1", consumed.TargetAppId);

        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(
            () => accessCodeService.ConsumeAsync(issued.Code, default, null, CancellationToken.None));
    }

    /// <summary>生产路径：SSO profile 消费认证内核（ISsoAccountQueryService 经 Abstractions 契约，守卫工厂跨扩展解析）——scope 裁剪。</summary>
    [Fact]
    public async Task Use_ISsoProfileService_CrossExtension_ScopeTrimmed()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        // 种子账号经 NoAop 直建 DataService（认证内核实体）
        var account = new AuthAccountEntity
        {
            UId = "u-100", Phone = "13800138000", Nickname = "张三", Avatar = "https://cdn.example.com/a.png", AuthLevel = 2,
        };
        await user.Use<AuthAccountEntityDataService>().CreateAsync(account, CancellationToken.None);

        var profileService = user.Use<ISsoProfileService>();
        var full = await profileService.GetProfileAsync("app-1", "u-100", ["profile:basic"], "1.2.3.4", CancellationToken.None);
        Assert.NotNull(full);
        Assert.Equal("u-100", full!.UId);
        Assert.Equal("张三", full.Nickname);

        var minimal = await profileService.GetProfileAsync("app-1", "u-100", [], "1.2.3.4", CancellationToken.None);
        Assert.NotNull(minimal);
        Assert.Null(minimal!.Nickname);
    }
}
