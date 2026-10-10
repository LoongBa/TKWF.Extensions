using System;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.TrustCenter.Tests;

/// <summary>
/// TrustCenter 生产路径集成测试（V4.10.53 领域自治根治，ADR90 正确路线）——经<b>真实 DI</b>
/// （TrustCenterExtensionInitializer ConfigureServices + FreeSql SQLite 基础设施 + AddLogging）+
/// <c>DomainUser&lt;TestUserInfo&gt;.BindScope(provider)</c> + <c>User.Use&lt;接口&gt;()</c>
/// （AOP 路径：设 CurrentAopUser → GetRequiredService → AddConstructibleService 守卫工厂）——
/// 验证 TrustCenter 三钩子 + 信任内核门面（IToken2Service/ISsoClientService/IAccessCodeService）完整链路。
/// <para><b>TrustCenter 剥离（2026-10-09）</b>：自 Federation.Tests SsoProductionPathTests 拆分迁入——
/// token2/accesscode/client 三用例（信任内核）；<see cref="ISsoProfileService"/> 跨扩展用例（连接层）留 Federation.Tests。</para>
/// <para>对齐 AuthenticationProductionPathTests 范式。DataService 不手动注册——测试经 <c>Use&lt;具体类&gt;()</c>
/// NoAop 路径直建（IEntityDAC 从 DI 解析，红线合规）。</para>
/// </summary>
public class TrustCenterProductionPathTests : TestHostBase
{
    protected override TestUserInfo CreateUser() => new("user-42", "测试用户");

    protected override void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<SsoClientEntity>();
        fsql.CodeFirst.SyncStructure<AccessCodeEntity>();
    }

    protected override void ConfigureHost(IServiceCollection services)
    {
        // TrustCenter 扩展 Initializer（信任内核门面）
        new TrustCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // Options 值绑定（消费方 DomainHost 等价）
        var opts = TrustCenterTestHost.CreateOptions();
        services.Configure<TrustCenterOptions>(o =>
        {
            o.Issuer = opts.Issuer;
            o.SigningKeyPath = opts.SigningKeyPath;
            o.CurrentKid = opts.CurrentKid;
            o.SigningKeys = opts.SigningKeys;
            o.SecretEncryptionKeyPath = opts.SecretEncryptionKeyPath;
            o.IsProduction = opts.IsProduction;
        });

        // 实体 DAC（真实 FreeSqlEntityDAC）
        TrustCenterTestHost.RegisterEntityDacs(services);
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

    /// <summary>生产路径：accesscode 全链——签发 → 消费 → 重放拒（原子 CAS）。</summary>
    [Fact]
    public async Task Use_IAccessCodeService_IssueConsumeReplay()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var accessCodeService = user.Use<IAccessCodeService>();
        var issued = await accessCodeService.IssueAsync(
            new AccessCodeIssueRequest("mp-1", "u-100", "app-1", "profile:basic", OpenId: "openid-xxx"), CancellationToken.None);

        var consumed = await accessCodeService.ConsumeAsync(issued.Code, default, "1.2.3.4", CancellationToken.None);
        Assert.Equal("u-100", consumed.UId);
        Assert.Equal("app-1", consumed.TargetAppId);

        await Assert.ThrowsAsync<System.Security.Authentication.AuthenticationException>(
            () => accessCodeService.ConsumeAsync(issued.Code, default, null, CancellationToken.None));
    }
}
