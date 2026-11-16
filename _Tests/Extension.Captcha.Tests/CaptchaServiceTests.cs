using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain.Exceptions;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Captcha.Tests;

/// <summary>
/// 人机验证门面 + 本地图形 Provider 测试（方案 §7 Capture 行 + §5.1-§5.3 语义）——
/// 生产 AOP 路径：<c>user.Use&lt;ICaptchaService&gt;()</c>（AddConstructibleService 守卫工厂 + CurrentAopUser 供给）；
/// Provider 集合经门面 ctor 在帧内枚举（TryAddEnumerableConstructible 集合版守卫工厂）。
/// <para>覆盖：Issue/Verify 正负、尝试上限作废、TTL 过期、单次消费 CAS（重放拒绝）、取题 IP 频控
/// （unknown 桶 + 桩化逐 IP 桶）、Provider 选中/未注册 fail-closed、Enabled=false fail-closed（m7）。</para>
/// </summary>
public class CaptchaServiceTests
{
    private static CaptchaOptions TestOptions(Action<CaptchaOptions>? tweak = null)
    {
        var o = new CaptchaOptions();
        tweak?.Invoke(o);
        return o;
    }

    private static CaptchaTestHost CreateHost(CaptchaOptions? options = null, Action<IServiceCollection>? configure = null)
        => CaptchaTestHost.Create(options ?? TestOptions(), configure);

    // ── Issue/Verify 正负 ─────────────────────────────────────────────────

    [Fact]
    public async Task IssueAsync_Success_ReturnsChallenge_WithCodeEmbeddedInSvgText()
    {
        using var host = CreateHost(TestOptions(o => o.ChallengeTtlSeconds = 120));
        var svc = host.Captcha;

        var challenge = await svc.IssueAsync("login", TestContext.Current.CancellationToken);

        Assert.False(string.IsNullOrWhiteSpace(challenge.ChallengeId));
        Assert.False(string.IsNullOrWhiteSpace(challenge.Payload));
        Assert.Equal(120, challenge.TtlSeconds);
        Assert.Contains("<svg", challenge.Payload);
        // 明文码以 <text> 元素逐字符内嵌于 SVG 载荷——测试经文本提取还原（视觉码应可被消费方展示/人工录入）
        var code = ExtractCodeFromSvg(challenge.Payload);
        Assert.InRange(code.Length, 4, 6);

        // 正演：正确作答 → Passed=true（M2 规范语义——BUG-CAPTCHA-STORE 修复后恢复）
        var ok = await svc.VerifyAsync(challenge.ChallengeId, code, TestContext.Current.CancellationToken);
        Assert.True(ok.Passed);
        Assert.Null(ok.Score);
    }

    [Fact]
    public async Task VerifyAsync_WrongAnswer_PassedFalse_AndDoesNotConsume()
    {
        using var host = CreateHost();
        var svc = host.Captcha;
        var challenge = await svc.IssueAsync("login", TestContext.Current.CancellationToken);
        var code = ExtractCodeFromSvg(challenge.Payload);

        // 反演：错误作答 → Passed=false（不区分判因）——状态保留路径（答错递减）正常
        var wrong = await svc.VerifyAsync(challenge.ChallengeId, "XXXXXX", TestContext.Current.CancellationToken);
        Assert.False(wrong.Passed);
        Assert.Null(wrong.Score);

        // 错答不消费——正确作答仍可过（M2 规范语义；BUG-CAPTCHA-STORE 修复后恢复正演）
        var ok = await svc.VerifyAsync(challenge.ChallengeId, code, TestContext.Current.CancellationToken);
        Assert.True(ok.Passed);
    }

    [Fact]
    public async Task VerifyAsync_Answer_CaseInsensitive()
    {
        using var host = CreateHost();
        var svc = host.Captcha;
        var challenge = await svc.IssueAsync("login", TestContext.Current.CancellationToken);
        var code = ExtractCodeFromSvg(challenge.Payload);

        // 大小写容错（码为大写无歧义字母表——`Trim().ToUpperInvariant()` 归一）；小写作答归一后命中散列 → 通过
        var ok = await svc.VerifyAsync(challenge.ChallengeId, code.ToLowerInvariant(), TestContext.Current.CancellationToken);
        Assert.True(ok.Passed);
    }

    [Fact]
    public async Task VerifyAsync_BlankChallengeIdOrAnswer_PassedFalse()
    {
        using var host = CreateHost();
        var svc = host.Captcha;
        var challenge = await svc.IssueAsync("login", TestContext.Current.CancellationToken);

        Assert.False((await svc.VerifyAsync("", "ANSWER", TestContext.Current.CancellationToken)).Passed);
        Assert.False((await svc.VerifyAsync("   ", "ANSWER", TestContext.Current.CancellationToken)).Passed);
        Assert.False((await svc.VerifyAsync(challenge.ChallengeId, "", TestContext.Current.CancellationToken)).Passed);
        Assert.False((await svc.VerifyAsync(challenge.ChallengeId, "   ", TestContext.Current.CancellationToken)).Passed);
    }

    // ── 尝试上限 3 次作废 ─────────────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_AttemptsExhausted_ChallengeInvalidated()
    {
        // 默认 AttemptsPerChallenge=3——连错 3 次作废，第 4 次（即便正确）也 Passed=false
        using var host = CreateHost();
        var svc = host.Captcha;
        var challenge = await svc.IssueAsync("login", TestContext.Current.CancellationToken);
        var code = ExtractCodeFromSvg(challenge.Payload);

        for (var i = 0; i < 2; i++)
        {
            var w = await svc.VerifyAsync(challenge.ChallengeId, "XXXXXX", TestContext.Current.CancellationToken);
            Assert.False(w.Passed, $"第 {i + 1} 次错答应 Passed=false");
        }

        // 第 3 次错答 = 耗尽作废（达上限移除路径）——3 次错答后正确码也 Passed=false（M2 规范语义）
        var third = await svc.VerifyAsync(challenge.ChallengeId, "XXXXXX", TestContext.Current.CancellationToken);
        Assert.False(third.Passed, "第 3 次错答应 Passed=false（耗尽作废）");
        var after = await svc.VerifyAsync(challenge.ChallengeId, code, TestContext.Current.CancellationToken);
        Assert.False(after.Passed, "作废后正确码也应 Passed=false");
    }

    [Fact]
    public async Task VerifyAsync_AttemptsRemaining_BelowCap_ChallengeSurvives()
    {
        using var host = CreateHost(TestOptions(o => o.AttemptsPerChallenge = 3));
        var svc = host.Captcha;
        var challenge = await svc.IssueAsync("login", TestContext.Current.CancellationToken);
        var code = ExtractCodeFromSvg(challenge.Payload);

        Assert.False((await svc.VerifyAsync(challenge.ChallengeId, "XXXXXX", TestContext.Current.CancellationToken)).Passed);
        Assert.False((await svc.VerifyAsync(challenge.ChallengeId, "YYYYYY", TestContext.Current.CancellationToken)).Passed);

        // 2 次错答未达上限——第 3 次正确仍可过（M2 规范语义）
        var ok = await svc.VerifyAsync(challenge.ChallengeId, code, TestContext.Current.CancellationToken);
        Assert.True(ok.Passed);
    }

    [Fact]
    public async Task VerifyAsync_CustomAttemptsPerChallenge_Honored()
    {
        // Options.AttemptsPerChallenge=1——1 次错答即作废（归零即移除路径）
        using var host = CreateHost(TestOptions(o => o.AttemptsPerChallenge = 1));
        var svc = host.Captcha;
        var challenge = await svc.IssueAsync("login", TestContext.Current.CancellationToken);
        var code = ExtractCodeFromSvg(challenge.Payload);

        // M2 规范语义：第 1 次错答 → Passed=false 且挑战作废（归零即移除——正确码亦拒）
        var first = await svc.VerifyAsync(challenge.ChallengeId, "XXXXXX", TestContext.Current.CancellationToken);
        Assert.False(first.Passed, "第 1 次错答应 Passed=false（AttemptsPerChallenge=1 即作废）");
        var after = await svc.VerifyAsync(challenge.ChallengeId, code, TestContext.Current.CancellationToken);
        Assert.False(after.Passed, "作废后正确码也应 Passed=false");
    }

    // ── 单次消费 CAS（重放拒绝）───────────────────────────────────────────

    [Fact]
    public async Task VerifyAsync_Success_SingleConsume_ReplayRejected()
    {
        using var host = CreateHost();
        var svc = host.Captcha;
        var challenge = await svc.IssueAsync("login", TestContext.Current.CancellationToken);
        var code = ExtractCodeFromSvg(challenge.Payload);

        // M2 规范语义：首次正确 → 通过并消费；同 challengeId+answer 重放 → 拒绝（单次消费 CAS）
        var first = await svc.VerifyAsync(challenge.ChallengeId, code, TestContext.Current.CancellationToken);
        Assert.True(first.Passed, "首次正确应通过");
        var replay = await svc.VerifyAsync(challenge.ChallengeId, code, TestContext.Current.CancellationToken);
        Assert.False(replay.Passed, "重放应拒绝（单次消费）");
    }

    [Fact]
    public async Task VerifyAsync_AfterTtl_Expired_PassedFalse()
    {
        // TTL=1s——签发后等待 1.1s 校验（即便答案正确）→ 过期 Passed=false
        using var host = CreateHost(TestOptions(o => o.ChallengeTtlSeconds = 1));
        var svc = host.Captcha;
        var challenge = await svc.IssueAsync("login", TestContext.Current.CancellationToken);
        var code = ExtractCodeFromSvg(challenge.Payload);

        await Task.Delay(1100, TestContext.Current.CancellationToken);

        // 过期移除路径——过期校验（即便答案正确）→ Passed=false（M2 规范语义）
        var expired = await svc.VerifyAsync(challenge.ChallengeId, code, TestContext.Current.CancellationToken);
        Assert.False(expired.Passed, "过期后应 Passed=false");
    }

    [Fact]
    public async Task VerifyAsync_WithInTtl_Passes()
    {
        using var host = CreateHost(TestOptions(o => o.ChallengeTtlSeconds = 60));
        var svc = host.Captcha;
        var challenge = await svc.IssueAsync("login", TestContext.Current.CancellationToken);
        var code = ExtractCodeFromSvg(challenge.Payload);

        // TTL 内正确作答 → Passed=true（M2 规范语义）
        var ok = await svc.VerifyAsync(challenge.ChallengeId, code, TestContext.Current.CancellationToken);
        Assert.True(ok.Passed);
    }

    // ── 取题 IP 频控 ─────────────────────────────────────────────────────

    [Fact]
    public async Task IssueAsync_RateLimited_ThirdIssueWithinWindow_Throws()
    {
        // 宿主默认无 HttpContext（HttpContextAccessor 桩 HttpContext=null）→ unknown 桶（fail-closed）；
        // IssuePerMinutePerIp=2 → 第 3 次取题频控拒绝
        using var host = CreateHost(TestOptions(o => o.IssuePerMinutePerIp = 2));
        var svc = host.Captcha;

        var a = await svc.IssueAsync("login", TestContext.Current.CancellationToken);
        var b = await svc.IssueAsync("login", TestContext.Current.CancellationToken);
        Assert.False(string.IsNullOrWhiteSpace(a.ChallengeId));
        Assert.False(string.IsNullOrWhiteSpace(b.ChallengeId));

        var ex = await Assert.ThrowsAsync<RateLimitedException>(() =>
            svc.IssueAsync("login", TestContext.Current.CancellationToken));

        Assert.Equal("CAPTCHA_RATE_LIMITED", ex.ErrorCode);
    }

    [Fact]
    public async Task IssueAsync_RateLimit_PerIpBucket_Independent()
    {
        // 桩化 IHttpContextAccessor——同一 IP 共享桶（第 3 次拒绝）；切换 IP → 新桶放行
        var accessor = new FakeHttpContextAccessor();
        using var host = CreateHost(
            TestOptions(o => o.IssuePerMinutePerIp = 2),
            services => services.AddSingleton<IHttpContextAccessor>(accessor));
        var svc = host.Captcha;

        Assert.NotNull(await svc.IssueAsync("login", TestContext.Current.CancellationToken));
        Assert.NotNull(await svc.IssueAsync("login", TestContext.Current.CancellationToken));
        await Assert.ThrowsAsync<RateLimitedException>(() =>
            svc.IssueAsync("login", TestContext.Current.CancellationToken));

        accessor.SetIp(IPAddress.Parse("10.0.0.99"));   // 切换到不同 IP → 独立桶
        Assert.NotNull(await svc.IssueAsync("login", TestContext.Current.CancellationToken));
    }

    // ── Provider 选中 / 未注册 fail-closed ────────────────────────────────

    [Fact]
    public async Task IssueAsync_EnabledProviderTypeUnmatched_GuardThrows()
    {
        using var host = CreateHost(TestOptions(o => o.EnabledProviderType = "geetest"));
        var svc = host.Captcha;

        var ex = await Assert.ThrowsAsync<DomainGuardException>(() =>
            svc.IssueAsync("login", TestContext.Current.CancellationToken));

        Assert.Contains("geetest", ex.Message);
        Assert.Contains("未注册", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_EnabledProviderTypeUnmatched_GuardThrows()
    {
        using var host = CreateHost(TestOptions(o => o.EnabledProviderType = "nonexistent"));
        var svc = host.Captcha;

        await Assert.ThrowsAsync<DomainGuardException>(() =>
            svc.VerifyAsync("challenge-1", "ANSWER", TestContext.Current.CancellationToken));
    }

    // ── Enabled=false fail-closed（m7）────────────────────────────────────

    [Fact]
    public async Task IssueAsync_GlobalSwitchDisabled_GuardThrows()
    {
        using var host = CreateHost(TestOptions(o => o.Enabled = false));
        var svc = host.Captcha;

        var ex = await Assert.ThrowsAsync<DomainGuardException>(() =>
            svc.IssueAsync("login", TestContext.Current.CancellationToken));

        Assert.Contains("已关闭", ex.Message);
    }

    [Fact]
    public async Task VerifyAsync_GlobalSwitchDisabled_GuardThrows()
    {
        using var host = CreateHost(TestOptions(o => o.Enabled = false));
        var svc = host.Captcha;

        await Assert.ThrowsAsync<DomainGuardException>(() =>
            svc.VerifyAsync("challenge-1", "ANSWER", TestContext.Current.CancellationToken));
    }

    // ── SVG 载荷文本提取（码明文内嵌途径）─────────────────────────────────

    /// <summary>
    /// 从 SVG 载荷提取验证码明文——ImageCaptchaProvider 每字符渲染一个
    /// <c>&lt;text ...&gt;{code[i]}&lt;/text&gt;</c> 元素（见 ImageCaptchaProvider.RenderSvg）。
    /// 答案明文仅存在于该图形载荷渲染瞬态；挑战态只有 SHA256 散列——测试唯一可取回明文的途径。
    /// </summary>
    internal static string ExtractCodeFromSvg(string payload)
    {
        var matches = Regex.Matches(payload, @"<text[^>]*>([^<]+)</text>");
        if (matches.Count == 0)
            throw new InvalidOperationException($"SVG 载荷不含 <text> 元素（无法提取验证码）——载荷开头：{payload[..Math.Min(120, payload.Length)]}");

        var code = string.Concat(matches.Select(m => m.Groups[1].Value));
        if (code.Length is < 4 or > 6)
            throw new InvalidOperationException($"提取的验证码长度异常：'{code}' (len={code.Length})");

        return code;
    }
}

/// <summary>
/// IHttpContextAccessor 桩——RemoteIpAddress 可切换（取题 IP 频控逐 IP 桶隔离测试）。
/// 生产路径语义：门面读 <c>HttpContext.Connection.RemoteIpAddress</c>；null/未装配 → unknown 桶（fail-closed）。
/// </summary>
internal sealed class FakeHttpContextAccessor : IHttpContextAccessor
{
    public HttpContext? HttpContext { get; set; }

    public FakeHttpContextAccessor() => SetIp(IPAddress.Parse("192.0.2.1"));

    public void SetIp(IPAddress ip)
    {
        var features = new FeatureCollection();
        features.Set<IHttpConnectionFeature>(new FakeConnectionFeature { RemoteIpAddress = ip });
        HttpContext = new DefaultHttpContext(features);
    }

    private sealed class FakeConnectionFeature : IHttpConnectionFeature
    {
        public string ConnectionId { get; set; } = "conn";
        public IPAddress? RemoteIpAddress { get; set; }
        public IPAddress? LocalIpAddress { get; set; }
        public int RemotePort { get; set; }
        public int LocalPort { get; set; }
    }
}