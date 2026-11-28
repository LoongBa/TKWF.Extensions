using System;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKW.Framework.Domain.Ticket;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;
using Xunit;

namespace TKWF.Ext.TrustCenter.Tests;

/// <summary>
/// AddSealedTicketFor&lt;FederatedTicketPayload&gt;() 生产路径 DI 回归网（代码审核 2026-11-23 P1 项 3）：
/// 已发布 Federation/v0.19.0 的运行期前置——TrustCenter 初始器只注册 FederatedIdentity，
/// FederatedTicketPayload 须由装配方经本扩展闭合注册。本测试锁定：
/// ① 帧内 User.Use&lt;ISealedTicketService&lt;FederatedTicketPayload&gt;&gt;() 可解析（守卫工厂 +
///    keyed ISymmetricKeyProvider + IOptions + ILogger 全链就绪）；
/// ② 初始器注册了 keyed ISymmetricKeyProvider（SymmetricKeyProviderKeys.TrustCenter）；
/// ③ 域作用域外直接 DI 解析门面抛 DomainGuardException（DI004 运行期兜底）。
/// </summary>
public class SealedTicketRegistrationTests : TestHostBase
{
    protected override TestUserInfo CreateUser() => new("user-42", "测试用户");

    protected override void SyncStructure(IFreeSql fsql)
    {
        fsql.CodeFirst.SyncStructure<SsoClientEntity>();
        fsql.CodeFirst.SyncStructure<SealedTicketEntity>();
        fsql.CodeFirst.SyncStructure<AuthorizationCodeEntity>();
    }

    protected override void ConfigureHost(IServiceCollection services)
        => ConfigureTrustCenter(services);

    /// <summary>TrustCenter 初始器 + AddSealedTicketFor + Options + 实体 DAC（生产路径等价装配）。</summary>
    private static void ConfigureTrustCenter(IServiceCollection services)
    {
        new TrustCenterExtensionInitializer<TestUserInfo>().ConfigureServices(services);

        // 被测入口：装配方逐 T 闭合注册（Federation/v0.19.0 运行期前置）
        services.AddSealedTicketFor<FederatedTicketPayload>();

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
        TrustCenterTestHost.RegisterEntityDacs(services);
    }

    [Fact]
    public void Use_SealedTicketService_InsideAopFrame_Resolves()
    {
        using var _ = this;
        var (_, user) = BindTestScope();

        var service = user.Use<ISealedTicketService<FederatedTicketPayload>>();

        Assert.NotNull(service);
    }

    [Fact]
    public void Initializer_Registers_KeyedSymmetricKeyProvider()
    {
        using var _ = this;
        var (provider, _) = BindTestScope();

        var keyProvider = provider.GetKeyedService<ISymmetricKeyProvider>(SymmetricKeyProviderKeys.TrustCenter);
        Assert.NotNull(keyProvider);
    }

    [Fact]
    public void SealedTicketServiceInterface_OutsideFrame_ThrowsDomainGuardException()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IFreeSql>(TrustCenterTestHost.CreateInMemoryFreeSql());
        services.AddSingleton<UnitOfWorkManager>();
        ConfigureTrustCenter(services);
        var provider = services.BuildServiceProvider();

        var ex = Assert.Throws<DomainGuardException>(
            () => provider.GetRequiredService<ISealedTicketService<FederatedTicketPayload>>());
        Assert.Contains("领域架构守卫", ex.Message);
    }
}
