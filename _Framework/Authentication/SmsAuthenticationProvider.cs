using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 短信验证码认证 Provider（内置，主认证路径——教育线/ DMP 通用，方案 §5.5）。
/// <para>流程：VerifyCodeAsync 校验（SmsVerificationService）→ 查账号（手机号主键）→ 有账号登录 / 无账号建账号（AuthAccount.Phone + UId）。</para>
/// <para>失败语义：返回 ProviderAuthenticateResult(Success=false, FailReason=异常消息)——调用方（装配层）负责 RecordAttemptAsync 落库。</para>
/// </summary>
internal sealed class SmsAuthenticationProvider : IAuthenticationProvider
{
    private readonly ISmsVerificationService _smsVerification;
    private readonly AuthAccountEntityDataService _accountDataService;
    private readonly ILogger<SmsAuthenticationProvider> _logger;

    public SmsAuthenticationProvider(
        ISmsVerificationService smsVerification,
        AuthAccountEntityDataService accountDataService,
        ILogger<SmsAuthenticationProvider> logger)
    {
        _smsVerification = smsVerification;
        _accountDataService = accountDataService;
        _logger = logger;
    }

    public string AuthType => AuthTypes.Sms;

    public async Task<ProviderAuthenticateResult> AuthenticateAsync(ProviderAuthenticateContext context, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(context.Phone) || string.IsNullOrWhiteSpace(context.Code))
            return new ProviderAuthenticateResult(false, null, "SMS_REQUIRED");

        try
        {
            await _smsVerification.VerifyCodeAsync(context.Phone, context.Code, SmsScenes.Login, ct);
        }
        catch (AuthenticationException ex)
        {
            return new ProviderAuthenticateResult(false, null, ex.Message);
        }

        // 有账号 → 登录；无账号 → 建账号（手机号主键——方案 §5.5 登录/注册合一）
        var account = await _accountDataService.GetByPhoneAsync(context.Phone, ct);
        if (account != null)
        {
            if (!account.IsEnabled) return new ProviderAuthenticateResult(false, null, "ACCOUNT_DISABLED");
            return new ProviderAuthenticateResult(true, account.UId, null, AuthLevel.Phone);
        }

        var created = new AuthAccountEntity
        {
            UId = AccountIdGenerator.NewUId(),
            Phone = context.Phone,
            AuthLevel = (int)AuthLevel.Phone,
            TokenVersion = 0
        };
        try
        {
            await _accountDataService.CreateAsync(created, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 并发注册同手机号——唯一约束兜底，重查
            _logger.LogDebug(ex, "短信登录建账号并发冲突——重查（UX_AuthAccount_Phone 唯一约束兜底）");
            var existing = await _accountDataService.GetByPhoneAsync(context.Phone, ct);
            if (existing != null) return new ProviderAuthenticateResult(true, existing.UId, null, AuthLevel.Phone);
            return new ProviderAuthenticateResult(false, null, "ACCOUNT_CREATE_FAILED");
        }
        return new ProviderAuthenticateResult(true, created.UId, null, AuthLevel.Phone);
    }
}
