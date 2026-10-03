using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 短信验证码认证 Provider（内置，主认证路径——教育线/ DMP 通用，方案 §5.5）。
/// <para>流程：VerifyCodeAsync 校验（SmsVerificationService）→ 查账号（手机号主键）→ 有账号登录 / 无账号建账号（AuthAccount.Phone + UId）。</para>
/// <para>失败语义：返回 ProviderAuthenticateResult(Success=false, FailReason=异常消息)——调用方（装配层）负责 RecordAttemptAsync 落库。</para>
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
    private readonly ILogger<SmsAuthenticationProvider> _logger;

    private ISmsVerificationService SmsVerification => _smsVerification ??= User.Use<ISmsVerificationService>();
    private AuthAccountEntityDataService AccountDataService => _accountDataService ??= User.Use<AuthAccountEntityDataService>();

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

        // 有账号 → 登录；无账号 → 建账号（手机号主键——方案 §5.5 登录/注册合一）
        var account = await AccountDataService.GetByPhoneAsync(context.Phone, ct);
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
            await AccountDataService.CreateAsync(created, ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // 并发注册同手机号——唯一约束兜底，重查
            _logger.LogDebug(ex, "短信登录建账号并发冲突——重查（UX_AuthAccount_Phone 唯一约束兜底）");
            var existing = await AccountDataService.GetByPhoneAsync(context.Phone, ct);
            if (existing != null) return new ProviderAuthenticateResult(true, existing.UId, null, AuthLevel.Phone);
            return new ProviderAuthenticateResult(false, null, "ACCOUNT_CREATE_FAILED");
        }
        return new ProviderAuthenticateResult(true, created.UId, null, AuthLevel.Phone);
    }
}
