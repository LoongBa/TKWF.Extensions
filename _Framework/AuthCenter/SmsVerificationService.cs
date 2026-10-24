using System;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 短信验证码服务——发送（6 位码 + TTL 5min + 频控 60s/小时/天/IP）+ 校验（单次消费 + 频控 5 次/小时）（方案 §5.5）。
/// <para>验证码单向散列（SHA256 hex）落库（<see cref="SmsRecordEntity"/>），不存明文；校验成功 IsVerified=true 单次消费。</para>
/// <para>生产未装配 <see cref="ISmsSender"/> → 捕获 <see cref="SmsMockForbiddenException"/> 转
/// <see cref="ServiceNotConfiguredException"/>（503，防 dev_code 泄露，Oracle D7）；开发模式告警 + 日志输出验证码（dev mock，不发真实短信）。</para>
/// <para>B3b（MapGenerated 生成化）：频控失败抛 <see cref="RateLimitedException"/>（429，ErrorCode = 原频控码）；
/// 业务失败抛 <see cref="DomainException"/>（400，ErrorCode = 原业务码）；`VerifyCodeAsync` 返回
/// <see cref="SmsVerifyResponse"/>（单次消费语义）。</para>
/// <para>数据访问红线合规：仅依赖 <see cref="SmsRecordEntityDataService"/>（SG1 DataService），不注入 IFreeSql/IEntityDAC。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册改 <c>AddConstructibleService&lt;ISmsVerificationService, SmsVerificationService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
[GenerateController]
public sealed class SmsVerificationService : DomainServiceBase, ISmsVerificationService
{
    /// <summary>验证码位数（6 位）。</summary>
    private const int CodeLength = 6;

    /// <summary>验证码有效期（分钟，TTL 5min）。</summary>
    private const int CodeTtlMinutes = 5;

    private readonly ISmsSender? _sender;
    private readonly IOptions<AuthCenterOptions> _options;
    private SmsRecordEntityDataService? _dataService;
    private readonly ILogger<SmsVerificationService> _logger;

    private SmsRecordEntityDataService DataService => _dataService ??= User.Use<SmsRecordEntityDataService>();

    public SmsVerificationService(
        IDomainUser user,
        ISmsSender? sender,
        IOptions<AuthCenterOptions> options,
        ILogger<SmsVerificationService> logger)
        : base(user)
    {
        _sender = sender;
        _options = options;
        _logger = logger;
    }

    /// <inheritdoc />
    public async Task SendCodeAsync(SendSmsCodeRequest request, CancellationToken ct = default)
    {
        var (phone, scene) = (request.Phone, request.Scene);
        try
        {
            var now = DateTime.UtcNow;
            var protection = _options.Value.LoginProtection;

            // (a) 频控检查——重发间隔 / 小时 / 天（SmsRecord 为唯一频控数据源，含已消费与过期记录）——命中抛 429
            var latest = await DataService.GetLatestUnverifiedAsync(phone, scene, ct);
            if (latest is not null
                && now.Subtract(latest.CreateTime).TotalSeconds < protection.SmsResendIntervalSeconds)
                throw new RateLimitedException(0, $"sms:{phone}:{scene}") { ErrorCode = "SMS_RESEND_TOO_FREQUENT" };

            if (await DataService.CountSentAsync(phone, scene, now.AddHours(-1), ct) >= protection.SmsHourlyLimitPerPhone)
                throw new RateLimitedException(0, $"sms:{phone}:{scene}") { ErrorCode = "SMS_HOURLY_LIMIT" };

            if (await DataService.CountSentAsync(phone, scene, now.AddDays(-1), ct) >= protection.SmsDailyLimitPerPhone)
                throw new RateLimitedException(0, $"sms:{phone}:{scene}") { ErrorCode = "SMS_DAILY_LIMIT" };

            // (b) 生成 6 位随机码——RandomNumberGenerator（加密安全，禁用 System.Random）
            var code = RandomNumberGenerator.GetInt32((int)Math.Pow(10, CodeLength)).ToString("D6");

            // (c) 发送器解析——未装配时生产抛 503 语义异常（下方捕获转 ServiceNotConfiguredException），开发仅告警 + 日志输出码（dev mock）
            var sender = _sender;
            if (sender is null)
            {
                if (_options.Value.IsProduction)
                    throw new SmsMockForbiddenException("SMS_SENDER_NOT_CONFIGURED");
                _logger.LogWarning(
                    "ISmsSender 未装配（dev mock）——短信验证码 Phone={Phone} Scene={Scene} Code={Code}",
                    phone, scene, code);
            }

            // (d) 落库（CodeHash=SHA256 hex，ExpireAt=now+5min；IP 无环境上下文——扩展服务无 AmbientContext，
            //     保持 null，调用方可经 AuthLoginAttempt.IpAddress 承载 IP 审计维度）
            var record = new SmsRecordEntity
            {
                Phone = phone,
                Scene = scene,
                CodeHash = HashCode(code),
                ExpireAt = now.AddMinutes(CodeTtlMinutes),
                CreateTime = now
            };
            await DataService.CreateAsync(record, ct);

            // (e) 发送——发送器失败向上传播（调用方 503 语义）；记录不标记 verified，未消费自然过期作废
            if (sender is not null)
                await sender.SendAsync(
                    new SmsMessage(phone, $"您的验证码是 {code}，{CodeTtlMinutes} 分钟内有效"),
                    ct);
        }
        catch (SmsMockForbiddenException ex)
        {
            // 503 语义（Oracle D7——防 dev_code 泄露）：SmsMockForbiddenException 子类 AuthenticationException 会误映射 401——
            // 显式转 ServiceNotConfiguredException（中间件 → 503）
            throw new ServiceNotConfiguredException("sms-sender", ex.Message, ex) { ErrorCode = "SMS_SENDER_NOT_CONFIGURED" };
        }
    }

    /// <inheritdoc />
    public async Task<SmsVerifyResponse> VerifyCodeAsync(SmsVerifyRequest request, CancellationToken ct = default)
    {
        var (phone, code, scene) = (request.Phone, request.Code, request.Scene);
        var now = DateTime.UtcNow;
        var protection = _options.Value.LoginProtection;

        // (a) 校验频控——窗口内活动验证码数（已发送未过期记录）≥ SmsVerifyAttemptsPerHour → 拒绝 429
        if (await DataService.CountVerifyAttemptsAsync(phone, scene, now.AddHours(-1), ct)
            >= protection.SmsVerifyAttemptsPerHour)
            throw new RateLimitedException(0, $"sms:{phone}:{scene}") { ErrorCode = "SMS_VERIFY_ATTEMPT_LIMIT" };

        // (b) 取最新未消费记录并校验（不存在 / 过期 / 散列不匹配）——业务失败抛 DomainException 400
        var record = await DataService.GetLatestUnverifiedAsync(phone, scene, ct);
        if (record is null)
            throw new DomainException("SMS_CODE_NOT_FOUND") { ErrorCode = "SMS_CODE_NOT_FOUND" };
        if (record.ExpireAt < now)
            throw new DomainException("SMS_CODE_EXPIRED") { ErrorCode = "SMS_CODE_EXPIRED" };
        if (!string.Equals(record.CodeHash, HashCode(code), StringComparison.Ordinal))
            throw new DomainException("SMS_CODE_MISMATCH") { ErrorCode = "SMS_CODE_MISMATCH" };

        // (c) 成功——单次消费（IsVerified=true）
        return new SmsVerifyResponse(await DataService.MarkVerifiedAsync(record.Id, ct));
    }

    /// <summary>验证码 SHA256 hex（单向散列落库，不存明文）。</summary>
    private static string HashCode(string code)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
