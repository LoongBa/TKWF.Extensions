using System;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.AuthSurface;

/// <summary>
/// 兑换写入门面——创建兑换码（运营侧）/ 兑换（仅本人——userId 显式参数，装配端点从 DomainUser/令牌解析）。
/// <para>⚠️ 兑换前提（Oracle4 P1-1 裁定 A）：v0.1.0 兑换<b>必须已登录</b>（userId 必填），频控按 userId 维度；
/// 未登录用户无法兑换（先注册/登录）；匿名兑换（兑换创建账号）留 v0.2.0 评估。</para>
/// </summary>
public interface IRedemptionCommandService : IDomainService
{
    /// <summary>
    /// 创建兑换码（单码——v0.1.0 无批次管理端；返回<b>明文 code 一次性</b>——不落库不日志，库中仅存 SHA256 哈希 + 脱敏值）。
    /// <para>v0.2.0 核验场景：<paramref name="payload"/> 为<b>核验业务信息明文</b>（如购买人信息/权益明细 JSON——运营侧传入），
    /// 门面经 keyed <see cref="ISymmetricKeyProvider"/> AES-GCM 加密落 <c>PayloadEncrypted</c>——<b>明文不落库</b>；
    /// null = 无附加信息（兼容 v0.1.0 调用）。</para>
    /// <para>运营侧调用（装配层管理员上下文；v0.2.0 批次管理端/权限控制）。</para>
    /// </summary>
    Task<string> CreateCodeAsync(string productName, string targetAppId, TimeSpan? validity = null, CancellationToken ct = default, string? payload = null);

    /// <summary>
    /// 兑换（校验 → CAS 原子兑换 → 返回历史 DTO）；失败抛 <see cref="AuthenticationException"/>（错误码见 <see cref="RedemptionErrorCodes"/>）。
    /// <para>仅本人：userId 显式参数（装配层强制）。</para>
    /// <para>v0.2.0 核验场景：返回 DTO 含 <see cref="RedemptionRecordDto.Payload"/>（<b>明文取回</b>——门面解密，
    /// 供人工核验展示 / 自动核验匹配；库中仅存密文）。</para>
    /// </summary>
    Task<RedemptionRecordDto> RedeemAsync(string userId, string codeInput, CancellationToken ct = default);
}

/// <summary>
/// 兑换写入门面实现——委托 <see cref="RedemptionCodeEntityDataService"/>（红线合规，零 IFreeSql/IEntityDAC 直注入）。
/// <para>V0.1.0（ADR90 门面范式）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文；
/// DataService 经 <c>User.Use&lt;具体类&gt;()</c> NoAop 懒加载；注册 <c>AddConstructibleService</c>。
/// <c>[DiContractIgnore]</c>：运行时手写注册，豁免 SG1a DI001 误报。</para>
/// <para>v4.10.67 适配：兑换尝试频控经 <see cref="IRateLimitCheck"/> 点检查原语（<c>TKW.Framework.Utility.RateLimitChecks</c>——
/// R1 上移自 MFA MfaRateLimiter；R3 同步删 MfaRateLimiter——本扩展不复制旧形态）。</para>
/// <para>v0.2.0 核验场景：keyed <see cref="ISymmetricKeyProvider"/>（键 <see cref="AuthSurfaceKeyProviderKeys.AuthSurface"/>）——
/// 创建 <c>payload</c> 加密落 <c>PayloadEncrypted</c>（明文不落库）；兑换取回解密 <see cref="RedemptionRecordDto.Payload"/>。</para>
/// </summary>
[DiContractIgnore]
internal sealed class RedemptionCommandService : DomainServiceBase, IRedemptionCommandService
{
    private readonly AuthSurfaceOptions _options;
    private readonly IRateLimitCheck _rateLimitCheck;
    private readonly ISymmetricKeyProvider _keys;
    private readonly ILogger<RedemptionCommandService> _logger;

    private RedemptionCodeEntityDataService? _codeDataService;
    private RedemptionCodeEntityDataService CodeDataService => _codeDataService ??= User.Use<RedemptionCodeEntityDataService>();

    public RedemptionCommandService(
        IDomainUser user,
        IOptions<AuthSurfaceOptions> options,
        IRateLimitCheck rateLimitCheck,
        [FromKeyedServices(AuthSurfaceKeyProviderKeys.AuthSurface)] ISymmetricKeyProvider keys,
        ILogger<RedemptionCommandService> logger) : base(user)
    {
        _options = options?.Value ?? new AuthSurfaceOptions();
        _rateLimitCheck = rateLimitCheck ?? throw new ArgumentNullException(nameof(rateLimitCheck));
        _keys = keys ?? throw new ArgumentNullException(nameof(keys));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public async Task<string> CreateCodeAsync(string productName, string targetAppId, TimeSpan? validity = null, CancellationToken ct = default, string? payload = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productName);
        ArgumentException.ThrowIfNullOrWhiteSpace(targetAppId);

        var code = RedemptionCodeGenerator.Generate();
        var now = DateTime.UtcNow;
        var entity = new RedemptionCodeEntity
        {
            CodeHash = RedemptionCodeGenerator.Hash(code),
            CodeMasked = RedemptionCodeGenerator.Mask(code),
            ProductName = productName,
            TargetAppId = targetAppId,
            Status = 0,                       // Available
            ExpireAtUtc = validity is { } v ? now.Add(v) : null,
            PayloadEncrypted = string.IsNullOrWhiteSpace(payload) ? null : _keys.Encrypt(payload),
            CreateTime = now,
            UpdateTime = now,
        };
        await CodeDataService.EntityCreateAsync(entity, ct);
        return code;                          // 明文一次性——不落库不日志
    }

    /// <inheritdoc />
    public async Task<RedemptionRecordDto> RedeemAsync(string userId, string codeInput, CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ArgumentException.ThrowIfNullOrWhiteSpace(codeInput);

        // 频控 key 规范化（IRateLimitCheck XML doc 约定 {policy}:{partitionBy}:{subject}——v4.10.67 R1）
        var key = $"redemption:redeem:{userId}";
        var window = TimeSpan.FromMinutes(_options.RedemptionAttemptWindowMinutes);
        var maxAttempts = _options.RedemptionAttemptMaxAttempts;

        // 频控前置（只读拦截——GetRemaining 不计数，防有效兑换误计；窗口满 → 所有尝试被拒直到滑出，对齐 MFA 频控语义）
        if (_rateLimitCheck.GetRemaining(key, maxAttempts, window) <= 0)
            throw new AuthenticationException(RedemptionErrorCodes.TooManyAttempts);

        var hash = RedemptionCodeGenerator.Hash(codeInput);
        var code = await CodeDataService.GetByCodeHashAsync(hash, ct);

        // 无效码——TryAcquire 计入一次失败（防批量枚举爆破）；成功兑换不计数（不污染窗口）
        if (code is null)
        {
            _rateLimitCheck.TryAcquire(key, maxAttempts, window, out _);
            throw new AuthenticationException(RedemptionErrorCodes.CodeInvalid);
        }

        // 状态判定（已兑/过期——过期顺手惰性翻转 Status=2）
        if (code.Status == 1)
        {
            _rateLimitCheck.TryAcquire(key, maxAttempts, window, out _);
            throw new AuthenticationException(RedemptionErrorCodes.CodeUsed);
        }
        if (code.Status == 2 || code.ExpireAtUtc is { } exp && exp <= DateTime.UtcNow)
        {
            if (code.Status != 2) await CodeDataService.MarkExpiredAsync(code.Id, ct);
            _rateLimitCheck.TryAcquire(key, maxAttempts, window, out _);
            throw new AuthenticationException(RedemptionErrorCodes.CodeExpired);
        }

        // CAS 原子兑换（谓词含 Status=0 且未过期——过期/并发天然拒绝）
        var redeemedAt = DateTime.UtcNow;
        var ok = await CodeDataService.RedeemCodeAsync(code.Id, userId, redeemedAt, ct);
        if (!ok)
        {
            // CAS 败者——重查判定（并发双兑/刚过期）
            var fresh = await CodeDataService.GetByCodeHashAsync(hash, ct);
            _rateLimitCheck.TryAcquire(key, maxAttempts, window, out _);
            throw fresh is { Status: 1 }
                ? new AuthenticationException(RedemptionErrorCodes.CodeUsed)
                : new AuthenticationException(RedemptionErrorCodes.CodeExpired);
        }

        // 成功——不计数（有效兑换不污染频控窗口；"成功重置"由不计数天然近似替代）
        return new RedemptionRecordDto(
            CodeMasked: code.CodeMasked,       // 已脱敏存储——契约透传
            ProductName: code.ProductName,
            TargetAppId: code.TargetAppId,
            RedeemedAtUtc: redeemedAt,
            Status: "redeemed",                // 授权面域字符串（本扩展透传不枚举化）
            Payload: DecryptPayload(code));    // 核验业务信息明文取回（库中仅存密文——人工/自动核验）
    }

    /// <summary>
    /// 解密附加信息（核验业务信息）——库中 <c>PayloadEncrypted</c> AES-GCM 密文 → 明文；
    /// 无密文返回 null。密文被篡改/密钥不匹配 → <see cref="System.Security.Cryptography.CryptographicException"/>
    /// （GCM 认证失败——fail-closed 拒信任，不静默降级）。
    /// </summary>
    private string? DecryptPayload(RedemptionCodeEntity code)
        => string.IsNullOrEmpty(code.PayloadEncrypted) ? null : _keys.Decrypt(code.PayloadEncrypted);
}
