using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Enumerations;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 认证中心登录衔接基类（方案 §5.8 路径 A——消费方继承，样板降至仅两个工厂方法）。
/// <para>预实现四个 <see cref="DomainUserHelperBase{TUserInfo}"/> 子虚钩子：</para>
/// <list type="bullet">
/// <item><see cref="OnLoginBySmsCodeAsync"/>——短信验证码校验（ISmsVerificationService）→ 查账号（手机号主键）→ <see cref="CreateUserInfoFromAccount"/> 工厂；</item>
/// <item><see cref="OnLoginByWeChatScanAsync"/>/<see cref="OnLoginByWeChatAppletAsync"/>/<see cref="OnLoginByOAuthAsync"/>——凭证为认证中心签发的 JWT（一次性票据已换令牌）→ ITokenVerifier 验签 → IAuthorizationMapper.MapRoles → <see cref="CreateUserInfoFromToken"/> 工厂。</item>
/// </list>
/// <para>服务经 <c>user.GetService&lt;T&gt;()</c> 运行时解析（UserHelper 宿主级、服务 Scoped——IdentityUserHelperBase 先例）。
/// <b>AuthorityFilter 零改动</b>：角色填充（Roles）→ 框架 IsInRole / Permissions 角色级判定自动工作。</para>
/// </summary>
/// <typeparam name="TUserInfo">消费方用户类型。</typeparam>
public abstract class AuthenticationUserHelperBase<TUserInfo> : DomainUserHelperBase<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>唯一 abstract 工厂 1：平台账号 + 本地角色 → TUserInfo（消费方实现；短信登录路径）。
    /// 典型实现：<c>new TUserInfo(account.UId, account.Phone ?? account.UId) { Roles = roles.ToList() }</c>。</summary>
    protected abstract TUserInfo CreateUserInfoFromAccount(AuthAccountEntity account, IReadOnlyList<string> roles);

    /// <summary>唯一 abstract 工厂 2：令牌载荷 + 本地角色 → TUserInfo（消费方实现；微信/OAuth 恢复路径）。
    /// 典型实现：<c>new TUserInfo(token.UserId, token.UserId) { Roles = roles.ToList() }</c>。</summary>
    protected abstract TUserInfo CreateUserInfoFromToken(TokenValidationResult token, IReadOnlyList<string> roles);

    /// <summary>预实现短信验证码登录（userName=手机号，credential=验证码）。</summary>
    protected override async Task<TUserInfo> OnLoginBySmsCodeAsync(
        DomainUser<TUserInfo> user, string userName, string credential, string authInfo)
    {
        var smsVerification = user.GetService<ISmsVerificationService>();
        var accountDataService = user.GetService<AuthAccountEntityDataService>();

        var verified = await smsVerification.VerifyCodeAsync(userName, credential, SmsScenes.Login);
        if (!verified) throw new AuthenticationException("SMS_CODE_MISMATCH");

        var account = await accountDataService.GetByPhoneAsync(userName);
        if (account == null) throw new AuthenticationException("ACCOUNT_NOT_FOUND");
        if (!account.IsEnabled) throw new AuthenticationException("ACCOUNT_DISABLED");

        return CreateUserInfoFromAccount(account, []);
    }

    /// <summary>预实现微信扫码登录（credential = 认证中心签发 JWT——票据已换令牌路径）。</summary>
    protected override Task<TUserInfo> OnLoginByWeChatScanAsync(
        DomainUser<TUserInfo> user, string userName, string credential, string authInfo)
        => RestoreFromTokenAsync(user, credential);

    /// <summary>预实现微信小程序登录（同上）。</summary>
    protected override Task<TUserInfo> OnLoginByWeChatAppletAsync(
        DomainUser<TUserInfo> user, string userName, string credential, string authInfo)
        => RestoreFromTokenAsync(user, credential);

    /// <summary>预实现 OAuth 登录（同上）。</summary>
    protected override Task<TUserInfo> OnLoginByOAuthAsync(
        DomainUser<TUserInfo> user, string userName, string credential, string authInfo)
        => RestoreFromTokenAsync(user, credential);

    /// <summary>验签恢复身份：ITokenVerifier.VerifyAsync → IAuthorizationMapper.MapRoles → CreateUserInfoFromToken 工厂。</summary>
    private async Task<TUserInfo> RestoreFromTokenAsync(DomainUser<TUserInfo> user, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new AuthenticationException("TOKEN_MALFORMED");

        // V4.10.57（fcbffd1 框架组判定受理）：ITokenVerifier 守卫工厂（AddConstructibleService）需帧内供给——
        // 原 GetService（DomainUser.GetService 直通 GetRequiredService 不设 CurrentAopUser）帧外抛守卫 → 票据换令牌 500。
        // 改 user.Use<ITokenVerifier>()（AOP 帧内：设 CurrentAopUser=user → 守卫工厂 ActivatorUtilities 供给）。
        var verifier = user.Use<ITokenVerifier>();
        var tokenResult = await verifier.VerifyAsync(token);

        var mapper = user.GetService<IAuthorizationMapper<TUserInfo>>();
        IReadOnlyList<string> roles = [];
        if (mapper != null)
            roles = await mapper.MapRolesAsync(tokenResult.UserId, tokenResult);

        return CreateUserInfoFromToken(tokenResult, roles);
    }
}
