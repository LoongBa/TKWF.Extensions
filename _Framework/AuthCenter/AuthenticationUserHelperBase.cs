using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Enumerations;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 认证中心登录衔接基类（方案 §5.8 路径 A——消费方继承，样板降至仅两个工厂方法）。
/// <para>预实现五个 <see cref="DomainUserHelperBase{TUserInfo}"/> 子虚钩子：</para>
/// <list type="bullet">
/// <item><see cref="OnLoginBySmsCodeAsync"/>——短信验证码校验（ISmsVerificationService）→ 查账号（手机号主键）→ <see cref="CreateUserInfoFromAccount"/> 工厂；</item>
/// <item><see cref="OnLoginByWeChatScanAsync"/>/<see cref="OnLoginByWeChatAppletAsync"/>/<see cref="OnLoginByOAuthAsync"/>——凭证为认证中心签发的 JWT（一次性票据已换令牌）→ ITokenVerifier 验签 → IAuthorizationMapper.MapRoles → <see cref="CreateUserInfoFromToken"/> 工厂；</item>
/// <item><see cref="OnLoginByFederatedAsync"/>——外部身份联邦直绑（D4 钩子，外部担保已可信）→ IAuthAccountQueryService 查账号 → IAuthorizationMapper.MapRoles（合成 TokenValidationResult）→ <see cref="CreateUserInfoFromAccount"/> 工厂。</item>
/// </list>
/// <para>服务经 <c>user.GetService&lt;T&gt;()</c> 运行时解析（UserHelper 宿主级、服务 Scoped——IdentityUserHelperBase 先例）。
/// <b>AuthorityFilter 零改动</b>：角色填充（Roles）→ 框架 IsInRole / Permissions 角色级判定自动工作。</para>
/// </summary>
/// <typeparam name="TUserInfo">消费方用户类型。</typeparam>
public abstract class AuthenticationUserHelperBase<TUserInfo> : DomainUserHelperBase<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>唯一 abstract 工厂 1：平台账号 + 本地角色 → TUserInfo（消费方实现；短信登录路径）。
    /// 典型实现：<c>new TUserInfo(account.UId, account.UId) { Roles = roles.ToList() }</c>（V0.11.0：手机号不再落账号列）。</summary>
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

        // V0.11.0（方案 §6.5/T7）：手机号经 AuthFactor 反查 UId（账号不再落 Phone 列）。
        var factor = await user.Use<IAuthFactorService>().GetByPhoneAsync(userName);
        var account = factor is null ? null : await accountDataService.GetByUIdAsync(factor.UId);
        if (account == null) throw new AuthenticationException("ACCOUNT_NOT_FOUND");
        if (!account.IsEnabled) throw new AuthenticationException("ACCOUNT_DISABLED");
        // V0.11.0（方案 §6.4/T3）：冻结判定经 IFreezeStateProvider（多原因冻结行集合）——守卫工厂须 user.Use（AOP 帧内）。
        if (await user.Use<IFreezeStateProvider>().IsFrozenAsync(account.UId)) throw new AuthenticationException("ACCOUNT_FROZEN");

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

    /// <summary>预实现外部身份联邦登录（D4 钩子——FederatedIdentity 外部担保直绑路径，方案 §五 E1 + ADR-Federation E1）。
    /// <para>流程：FederatedIdentity → <c>IAuthAccountQueryService.GetByUIdAsync</c> → 账号存在/启用/冻结检查 →
    /// <c>IAuthorizationMapper</c> 角色映射（<b>合成 <see cref="TokenValidationResult"/></b>——联邦路径无 JWT，零契约变更）→
    /// <see cref="CreateUserInfoFromAccount"/> 工厂。</para></summary>
    protected override async Task<TUserInfo> OnLoginByFederatedAsync(
        DomainUser<TUserInfo> user, FederatedIdentity identity, EnumLoginFrom loginFrom)
    {
        // ⚠️ IAuthAccountQueryService 为 AddConstructibleService 守卫工厂——必须 user.Use<>()（AOP 帧内供给
        //     CurrentAopUser → ActivatorUtilities 直建 AuthAccountQueryService）；不可 user.GetService<>()
        //     （帧外抛守卫——L37-38 遗留旧形态勿照抄）。
        var account = await user.Use<IAuthAccountQueryService>().GetByUIdAsync(identity.UId);
        if (account is null) throw new AuthenticationException("ACCOUNT_NOT_FOUND");
        if (!account.IsEnabled) throw new AuthenticationException("ACCOUNT_DISABLED");
        if (await user.Use<IFreezeStateProvider>().IsFrozenAsync(account.UId)) throw new AuthenticationException("ACCOUNT_FROZEN");   // V0.11.0 冻结判定（多原因冻结行集合——与短信钩子对齐）

        // 角色映射（关键裁定——合成 TokenValidationResult）：IAuthorizationMapper<TUserInfo>.MapRolesAsync(sub, token, ct)
        // 签名强制非可空 TokenValidationResult，联邦路径无 JWT——合成令牌（AuthType=federated + ChannelType 透传），零契约变更。
        // IAuthorizationMapper 为 TryAddScoped 普通 DI（非守卫工厂）——GetService 判空合法（L77-80 RestoreFromTokenAsync 先例）。
        var mapper = user.GetService<IAuthorizationMapper<TUserInfo>>();
        IReadOnlyList<string> roles = [];
        if (mapper != null)
        {
            var syntheticToken = new TokenValidationResult(
                UserId: identity.UId,
                AuthType: AuthTypes.Federated,
                AuthLevel: identity.AuthLevel,
                Jti: "",
                ExpiresAtUtc: DateTime.UtcNow,
                Claims: new Dictionary<string, string>(),
                ChannelType: identity.ChannelType);
            roles = await mapper.MapRolesAsync(identity.UId, syntheticToken);
        }

        return CreateUserInfoFromAccount(account, roles);
    }

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
