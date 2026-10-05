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
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 短信验证码服务——发送（6 位码 + TTL 5min + 频控 60s/小时/天/IP）+ 校验（单次消费 + 频控 5 次/小时）（方案 §5.5）。
/// <para>验证码单向散列（SHA256 hex）落库（<see cref="SmsRecordEntity"/>），不存明文；校验成功 IsVerified=true 单次消费。</para>
/// <para>生产未装配 <see cref="ISmsSender"/> → <see cref="SmsMockForbiddenException"/>（503 语义，防 dev_code 泄露，Oracle D7）；
/// 开发模式告警 + 日志输出验证码（dev mock，不发真实短信）。</para>
/// <para>数据访问红线合规：仅依赖 <see cref="SmsRecordEntityDataService"/>（SG1 DataService），不注入 IFreeSql/IEntityDAC。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文
/// （IDomainUser 永不注册 DI）；DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；
/// 注册改 <c>AddConstructibleService&lt;ISmsVerificationService, SmsVerificationService&gt;</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// </summary>
[DiContractIgnore]
internal sealed class SmsVerificationService : DomainServiceBase, ISmsVerificationService
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
    public async Task SendCodeAsync(string phone, string scene, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var protection = _options.Value.LoginProtection;

        // (a) 频控检查——重发间隔 / 小时 / 天（SmsRecord 为唯一频控数据源，含已消费与过期记录）
        // 重发间隔：最新未消费记录 CreateTime 距今 < SmsResendIntervalSeconds → 拒绝
        var latest = await DataService.GetLatestUnverifiedAsync(phone, scene, ct);
        if (latest is not null
            && now.Subtract(latest.CreateTime).TotalSeconds < protection.SmsResendIntervalSeconds)
            throw new AuthenticationException("SMS_RESEND_TOO_FREQUENT");

        if (await DataService.CountSentAsync(phone, scene, now.AddHours(-1), ct) >= protection.SmsHourlyLimitPerPhone)
            throw new AuthenticationException("SMS_HOURLY_LIMIT");

        if (await DataService.CountSentAsync(phone, scene, now.AddDays(-1), ct) >= protection.SmsDailyLimitPerPhone)
            throw new AuthenticationException("SMS_DAILY_LIMIT");

        // (b) 生成 6 位随机码——RandomNumberGenerator（加密安全，禁用 System.Random）
        var code = RandomNumberGenerator.GetInt32((int)Math.Pow(10, CodeLength)).ToString("D6");

        // (c) 发送器解析——未装配时生产抛 503 语义异常，开发仅告警 + 日志输出码（dev mock）
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

    /// <inheritdoc />
    public async Task<bool> VerifyCodeAsync(string phone, string code, string scene, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        var protection = _options.Value.LoginProtection;

        // (a) 校验频控——窗口内活动验证码数（已发送未过期记录）≥ SmsVerifyAttemptsPerHour → 拒绝
        //     （设计决策：不落独立"尝试"行；复用 SmsRecord 活动码行计数，SendCodeAsync 每码一行即一次尝试机会）
        if (await DataService.CountVerifyAttemptsAsync(phone, scene, now.AddHours(-1), ct)
            >= protection.SmsVerifyAttemptsPerHour)
            throw new AuthenticationException("SMS_VERIFY_ATTEMPT_LIMIT");

        // (b) 取最新未消费记录并校验（不存在 / 过期 / 散列不匹配）
        var record = await DataService.GetLatestUnverifiedAsync(phone, scene, ct);
        if (record is null)
            throw new AuthenticationException("SMS_CODE_NOT_FOUND");
        if (record.ExpireAt < now)
            throw new AuthenticationException("SMS_CODE_EXPIRED");
        if (!string.Equals(record.CodeHash, HashCode(code), StringComparison.Ordinal))
            throw new AuthenticationException("SMS_CODE_MISMATCH");

        // (c) 成功——单次消费（IsVerified=true）
        return await DataService.MarkVerifiedAsync(record.Id, ct);
    }

    /// <summary>验证码 SHA256 hex（单向散列落库，不存明文）。</summary>
    private static string HashCode(string code)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(code)));
}
