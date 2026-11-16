using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.Captcha;

/// <summary>
/// 人机验证门面实现（方案 §5.3——编排者；Provider 只做"生成/校验"）。
/// <para>编排链：① <b>全局开关 fail-closed</b>（m7 评审闭环）——<c>TKWF:Captcha:Enabled=false</c> + 消费方场景
/// 要求门禁 = 配置冲突 → 恒抛守卫异常（禁静默放行——防运维误关导致短信预算裸奔）；② <b>取题 IP 频控</b>
/// （<c>captcha:issue:{ip}</c>，默认 10/min，IRateLimitCheck 点检查——Initializer fallback MemoryRateLimitCheck
/// "始终在"）；③ <b>Provider 选中</b>——Options <c>EnabledProviderType</c> 对 <see cref="ICaptchaProvider.Type"/>
/// 精确匹配，未匹配 → fail-closed 守卫异常；④ 委托 Provider（挑战态生命周期 TTL/尝试上限/单次消费归 Provider
/// 自持，见 <see cref="ImageCaptchaProvider"/>）。</para>
/// <para>注册：<c>AddConstructibleService&lt;ICaptchaService, CaptchaService&gt;()</c>——接口可构造守卫工厂 +
/// 实现类 throw-factory；消费方经 <c>User.Use&lt;ICaptchaService&gt;()</c> 帧内解析（匿名面经游客帧
/// <c>guest.Use</c>）。门面 ctor 注入 <c>IEnumerable&lt;ICaptchaProvider&gt;</c>——帧内枚举
/// （<c>TryAddEnumerableConstructible</c> 集合版守卫工厂经 CurrentAopUser 供给），禁帧外 [FromServices] 集合预绑定。</para>
/// <para>noexcept 链：Provider 异常原样向上传播（不 catch 不降级——失败即拒绝）；门面自身守卫/频控抛出前不吞。</para>
/// </summary>
internal sealed class CaptchaService : DomainServiceBase, ICaptchaService
{
    private readonly IReadOnlyList<ICaptchaProvider> _providers;
    private readonly IOptions<CaptchaOptions> _options;
    private readonly IRateLimitCheck _rateLimit;
    private readonly IHttpContextAccessor? _httpContextAccessor;
    private readonly ILogger<CaptchaService> _logger;

    public CaptchaService(
        IDomainUser user,
        IEnumerable<ICaptchaProvider> providers,
        IOptions<CaptchaOptions> options,
        IRateLimitCheck rateLimit,
        IHttpContextAccessor? httpContextAccessor,
        ILogger<CaptchaService> logger)
        : base(user)
    {
        _providers = providers?.ToList() ?? throw new ArgumentNullException(nameof(providers));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _rateLimit = rateLimit ?? throw new ArgumentNullException(nameof(rateLimit));
        _httpContextAccessor = httpContextAccessor;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// 签发新挑战：全局开关守卫（fail-closed）→ 取题 IP 频控（429）→ Provider 选中（未匹配守卫）→ 委托签发。
    /// </summary>
    public async Task<CaptchaChallenge> IssueAsync(string scene, CancellationToken ct = default)
    {
        var options = _options.Value;
        EnsureEnabled(options);

        // 取题频控（方案 §5.2：captcha:issue:{ip}，默认 10/min，防挑战表洪泛）——IP 空白/未注册 → unknown 桶
        // （fail-closed，AuthCenter AC-02 同款；IRateLimitCheck TryAdd fallback MemoryRateLimitCheck "始终在"）
        var ip = GetClientIp();
        var ipKey = string.IsNullOrWhiteSpace(ip) ? "unknown" : ip;
        if (!_rateLimit.TryAcquire($"captcha:issue:{ipKey}", options.IssuePerMinutePerIp, TimeSpan.FromMinutes(1), out _))
            throw new RateLimitedException(0, $"captcha:issue:{ipKey}") { ErrorCode = "CAPTCHA_RATE_LIMITED" };

        var provider = SelectProvider(options.EnabledProviderType);
        return await provider.IssueAsync(scene, ct);
    }

    /// <summary>
    /// 校验挑战作答：全局开关守卫（fail-closed）→ Provider 选中 → 委托校验（失败统一 Passed=false，不区分判因）。
    /// </summary>
    public async Task<CaptchaVerifyResult> VerifyAsync(string challengeId, string answer, CancellationToken ct = default)
    {
        var options = _options.Value;
        EnsureEnabled(options);

        var provider = SelectProvider(options.EnabledProviderType);
        return await provider.VerifyAsync(challengeId, answer, ct);
    }

    /// <summary>全局开关守卫（m7——Enabled=false 恒守卫异常，禁静默放行）。</summary>
    private static void EnsureEnabled(CaptchaOptions options)
    {
        if (!options.Enabled)
            throw new DomainGuardException(
                "Captcha 全局开关已关闭（TKWF:Captcha:Enabled=false）——若消费方场景要求门禁则属配置冲突，拒绝而非跳过（fail-closed）");
    }

    /// <summary>Provider 选中——Options EnabledProviderType 对 Type 精确匹配；未匹配 → fail-closed 守卫异常。</summary>
    private ICaptchaProvider SelectProvider(string type)
    {
        foreach (var provider in _providers)
        {
            if (provider.Type == type)
                return provider;
        }

        var registered = _providers.Count == 0
            ? "(无已注册 Provider)"
            : string.Join(", ", _providers.Select(p => p.Type));
        throw new DomainGuardException($"Captcha Provider '{type}' 未注册（已注册：{registered}）——fail-closed，禁止无验证放行");
    }

    /// <summary>客户端 IP（RemoteIpAddress；未装配 HttpContext/获取失败 → null → unknown 桶）。</summary>
    private string? GetClientIp()
        => _httpContextAccessor?.HttpContext?.Connection?.RemoteIpAddress?.ToString();
}