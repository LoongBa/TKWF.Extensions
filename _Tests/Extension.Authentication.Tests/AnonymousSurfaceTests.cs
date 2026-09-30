using System.Linq;
using System.Reflection;
using TKW.Framework.Domain.Interception.Filters;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>E1（EduPlatform 转达）：认证入口匿名面契约——9 处 [AllowAnonymousFlag] 声明锁 + 负向边界。
/// <para>机制：框架 AuthorityFilterAttribute.CanWeGo（L40）读 AllowAnonymousFlagAttribute → 跳过授权检查；
/// 对齐 IdentityAuthService（register/login）+ IAuthController 接口级先例。零声明即匿名入口 401（消费者冒烟实证）。</para></summary>
public class AnonymousSurfaceTests
{
    private static bool HasAnonymousFlag(MethodInfo m)
        => m.GetCustomAttributes(typeof(AllowAnonymousFlagAttribute), false).Length > 0;

    /// <summary>正向：9 处认证入口方法必须携带 [AllowAnonymousFlag]（EduPlatform 转达 10 方法清单——AuthenticateAsync 双 Provider 共用 1 声明）。</summary>
    [Fact]
    public void AnonymousEntryMethods_AllCarry_AllowAnonymousFlag()
    {
        // ISmsVerificationService：验证码发送/校验前置
        Assert.True(HasAnonymousFlag(typeof(ISmsVerificationService).GetMethod(nameof(ISmsVerificationService.SendCodeAsync))!));
        Assert.True(HasAnonymousFlag(typeof(ISmsVerificationService).GetMethod(nameof(ISmsVerificationService.VerifyCodeAsync))!));

        // IAuthenticationProvider：短信/微信登录 + OAuth 回调前置（双 Provider 共用同一声明）
        Assert.True(HasAnonymousFlag(typeof(IAuthenticationProvider).GetMethod(nameof(IAuthenticationProvider.AuthenticateAsync))!));

        // ITokenService：登录签发 / Refresh 续期 / 登出撤销（ValidateTokenAsync 例外——见负向）
        Assert.True(HasAnonymousFlag(typeof(ITokenService).GetMethod(nameof(ITokenService.IssueTokenAsync))!));
        Assert.True(HasAnonymousFlag(typeof(ITokenService).GetMethod(nameof(ITokenService.RefreshTokenAsync))!));
        Assert.True(HasAnonymousFlag(typeof(ITokenService).GetMethod(nameof(ITokenService.RevokeTokenAsync))!));

        // IOAuthTicketService：票据签发/换取前置
        Assert.True(HasAnonymousFlag(typeof(IOAuthTicketService).GetMethod(nameof(IOAuthTicketService.IssueAsync))!));
        Assert.True(HasAnonymousFlag(typeof(IOAuthTicketService).GetMethod(nameof(IOAuthTicketService.ExchangeAsync))!));

        // IPlatformCredentialService：微信 authorize URL 凭证读取（管理写方法例外——见负向）
        Assert.True(HasAnonymousFlag(typeof(IPlatformCredentialService).GetMethod(nameof(IPlatformCredentialService.GetSecretByAppIdAsync))!));
    }

    /// <summary>负向边界：非匿名面方法零声明——服务端通道/凭据校验/管理写面。</summary>
    [Fact]
    public void NonAnonymousMethods_MustNotCarry_AllowAnonymousFlag()
    {
        // ISmsSender：消费方实现的发送通道（服务端内部调用，不暴露为匿名端点）
        Assert.False(HasAnonymousFlag(typeof(ISmsSender).GetMethod(nameof(ISmsSender.SendAsync))!));

        // ITokenService.ValidateTokenAsync：Bearer 验签在认证/中间件上下文执行，非匿名端点
        Assert.False(HasAnonymousFlag(typeof(ITokenService).GetMethod(nameof(ITokenService.ValidateTokenAsync))!));

        // IPlatformCredentialService 管理写面：凭证创建/更新/启停必须保持鉴权（防凭据管理面匿名）
        Assert.False(HasAnonymousFlag(typeof(IPlatformCredentialService).GetMethod(nameof(IPlatformCredentialService.CreateAsync))!));
        Assert.False(HasAnonymousFlag(typeof(IPlatformCredentialService).GetMethod(nameof(IPlatformCredentialService.UpdateAsync))!));
        Assert.False(HasAnonymousFlag(typeof(IPlatformCredentialService).GetMethod(nameof(IPlatformCredentialService.SetEnabledAsync))!));
    }
}