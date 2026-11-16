using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Captcha;

/// <summary>
/// 本地图形验证码 Provider（v1 唯一内置实现，方案 §5.2）。
/// <para>挑战生成：随机 4-6 位字符（无歧义字母表，去 0/O/1/I/l）→ 答案 SHA256 散列存挑战态（<see cref="CaptchaChallengeStateStore"/>，
/// <b>答案永不进客户端载荷</b>）→ 渲染 SVG 字符串 Payload（<b>零第三方依赖</b>——System.Drawing 不可跨平台；
/// SkiaSharp/ImageSharp 引入须另行 ADR，§11 风险 #1 已冻结本方案）。</para>
/// <para>挑战态生命周期（TTL/尝试上限/单次消费）由本 Provider 自持：签发时写状态（Attempts = Options.AttemptsPerChallenge，
/// ExpireAt = UtcNow + Options.ChallengeTtlSeconds），校验经 <see cref="CaptchaChallengeStateStore.ExecuteAtomicAsync"/>
/// per-challengeId 自锁串行化（in-memory CAS 非原子防护，§5.2）——成功即消费删除 / 达答错上限作废 / 过期视为不存在。</para>
/// <para>校验失败统一 <c>Passed=false</c> 不区分判因（防枚举/防侧信道泄露挑战态存在性）。</para>
/// <para>注册：<c>TryAddEnumerableConstructible&lt;ICaptchaProvider, ImageCaptchaProvider&gt;()</c>（ADR92 集合版守卫工厂——
/// 帧内 CurrentAopUser 供给 IDomainUser）；本 Provider 不依赖用户上下文，<see cref="DomainServiceBase"/> 继承仅为集合契约一致。</para>
/// </summary>
internal sealed class ImageCaptchaProvider : DomainServiceBase, ICaptchaProvider
{
    /// <summary>无歧义字母表（去 0/O/1/I/l——目视易混字符，提升人工录入正确率）。</summary>
    private const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private readonly CaptchaChallengeStateStore _store;
    private readonly IOptions<CaptchaOptions> _options;
    private readonly ILogger<ImageCaptchaProvider> _logger;

    public ImageCaptchaProvider(
        IDomainUser user,
        CaptchaChallengeStateStore store,
        IOptions<CaptchaOptions> options,
        ILogger<ImageCaptchaProvider> logger)
        : base(user)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Provider 类型标识（Options EnabledProviderType="image" 精确匹配）。</summary>
    public string Type => "image";

    /// <summary>
    /// 签发新挑战：随机 4-6 位码 → 答案 SHA256 散列落挑战态（IMemoryCache，TTL 配置化）→ SVG 图形载荷。
    /// <paramref name="scene"/> v1 纯透传（CAPTCHA 场景无关——"哪些场景要门禁"归消费方场景注册表）。
    /// </summary>
    public Task<CaptchaChallenge> IssueAsync(string scene, CancellationToken ct = default)
    {
        var options = _options.Value;
        var ttl = options.ChallengeTtlSeconds > 0 ? options.ChallengeTtlSeconds : 300;

        var code = RandomCode();
        var challengeId = RandomToken(16); // 32 hex——高熵防枚举
        _store.Set(challengeId, new CaptchaChallengeState(
            AnswerHash: Sha256Hex(code),
            AttemptsRemaining: Math.Max(1, options.AttemptsPerChallenge),
            ExpireAt: DateTime.UtcNow.AddSeconds(ttl)));

        _logger.LogDebug("图形验证码已签发（challengeId={ChallengeIdPrefix}，长度 {Length}，TTL {Ttl}s）",
            challengeId[..Math.Min(8, challengeId.Length)], code.Length, ttl);

        return Task.FromResult(new CaptchaChallenge(challengeId, RenderSvg(challengeId, code), ttl));
    }

    /// <summary>
    /// 校验挑战作答（per-challengeId 自锁原子读-改-写）：成功 → 单次消费删除并 Passed=true；答错 → 尝试计数递减，
    /// 耗尽 → 作废删除；过期/不存在 → 统一 Passed=false。
    /// </summary>
    public async Task<CaptchaVerifyResult> VerifyAsync(string challengeId, string answer, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(challengeId) || string.IsNullOrWhiteSpace(answer))
            return new CaptchaVerifyResult(false);

        var normalized = answer.Trim().ToUpperInvariant(); // 码为大写无歧义字母表——大小写容错

        var passed = false;
        await _store.ExecuteAtomicAsync(challengeId, state =>
        {
            // 不存在 / 已过期——统一判未通过（不区分判因防枚举）
            if (state is null || state.ExpireAt < DateTime.UtcNow)
                return null;

            if (CryptographicOperations.FixedTimeEquals(
                    Encoding.UTF8.GetBytes(Sha256Hex(normalized)),
                    Encoding.UTF8.GetBytes(state.AnswerHash)))
            {
                passed = true; // 单次消费：成功即删除挑战态（重放/重复提交 → 状态不存在 → false）
                return null;
            }

            var remaining = state.AttemptsRemaining - 1;
            if (remaining <= 0)
            {
                _logger.LogDebug("图形验证码答错耗尽作废（challengeId={ChallengeIdPrefix}）", challengeId[..Math.Min(8, challengeId.Length)]);
                return null; // 达上限作废
            }

            return state with { AttemptsRemaining = remaining };
        }, ct);

        return new CaptchaVerifyResult(passed);
    }

    /// <summary>随机 4-6 位字符（加密安全 RandomNumberGenerator——禁 System.Random）。</summary>
    private static string RandomCode()
    {
        var length = RandomNumberGenerator.GetInt32(3) + 4; // 4..6
        var chars = new char[length];
        for (var i = 0; i < length; i++)
            chars[i] = Alphabet[RandomNumberGenerator.GetInt32(Alphabet.Length)];
        return new string(chars);
    }

    /// <summary>加密安全随机 hex（小写）——挑战 Id（高熵防枚举/防重放）。</summary>
    private static string RandomToken(int bytes)
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(bytes)).ToLowerInvariant();

    /// <summary>答案单向散列——SHA256 hex（小写，统一 MFA 先例）。</summary>
    internal static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>
    /// 渲染 SVG 字符串图形码（零第三方依赖——纯字符串，跨平台；消费方 <c>&lt;img src="data:image/svg+xml;base64,..."&gt;</c>
    /// 或 <c>&lt;img src="data:image/svg+xml;utf8,..."&gt;</c> 直接展示）。
    /// 视觉要素：浅灰圆角底 + 2 条随机干扰线 + 居中加粗字符 + 每字符轻微随机旋转（基础 OCR 干扰）；
    /// 随机数经 challengeId 确定性播种（同题载荷稳定——缓存/懒加载友好；非安全边界，防 OCR 靠挑战态 hash 单次消费本身）。
    /// </summary>
    private static string RenderSvg(string challengeId, string code)
    {
        var n = code.Length;
        var fontSize = n <= 4 ? 36 : n == 5 ? 30 : 26;
        var width = 40 + n * 28;
        const int height = 60;

        var seed = 0;
        foreach (var c in challengeId) seed = (seed * 31 + c) & 0x7FFFFFFF; // 确定性播种（非安全随机）
        var rnd = new Random(seed);

        var sb = new StringBuilder(512);
        sb.Append($"<svg xmlns=\"http://www.w3.org/2000/svg\" width=\"{width}\" height=\"{height}\" viewBox=\"0 0 {width} {height}\">");
        sb.Append($"<rect width=\"100%\" height=\"100%\" fill=\"#f3f4f6\" rx=\"8\"/>");

        for (var i = 0; i < 2; i++) // 2 条随机干扰线
        {
            sb.Append($"<line x1=\"{rnd.Next(0, width)}\" y1=\"{rnd.Next(0, height)}\" x2=\"{rnd.Next(0, width)}\" y2=\"{rnd.Next(0, height)}\" stroke=\"#9ca3af\" stroke-width=\"1\" opacity=\"0.7\"/>");
        }

        var charWidth = 28;
        var startX = (width - n * charWidth) / 2.0 + charWidth / 2.0;
        for (var i = 0; i < n; i++)
        {
            var x = startX + i * charWidth;
            var y = height / 2.0 + fontSize / 3.0;
            var rotate = rnd.Next(-8, 9); // 每字符 -8°~+8° 轻微旋转
            sb.Append($"<text x=\"{x:0.0}\" y=\"{y:0.0}\" font-family=\"Consolas, Menlo, monospace\" font-size=\"{fontSize}\" font-weight=\"bold\" fill=\"#1f2937\" text-anchor=\"middle\" transform=\"rotate({rotate} {x:0.0} {y:0.0})\">{code[i]}</text>");
        }

        sb.Append("</svg>");
        return sb.ToString();
    }
}