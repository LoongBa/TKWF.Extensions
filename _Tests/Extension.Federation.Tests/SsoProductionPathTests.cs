using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// Federation 连接层生产路径集成测试（V4.10.53 领域自治根治，ADR90 正确路线）——经<b>真实 DI</b>
/// （双 Initializer ConfigureServices + FreeSql SQLite 基础设施 + AddLogging）+ <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c>
/// + <c>User.Use&lt;接口&gt;()</c>（AOP 路径：设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂）。
/// <para><b>TrustCenter 剥离（2026-10-09）</b>：自 SsoProductionPathTests 拆分——token2/accesscode/client 三用例
/// （信任内核）迁 TrustCenter.Tests（TrustCenterProductionPathTests）；本文件仅保留连接层用例
/// （ISsoProfileService 跨扩展——经 AuthCenter.Abstractions 契约消费认证内核）。</para>
/// <para>对齐 AuthenticationProductionPathTests 范式。DataService 不手动注册——测试经 <c>Use&lt;具体类&gt;()</c>
/// NoAop 路径直建（IEntityDAC 从 DI 解析，红线合规）。</para>
/// </summary>
public class SsoProductionPathTests : TestHostBase
{
    protected override TestUserInfo CreateUser() => new("user-42", "测试用户");

    protected override void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<AuthAccountEntity>();
        fsql.CodeFirst.SyncStructure<PlatformAccountMapEntity>();
        fsql.CodeFirst.SyncStructure<UserProfileEntity>();
    }

    protected override void ConfigureHost(IServiceCollection services)
    {
        // 双扩展 Initializer（Federation 连接层 + 认证内核——ISsoProfileService 消费 ISsoAccountQueryService 实现）
        new FederationExtensionInitializer<TestUserInfo>().ConfigureServices(services);
        new AuthCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // Options 值绑定（消费方 DomainHost 等价）
        var authOpts = FederationTestHost.CreateAuthOptions();
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
        FederationTestHost.RegisterEntityDacs(services);
    }

    /// <summary>生产路径：Federation profile 消费认证内核（ISsoAccountQueryService 经 Abstractions 契约，守卫工厂跨扩展解析）——scope 裁剪。</summary>
    [Fact]
    public async Task Use_ISsoProfileService_CrossExtension_ScopeTrimmed()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        // 种子账号经 NoAop 直建 DataService（认证内核实体）
        var account = new AuthAccountEntity
        {
            UId = "u-100", Phone = "13800138000", AuthLevel = 2,
        };
        await user.Use<AuthAccountEntityDataService>().CreateAsync(account, CancellationToken.None);

        // V0.9.0（A.1 凭据/档案分离）：SSO 档案（Nickname/Avatar）从 UserProfile 1:1 表读——种子档案经其 DataService
        await user.Use<UserProfileEntityDataService>().CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-100",
            Nickname = "张三",
            Avatar = "https://cdn.example.com/a.png",
        }, CancellationToken.None);

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
