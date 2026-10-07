using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.RateLimitChecks;
using TKWF.Ext.Emailing;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 找回密码服务实现（V0.9.0 B.10/B.11——AuthCenter 自建多通道链路，UId-keyed，不实现 IAccountPasswordManager）。
/// <para>SMS 通道：复用 <see cref="SmsScenes.Reset"/>（两端点白名单已含——零消费流承接）+ <see cref="ISmsVerificationService"/>
/// 单次消费校验——**自带投递**（B.11 修复 Account DefaultPasswordResetFlow 生成码不投递缺口）。</para>
/// <para>Email 通道：查 <see cref="UserProfileEntity.Email"/>（联系方式角色）→ Emailing.Abstractions <see cref="IEmailSender"/>
/// 可空依赖降级（<c>GetService</c> null → EMAIL_SENDER_NOT_CONFIGURED；未接 Emailing 时仅 Email 通道禁用，SMS/扫码不受影响）。</para>
/// <para>扫码通道：**身份持有证明前置**（B.10/C.13 层级：改密前证明持有微信）——走 OAuthTicket 链路（一次性 TTL+state），
/// 编排归装配层；本服务提供 <see cref="CompleteResetVerifiedAsync"/>（已证明后直接设密）。</para>
/// <para>凭证标识解析：<paramref name="identifier"/> 为 UId 或 Phone（凭据角色——与登录门面一致）；SMS 走 Phone 校验，
/// Email 需 UId 定位 UserProfile（Phone → UId 反向经 AuthAccount 查询）。</para>
/// <para>铁律：继承 <see cref="DomainServiceBase"/>（IDomainUser 永不注册 DI）；依赖经 <c>User.Use&lt;T&gt;()</c> 懒加载
/// （DI004）；注册 <c>AddConstructibleService&lt;IPasswordResetService, PasswordResetService&gt;</c>。
/// 可选依赖（ISmsSender/IEmailSender）经 <c>GetService</c> 降级（对应 ServiceProvider 可空解析）。</para>
/// </summary>
[DiContractIgnore]
internal sealed class PasswordResetService : DomainServiceBase, IPasswordResetService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IRateLimitCheck _rateLimit;
    private readonly ILogger<PasswordResetService> _logger;

    // DI004 铁律：领域服务间调用经 User.Use<T>() 懒加载
    private IAuthAccountQueryService? _accountQuery;
    private IAuthAccountQueryService AccountQuery => _accountQuery ??= User.Use<IAuthAccountQueryService>();
    private IAuthAccountService? _accountWrite;
    private IAuthAccountService AccountWrite => _accountWrite ??= User.Use<IAuthAccountService>();
    private ISmsVerificationService? _sms;
    private ISmsVerificationService Sms => _sms ??= User.Use<ISmsVerificationService>();
    private PasswordResetCodeEntityDataService? _codeDataService;
    private PasswordResetCodeEntityDataService CodeDataService => _codeDataService ??= User.Use<PasswordResetCodeEntityDataService>();

    public PasswordResetService(
        IDomainUser user,
        IServiceProvider serviceProvider,
        IRateLimitCheck rateLimit,
        ILogger<PasswordResetService> logger)
        : base(user)
    {
        _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        _rateLimit = rateLimit ?? throw new ArgumentNullException(nameof(rateLimit));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> InitiateSmsResetAsync(string identifier, CancellationToken ct = default)
    {
        // V0.9.0（ADR 决策 2——找回频控）：前置点检查（key 规范 pwd:reset:{identifier}）——TryAcquire 失败 = 锁定阻断
        if (!_rateLimit.TryAcquire($"pwd:reset:{identifier}", 5, TimeSpan.FromMinutes(10), out _))
            throw new AuthenticationException("PASSWORD_RESET_RATE_LIMITED");

        var phone = await ResolveSmsIdentifierAsync(identifier, ct);
        await Sms.SendCodeAsync(phone, SmsScenes.Reset, ct);   // 自带投递（B.11）——SmsScenes.Reset 现成通道
        _logger.LogDebug("找回密码发起（SMS）——identifier={Identifier}", MaskIdentifier(identifier));
        return "SMS_RESET_CODE_SENT";
    }

    /// <inheritdoc />
    public async Task<string> InitiateEmailResetAsync(string identifier, CancellationToken ct = default)
    {
        // V0.9.0（ADR 决策 2——找回频控）：前置点检查（key 规范 pwd:reset:{identifier}）
        if (!_rateLimit.TryAcquire($"pwd:reset:{identifier}", 5, TimeSpan.FromMinutes(10), out _))
            throw new AuthenticationException("PASSWORD_RESET_RATE_LIMITED");

        // 可空依赖降级（P2-3）：未接 Emailing → Email 通道禁用（SMS/扫码不受影响）
        var emailSender = _serviceProvider.GetService<IEmailSender>();
        if (emailSender is null)
        {
            _logger.LogWarning("Email 找回通道未配置——未接 Emailing.Abstractions IEmailSender（降级 EMAIL_SENDER_NOT_CONFIGURED）");
            return "EMAIL_SENDER_NOT_CONFIGURED";
        }

        var uid = await ResolveUIdAsync(identifier, ct);
        var profile = await AccountQuery.GetProfileByUIdAsync(uid, ct);
        var email = profile?.Email;
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogDebug("Email 找回失败——账号未绑定邮箱（identifier={Identifier}）", MaskIdentifier(identifier));
            return "EMAIL_NOT_BOUND";
        }

        // 生成重置码 + 落库 + Emailing 投递（AuthCenter 自建链路自带投递——修复 B.11 Account 侧缺口）
        var code = GenerateResetCode();
        var entity = new PasswordResetCodeEntity
        {
            UId = uid,
            Channel = "email",
            CodeHash = Sha256Hex(code),
            ExpireAt = DateTime.UtcNow.AddMinutes(ResetCodeValidityMinutes),
            IsConsumed = false,
        };
        await CodeDataService.CreateAsync(entity, ct);

        await emailSender.SendAsync(new EmailMessage
        {
            To = email,
            Subject = "找回密码验证码",
            Body = $"您的找回密码验证码：{code}（{ResetCodeValidityMinutes} 分钟内有效）",
        });
        _logger.LogDebug("Email 找回密码发起成功——uid={UId}", uid);
        return "EMAIL_RESET_CODE_SENT";
    }

    /// <inheritdoc />
    public async Task CompleteResetWithCodeAsync(string identifier, string resetCode, string newClientHash, string newSalt, CancellationToken ct = default)
    {
        var uid = await ResolveUIdAsync(identifier, ct);
        var record = await CodeDataService.GetLastUnconsumedAsync(uid, "email", ct)
                     ?? throw new AuthenticationException("RESET_CODE_NOT_FOUND");
        if (record.ExpireAt < DateTime.UtcNow)
            throw new AuthenticationException("RESET_CODE_EXPIRED");

        // 单向散列比对（CodeHash——SMS 通道经 ISmsVerificationService 校验单次消费；Email 通道本表校验）
        var ok = FixedTimeEqualsHex(Sha256Hex(resetCode), record.CodeHash);
        if (!ok) throw new AuthenticationException("RESET_CODE_INVALID");

        if (!await CodeDataService.TryConsumeAsync(record.Id, ct))   // CAS 单次消费（ADR89）——并发重放败者走已消费
            throw new AuthenticationException("RESET_CODE_CONSUMED");

        await AccountWrite.SetPasswordAsync(uid, newClientHash, newSalt, ct);   // SecurePassword 零明文 + TokenVersion++ + 历史（①）
        _logger.LogDebug("找回密码完成（验证码通道）——uid={UId}", uid);
    }

    /// <inheritdoc />
    public async Task CompleteResetSmsAsync(string identifier, string phone, string code, string newClientHash, string newSalt, CancellationToken ct = default)
    {
        // 门面内编排（P1-NEW-1 修订）：SMS 通道统一完成——校验验证码（单次消费）→ 设新密码，表现层零编排
        var uid = await ResolveUIdAsync(identifier, ct);
        var verified = await Sms.VerifyCodeAsync(phone, code, SmsScenes.Reset, ct);
        if (!verified) throw new AuthenticationException("SMS_CODE_MISMATCH");

        await AccountWrite.SetPasswordAsync(uid, newClientHash, newSalt, ct);   // SecurePassword 零明文 + TokenVersion++ + 历史（①）
        _logger.LogDebug("找回密码完成（SMS 通道）——uid={UId}", uid);
    }

    /// <inheritdoc />
    public async Task CompleteResetVerifiedAsync(string uid, string newClientHash, string newSalt, CancellationToken ct = default)
    {
        // 扫码前置已证明（装配层经 OAuthTicket 链路完成身份持有证明）——直接设密
        await AccountWrite.SetPasswordAsync(uid, newClientHash, newSalt, ct);   // SecurePassword 零明文 + TokenVersion++ + 历史（①）
        _logger.LogDebug("找回密码完成（扫码已验证通道）——uid={UId}", uid);
    }

    // ── 私有辅助 ──

    private const int ResetCodeValidityMinutes = 30;

    /// <summary>SMS 标识解析：UId 或 Phone → Phone（短信通道校验以 Phone 为准）。</summary>
    private async Task<string> ResolveSmsIdentifierAsync(string identifier, CancellationToken ct)
    {
        var account = await AccountQuery.GetByUIdAsync(identifier, ct)
                      ?? await AccountQuery.GetByPhoneAsync(identifier, ct);
        if (account is null) throw new AuthenticationException("ACCOUNT_NOT_FOUND");
        if (string.IsNullOrWhiteSpace(account.Phone)) throw new AuthenticationException("PHONE_NOT_BOUND");
        return account.Phone;
    }

    /// <summary>通用标识解析：UId 或 Phone → UId。</summary>
    private async Task<string> ResolveUIdAsync(string identifier, CancellationToken ct)
    {
        var account = await AccountQuery.GetByUIdAsync(identifier, ct)
                      ?? await AccountQuery.GetByPhoneAsync(identifier, ct);
        if (account is null) throw new AuthenticationException("ACCOUNT_NOT_FOUND");
        return account.UId;
    }

    private static string GenerateResetCode()
        // 8 位数字（加密安全 RNG——RandomNumberGenerator.GetInt32；P0-NEW-1 修复：弃 new Random()（非加密、种子可预测→账户接管风险））
        => System.Security.Cryptography.RandomNumberGenerator.GetInt32(10000000, 100000000).ToString();

    private static string Sha256Hex(string input)
    {
        using var sha = System.Security.Cryptography.SHA256.Create();
        var bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }

    private static bool FixedTimeEqualsHex(string a, string b)
        => a.Length == b.Length && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.ASCII.GetBytes(a), System.Text.Encoding.ASCII.GetBytes(b));

    private static string MaskIdentifier(string identifier)
        => identifier.Length > 4 ? identifier[..4] + "***" : "***";
}