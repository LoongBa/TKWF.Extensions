using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 短信验证码认证 Provider（内置，主认证路径——教育线/ DMP 通用，方案 §5.5）。
/// <para>流程：VerifyCodeAsync 校验（SmsVerificationService）→ 经 Sms 因素反查账号 → 有账号登录 / 无账号建账号并绑定 Sms 因素。</para>
/// <para>失败语义：返回 ProviderAuthenticateResult(Success=false, FailReason=异常消息)——调用方（装配层）负责 RecordAttemptAsync 落库。</para>
/// <para>V0.11.0（方案 §6.5 T6/T7）：建号停写 <c>Phone</c>/<c>AuthLevel</c>，手机号唯一性由 <c>AuthFactor(Sms)</c> 承载——
/// 账号定位经 <see cref="IAuthFactorService.GetByPhoneAsync"/>，不再查账号列。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；内部 <see cref="ISmsVerificationService"/> / DataService 经 <c>User.Use&lt;T&gt;()</c> 懒加载。
/// 注册保持 <c>TryAddEnumerable(Scoped&lt;IAuthenticationProvider, SmsAuthenticationProvider&gt;)</c>——多实现集合，
/// AddConstructibleService 单实现不适用。<c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class SmsAuthenticationProvider : DomainServiceBase, IAuthenticationProvider
{
    private ISmsVerificationService? _smsVerification;
    private AuthAccountEntityDataService? _accountDataService;
    private IAuthFactorService? _factors;
    private readonly ILogger<SmsAuthenticationProvider> _logger;

    // V0.11.0（方案 §6.4/T3）：冻结判定经 IFreezeStateProvider（多原因冻结行集合）。
    private IFreezeStateProvider? _freezeState;
    private IFreezeStateProvider FreezeState => _freezeState ??= User.Use<IFreezeStateProvider>();

    private ISmsVerificationService SmsVerification => _smsVerification ??= User.Use<ISmsVerificationService>();
    private AuthAccountEntityDataService AccountDataService => _accountDataService ??= User.Use<AuthAccountEntityDataService>();
    private IAuthFactorService Factors => _factors ??= User.Use<IAuthFactorService>();

    public SmsAuthenticationProvider(IDomainUser user, ILogger<SmsAuthenticationProvider> logger) : base(user)
    {
        _logger = logger;
    }

    public string AuthType => AuthTypes.Sms;

    public async Task<ProviderAuthenticateResult> AuthenticateAsync(ProviderAuthenticateContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.Phone) || string.IsNullOrWhiteSpace(context.Code))
            return new ProviderAuthenticateResult(false, null, "SMS_REQUIRED");

        try
        {
            await SmsVerification.VerifyCodeAsync(context.Phone, context.Code, SmsScenes.Login, ct);
        }
        catch (AuthenticationException ex)
        {
            return new ProviderAuthenticateResult(false, null, ex.Message);
        }

        // 有账号 → 登录；无账号 → 建账号 + 绑定 Sms 因素（方案 §5.5 登录/注册合一 + §6.5 T6）
        var account = await FindAccountByPhoneAsync(context.Phone, ct);
        if (account != null)
        {
            if (!account.IsEnabled) return new ProviderAuthenticateResult(false, null, "ACCOUNT_DISABLED");
            if (await FreezeState.IsFrozenAsync(account.UId, ct: ct)) return new ProviderAuthenticateResult(false, null, "ACCOUNT_FROZEN");
            return new ProviderAuthenticateResult(true, account.UId, null, AuthLevel.Phone);
        }

        var created = new AuthAccountEntity
        {
            UId = AccountIdGenerator.NewUId(),
            TokenVersion = 0
        };
        await AccountDataService.CreateAsync(created, ct);
        try
        {
            await Factors.BindAsync(created.UId, AuthFactorTypes.Sms, context.Phone,
                source: AuthFactorSources.Provisioning, ct: ct);
        }
        catch (AuthenticationException ex) when (ex.Message == IAuthFactorService.FACTOR_ALREADY_BOUND)
        {
            _logger.LogDebug(ex, "短信登录建账号并发冲突——回滚空军账号并重查（AuthFactor 唯一约束兜底）");
            var orphan = await AccountDataService.GetByUIdAsync(created.UId, ct);
            if (orphan != null) await AccountDataService.EntityDeleteBatchAsync(new[] { orphan.Id }, ct);
            var existing = await FindAccountByPhoneAsync(context.Phone, ct);
            if (existing != null) return new ProviderAuthenticateResult(true, existing.UId, null, AuthLevel.Phone);
            return new ProviderAuthenticateResult(false, null, "ACCOUNT_CREATE_FAILED");
        }
        return new ProviderAuthenticateResult(true, created.UId, null, AuthLevel.Phone);
    }

    private async Task<AuthAccountEntity?> FindAccountByPhoneAsync(string phone, CancellationToken ct)
    {
        var factor = await Factors.GetByPhoneAsync(phone, ct);
        return factor is null ? null : await AccountDataService.GetByUIdAsync(factor.UId, ct);
    }
}
