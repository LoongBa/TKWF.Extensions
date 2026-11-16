using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain.Exceptions;
using TKWF.Ext.Captcha;
using TKWF.Ext.SmsCode;

namespace TKWF.Ext.SmsCode.Tests;

/// <summary>
/// 短信验证码门面测试（方案 §7 门面行 + §4.3/§4.4/§4.6 语义）——
/// 生产 AOP 路径：<c>user.Use&lt;ISmsCodeService&gt;()</c>（AddConstructibleService 守卫工厂 + CurrentAopUser 供给）。
/// <para>覆盖：发送（成功/重发间隔/小时/日/IP/活跃上限/未知场景/生产无 sender 503/dev mock/重发作废）、
/// 校验（成功/单次消费/错误+FailedAttempts 作废/过期/未知场景/校验频控/ScopeMissing）。</para>
/// </summary>
public class SmsCodeServiceTests
{
    private sealed record SentSms(string Phone, string Content);

    private sealed class TestSmsSender : ISmsSender
    {
        public List<SentSms> Sent { get; } = new();

        public Task SendAsync(SmsMessage message, CancellationToken ct = default)
        {
            Sent.Add(new SentSms(message.Phone, message.Content));
            return Task.CompletedTask;
        }
    }

    private static SmsCodeOptions TestOptions(Action<SmsCodeOptions>? tweak = null)
    {
        var o = new SmsCodeOptions { IsProduction = true };
        // 发送链路测试聚焦频控/落库/发送——显式关 login 门禁（生产默认 RequiresCaptcha=true 由门禁测试组专门覆盖，Q6/m7 全分支）
        o.Scenes[SmsScenes.Login].RequiresCaptcha = false;
        tweak?.Invoke(o);
        return o;
    }

    private static SmsCodeTestHost CreateHost(SmsCodeOptions? options = null, TestSmsSender? sender = null)
        => SmsCodeTestHost.Create(options ?? TestOptions(), services =>
        {
            if (sender is not null)
                services.TryAddSingleton<ISmsSender>(sender);
        });

    private static Task<SmsCodeSendResult> Send(ISmsCodeService svc, string subject = "13800000001", string? token = null)
        => svc.SendCodeAsync(SmsScenes.Login, subject, subject, token, TestContext.Current.CancellationToken);

    // ── 发送 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task SendCodeAsync_Success_SendsViaSender_AndPersistsHashedCode()
    {
        var sender = new TestSmsSender();
        using var host = CreateHost(sender: sender);
        var svc = host.User.Use<ISmsCodeService>();

        var result = await Send(svc);

        Assert.True(result.Sent);
        Assert.Null(result.Failure);
        Assert.Single(sender.Sent);
        Assert.Equal("13800000001", sender.Sent[0].Phone);
        // 短信内容含 6 位验证码明文（发送器可读）；落库为散列（下方验证）
        var record = await host.Codes.GetLatestUnverifiedAsync("13800000001", SmsScenes.Login, TestContext.Current.CancellationToken);
        Assert.NotNull(record);
        Assert.NotEqual(sender.Sent[0].Content, record!.CodeHash);
        Assert.Equal(64, record.CodeHash.Length);
        Assert.True(VerificationCodeEngine.VerifyHash(
            ExtractCode(sender.Sent[0].Content), record.CodeHash), "落库散列应为明文码的 SHA256 hex");
    }

    [Fact]
    public async Task SendCodeAsync_UnknownScene_FailsClosed_NoRateKey()
    {
        // dev mock（IsProduction=false）排除 sender 因素——本用例验证未知场景 fail-closed 不落频控键
        using var host = CreateHost(TestOptions(o => o.IsProduction = false));
        var svc = host.User.Use<ISmsCodeService>();

        var result = await svc.SendCodeAsync("noscope", "13800000001", "13800000001", null, TestContext.Current.CancellationToken);

        Assert.False(result.Sent);
        Assert.Equal(SmsCodeSendFailure.UnknownScene, result.Failure);
        // 未落入任何频控键——同 subject 用合法场景仍可发
        var ok = await Send(svc);
        Assert.True(ok.Sent);
    }

    [Fact]
    public async Task SendCodeAsync_ResendInterval_RateLimited_WithRetryAfter()
    {
        var sender = new TestSmsSender();
        using var host = CreateHost(TestOptions(o => o.SmsResendIntervalSeconds = 60), sender);
        var svc = host.User.Use<ISmsCodeService>();

        var first = await Send(svc);
        var second = await Send(svc);

        Assert.True(first.Sent);
        Assert.False(second.Sent);
        Assert.Equal(SmsCodeSendFailure.RateLimited, second.Failure);
        Assert.NotNull(second.RetryAfterSeconds);
        Assert.InRange(second.RetryAfterSeconds!.Value, 1, 60);
        Assert.Single(sender.Sent);   // 第二次被频控拦截——未发送
    }

    [Fact]
    public async Task SendCodeAsync_HourlyLimit_RateLimited()
    {
        var sender = new TestSmsSender();
        using var host = CreateHost(TestOptions(o => { o.SmsHourlyLimitPerSubject = 3; o.SmsResendIntervalSeconds = 0; }), sender);
        var svc = host.User.Use<ISmsCodeService>();

        for (var i = 0; i < 3; i++)
            Assert.True((await Send(svc)).Sent);
        var fourth = await Send(svc);

        Assert.False(fourth.Sent);
        Assert.Equal(SmsCodeSendFailure.RateLimited, fourth.Failure);
        Assert.Equal(3, sender.Sent.Count);
    }

    [Fact]
    public async Task SendCodeAsync_ActiveCap_RateLimited()
    {
        // 活跃码上限：直接落库 2 条活跃记录（绕过重发作废——CountActive 检查在 Invalidate 之前），
        // 再 Send 应被频控拒绝（CountActive=2 >= MaxActiveCodesPerSubject=2）
        var sender = new TestSmsSender();
        using var host = CreateHost(TestOptions(o =>
        {
            o.MaxActiveCodesPerSubject = 2;
            o.SmsResendIntervalSeconds = 0;
            o.SmsHourlyLimitPerSubject = 100;
            o.IsProduction = false;   // dev mock——验证活跃上限拒绝而非 sender 503
        }), sender);
        var svc = host.User.Use<ISmsCodeService>();

        await host.Codes.CreateAsync(new SmsCodeRecordEntity
        {
            Subject = "13800000001",
            DeliveryTarget = "13800000001",
            Scene = SmsScenes.Login,
            CodeHash = VerificationCodeEngine.HashCode("111111"),
            ExpireAt = DateTime.UtcNow.AddMinutes(5),
            CreateTime = DateTime.UtcNow
        }, TestContext.Current.CancellationToken);
        await host.Codes.CreateAsync(new SmsCodeRecordEntity
        {
            Subject = "13800000001",
            DeliveryTarget = "13800000001",
            Scene = SmsScenes.Login,
            CodeHash = VerificationCodeEngine.HashCode("222222"),
            ExpireAt = DateTime.UtcNow.AddMinutes(5),
            CreateTime = DateTime.UtcNow.AddSeconds(-1)
        }, TestContext.Current.CancellationToken);

        var third = await Send(svc);

        Assert.False(third.Sent);
        Assert.Equal(SmsCodeSendFailure.RateLimited, third.Failure);
    }

    [Fact]
    public async Task SendCodeAsync_IpBucket_RateLimited()
    {
        var sender = new TestSmsSender();
        // IP 桶共享（send+verify）——SmsHourlyLimitPerIp=2；宿主无 IHttpContextAccessor → unknown 桶（fail-closed）
        using var host = CreateHost(TestOptions(o =>
        {
            o.SmsHourlyLimitPerIp = 2;
            o.SmsResendIntervalSeconds = 0;
            o.SmsDailyLimitPerSubject = 100;
            o.SmsHourlyLimitPerSubject = 100;
        }), sender);
        var svc = host.User.Use<ISmsCodeService>();

        Assert.True((await Send(svc, "13800000001")).Sent);
        Assert.True((await Send(svc, "13800000002")).Sent);   // 不同 subject 但同 IP 桶
        var third = await Send(svc, "13800000003");

        Assert.False(third.Sent);
        Assert.Equal(SmsCodeSendFailure.RateLimited, third.Failure);
    }

    [Fact]
    public async Task VerifyCodeAsync_IpBucketShared_FromSend_RejectsVerify()
    {
        var sender = new TestSmsSender();
        // P1 审核修复：verify 亦消费 IP 桶（smscode:ip:{scope}:{ip}——与 send 共享配额，防换 subject 枚举 verify 面）
        // SmsHourlyLimitPerIp=2；宿主无 IHttpContextAccessor → unknown 桶
        using var host = CreateHost(TestOptions(o =>
        {
            o.SmsHourlyLimitPerIp = 2;
            o.SmsResendIntervalSeconds = 0;
            o.SmsDailyLimitPerSubject = 100;
            o.SmsHourlyLimitPerSubject = 100;
        }), sender);
        var svc = host.User.Use<ISmsCodeService>();

        // send 消耗 2 次 IP 配额（不同 subject 同桶）
        Assert.True((await Send(svc, "13800000001")).Sent);
        Assert.True((await Send(svc, "13800000002")).Sent);
        // verify（不同 subject）也应被 IP 桶拒——共享单桶语义
        var verify = await svc.VerifyCodeAsync(SmsScenes.Login, "13800000003", "000000", TestContext.Current.CancellationToken);

        Assert.False(verify.Verified);
        Assert.Equal(SmsCodeFailure.RateLimited, verify.Failure);
    }

    [Fact]
    public async Task SendCodeAsync_ProductionNoSender_ReturnsSenderUnavailable()
    {
        using var host = CreateHost(TestOptions());   // IsProduction=true，无 sender
        var svc = host.User.Use<ISmsCodeService>();

        var result = await Send(svc);

        Assert.False(result.Sent);
        Assert.Equal(SmsCodeSendFailure.SenderUnavailable, result.Failure);
    }

    [Fact]
    public async Task SendCodeAsync_DevMock_NoSenderStillSucceeds()
    {
        using var host = CreateHost(TestOptions(o => o.IsProduction = false));   // dev mock
        var svc = host.User.Use<ISmsCodeService>();

        var result = await Send(svc);

        Assert.True(result.Sent);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task SendCodeAsync_Resend_InvalidatesPriorActiveCodes()
    {
        var sender = new TestSmsSender();
        using var host = CreateHost(TestOptions(o => o.SmsResendIntervalSeconds = 0), sender);
        var svc = host.User.Use<ISmsCodeService>();

        Assert.True((await Send(svc)).Sent);
        Assert.True((await Send(svc)).Sent);   // 重发——旧码应作废

        var active = await host.Codes.CountActiveAsync("13800000001", SmsScenes.Login, TestContext.Current.CancellationToken);
        Assert.Equal(1, active);   // 旧码失效 + 新码活跃 = 只 1 条活跃（M3 闭环：重发作废 CAS）
        // 旧码不可用——校验旧码失败（NotFound：最新未消费记录是新码）
        var old = await svc.VerifyCodeAsync(SmsScenes.Login, "13800000001", "000000", TestContext.Current.CancellationToken);
        Assert.False(old.Verified);
        Assert.Equal(SmsCodeFailure.Mismatch, old.Failure);
    }

    [Fact]
    public async Task SendCodeAsync_ScopeMissing_WhenResolverReturnsNull()
    {
        using var host = SmsCodeTestHost.Create(TestOptions(), services =>
            services.AddSingleton<ISmsCodeScopeResolver>(new NullScopeResolver()));
        var svc = host.User.Use<ISmsCodeService>();

        var result = await Send(svc);

        Assert.False(result.Sent);
        Assert.Equal(SmsCodeSendFailure.ScopeMissing, result.Failure);
    }

    private sealed class NullScopeResolver : ISmsCodeScopeResolver
    {
        public string? GetScopePrefix() => null;
    }

    // ── 校验 ─────────────────────────────────────────────────────────

    [Fact]
    public async Task VerifyCodeAsync_CorrectCode_Verified_AndSingleConsume()
    {
        var sender = new TestSmsSender();
        using var host = CreateHost(sender: sender);
        var svc = host.User.Use<ISmsCodeService>();
        await Send(svc);
        var code = ExtractCode(sender.Sent[0].Content);

        var first = await svc.VerifyCodeAsync(SmsScenes.Login, "13800000001", code, TestContext.Current.CancellationToken);
        var second = await svc.VerifyCodeAsync(SmsScenes.Login, "13800000001", code, TestContext.Current.CancellationToken);

        Assert.True(first.Verified);
        Assert.Null(first.Failure);
        Assert.False(second.Verified);   // 单次消费 CAS——重放拒绝
        Assert.Equal(SmsCodeFailure.NotFound, second.Failure);
    }

    [Fact]
    public async Task VerifyCodeAsync_WrongCode_Mismatch_AndFailedAttemptsIncrement()
    {
        var sender = new TestSmsSender();
        using var host = CreateHost(TestOptions(o => o.FailedVerifyInvalidateThreshold = 5), sender);
        var svc = host.User.Use<ISmsCodeService>();
        await Send(svc);

        var wrong = await svc.VerifyCodeAsync(SmsScenes.Login, "13800000001", "000000", TestContext.Current.CancellationToken);

        Assert.False(wrong.Verified);
        Assert.Equal(SmsCodeFailure.Mismatch, wrong.Failure);
        var record = await host.Codes.GetLatestUnverifiedAsync("13800000001", SmsScenes.Login, TestContext.Current.CancellationToken);
        Assert.Equal(1, record!.FailedAttempts);
    }

    [Fact]
    public async Task VerifyCodeAsync_FailedAttemptsReachThreshold_InvalidatesCode()
    {
        // 阈值 3——连续 3 次错码后记录作废（ExpireAt 置 now）→ 后续正确码也失败（NotFound）
        var sender = new TestSmsSender();
        using var host = CreateHost(TestOptions(o => o.FailedVerifyInvalidateThreshold = 3), sender);
        var svc = host.User.Use<ISmsCodeService>();
        await Send(svc);
        var code = ExtractCode(sender.Sent[0].Content);

        for (var i = 0; i < 3; i++)
        {
            var w = await svc.VerifyCodeAsync(SmsScenes.Login, "13800000001", "000000", TestContext.Current.CancellationToken);
            Assert.Equal(SmsCodeFailure.Mismatch, w.Failure);
        }
        var afterInvalidate = await svc.VerifyCodeAsync(SmsScenes.Login, "13800000001", code, TestContext.Current.CancellationToken);

        // 作废 = ExpireAt 置 now（CAS 立即到期）——已作废码语义 = 过期（Expired），非删除（NotFound）
        Assert.False(afterInvalidate.Verified);
        Assert.Equal(SmsCodeFailure.Expired, afterInvalidate.Failure);
    }

    [Fact]
    public async Task VerifyCodeAsync_ExpiredCode_Expired()
    {
        // 宿主直落库一条已过期记录（绕过发送路径 TTL）——校验应判 Expired
        using var host = CreateHost(TestOptions());
        var svc = host.User.Use<ISmsCodeService>();
        await host.Codes.CreateAsync(new SmsCodeRecordEntity
        {
            Subject = "13800000001",
            DeliveryTarget = "13800000001",
            Scene = SmsScenes.Login,
            CodeHash = VerificationCodeEngine.HashCode("123456"),
            ExpireAt = DateTime.UtcNow.AddMinutes(-1),
            CreateTime = DateTime.UtcNow.AddMinutes(-6)
        }, TestContext.Current.CancellationToken);

        var expired = await svc.VerifyCodeAsync(SmsScenes.Login, "13800000001", "123456", TestContext.Current.CancellationToken);

        Assert.False(expired.Verified);
        Assert.Equal(SmsCodeFailure.Expired, expired.Failure);
    }

    [Fact]
    public async Task VerifyCodeAsync_UnknownScene_UnknownScene()
    {
        var sender = new TestSmsSender();
        using var host = CreateHost(sender: sender);
        var svc = host.User.Use<ISmsCodeService>();
        await Send(svc);
        var code = ExtractCode(sender.Sent[0].Content);

        var result = await svc.VerifyCodeAsync("noscope", "13800000001", code, TestContext.Current.CancellationToken);

        Assert.False(result.Verified);
        Assert.Equal(SmsCodeFailure.UnknownScene, result.Failure);
    }

    [Fact]
    public async Task VerifyCodeAsync_VerifyRateLimit_RateLimited()
    {
        // 阈值 3/15min——错码不触发频控，第 4 次调用频控拒绝（verify 调用计数，非活跃记录计数）
        using var host = CreateHost(TestOptions(o => o.VerifyAttemptsPerWindow = 3));
        var svc = host.User.Use<ISmsCodeService>();
        await Send(svc);

        for (var i = 0; i < 3; i++)
        {
            var w = await svc.VerifyCodeAsync(SmsScenes.Login, "13800000001", "000000", TestContext.Current.CancellationToken);
            Assert.Equal(SmsCodeFailure.Mismatch, w.Failure);   // 错码但未频控
        }
        var limited = await svc.VerifyCodeAsync(SmsScenes.Login, "13800000001", "000000", TestContext.Current.CancellationToken);

        Assert.Equal(SmsCodeFailure.RateLimited, limited.Failure);
    }

    private static string ExtractCode(string content)
    {
        // 短信内容"您的验证码是 123456，5 分钟内有效"——取 6 位数字
        var m = System.Text.RegularExpressions.Regex.Match(content, @"\d{6}");
        if (!m.Success) throw new InvalidOperationException($"短信内容不含 6 位验证码：{content}");
        return m.Value;
    }
}

/// <summary>
/// CAPTCHA 门禁集成测试（方案 §7 门禁行 + §4.3 Q6/m7）——login 场景 RequiresCaptcha=true 时：
/// 未传 token → 拒；token 格式错 → 拒；token 无效（Passed=false）→ 拒；token 有效 → 发送；
/// Captcha 未装配 + 场景要求门禁 → 守卫异常（fail-closed 禁静默跳过——绕过前端直调 send-code 必须被拒）。
/// </summary>
public class SmsCodeCaptchaGateTests
{
    private sealed class FakeCaptchaService : ICaptchaService
    {
        private readonly Func<CaptchaVerifyResult> _verifyResult;

        public FakeCaptchaService(Func<CaptchaVerifyResult> verifyResult) => _verifyResult = verifyResult;

        public Task<CaptchaChallenge> IssueAsync(string scene, CancellationToken ct = default)
            => Task.FromResult(new CaptchaChallenge("challenge-1", "payload", 300));

        public Task<CaptchaVerifyResult> VerifyAsync(string challengeId, string answer, CancellationToken ct = default)
            => Task.FromResult(_verifyResult());
    }

    private static SmsCodeOptions CaptchaOnOptions()
    {
        var o = new SmsCodeOptions { IsProduction = false };   // dev mock——验证门禁语义而非 sender 503
        o.Scenes[SmsScenes.Login].RequiresCaptcha = true;
        return o;
    }

    private static SmsCodeTestHost CreateGateHost(
        Func<CaptchaVerifyResult>? verifyResult = null,
        Action<IServiceCollection>? extraConfigure = null)
        => SmsCodeTestHost.Create(CaptchaOnOptions(), services =>
        {
            // 装配 fake ICaptchaService（契约面——只依赖 Abstractions 接口，不引 Captcha 主包）
            services.AddSingleton<ICaptchaService>(new FakeCaptchaService(
                verifyResult ?? (() => new CaptchaVerifyResult(true))));
            extraConfigure?.Invoke(services);
        });

    [Fact]
    public async Task SendCodeAsync_RequiresCaptcha_NoToken_Rejected()
    {
        using var host = CreateGateHost();
        var svc = host.User.Use<ISmsCodeService>();

        var result = await svc.SendCodeAsync(SmsScenes.Login, "13800000001", "13800000001", null, TestContext.Current.CancellationToken);

        Assert.False(result.Sent);
        Assert.Equal(SmsCodeSendFailure.CaptchaFailed, result.Failure);
    }

    [Fact]
    public async Task SendCodeAsync_RequiresCaptcha_EmptyToken_Rejected()
    {
        using var host = CreateGateHost();
        var svc = host.User.Use<ISmsCodeService>();

        var result = await svc.SendCodeAsync(SmsScenes.Login, "13800000001", "13800000001", "   ", TestContext.Current.CancellationToken);

        Assert.False(result.Sent);
        Assert.Equal(SmsCodeSendFailure.CaptchaFailed, result.Failure);
    }

    [Fact]
    public async Task SendCodeAsync_RequiresCaptcha_MalformedToken_Rejected()
    {
        using var host = CreateGateHost();
        var svc = host.User.Use<ISmsCodeService>();

        // 无 ':' 分隔 → 格式不符判 CaptchaFailed
        var result = await svc.SendCodeAsync(SmsScenes.Login, "13800000001", "13800000001", "challenge-1abc123", TestContext.Current.CancellationToken);

        Assert.False(result.Sent);
        Assert.Equal(SmsCodeSendFailure.CaptchaFailed, result.Failure);
    }

    [Fact]
    public async Task SendCodeAsync_RequiresCaptcha_InvalidAnswer_Rejected()
    {
        using var host = CreateGateHost(() => new CaptchaVerifyResult(false));
        var svc = host.User.Use<ISmsCodeService>();

        var result = await svc.SendCodeAsync(SmsScenes.Login, "13800000001", "13800000001", "challenge-1:wrong", TestContext.Current.CancellationToken);

        Assert.False(result.Sent);
        Assert.Equal(SmsCodeSendFailure.CaptchaFailed, result.Failure);
    }

    [Fact]
    public async Task SendCodeAsync_RequiresCaptcha_ValidToken_Sends()
    {
        using var host = CreateGateHost(() => new CaptchaVerifyResult(true));
        var svc = host.User.Use<ISmsCodeService>();

        var result = await svc.SendCodeAsync(SmsScenes.Login, "13800000001", "13800000001", "challenge-1:abc123", TestContext.Current.CancellationToken);

        Assert.True(result.Sent);
        Assert.Null(result.Failure);
    }

    [Fact]
    public async Task SendCodeAsync_RequiresCaptcha_CaptchaUnavailable_GuardThrows()
    {
        // 场景要求门禁但 Captcha 未装配 → 守卫异常（m7 fail-closed）
        using var host = SmsCodeTestHost.Create(CaptchaOnOptions());
        var svc = host.User.Use<ISmsCodeService>();

        var ex = await Assert.ThrowsAsync<DomainGuardException>(() =>
            svc.SendCodeAsync(SmsScenes.Login, "13800000001", "13800000001", "challenge-1:abc123", TestContext.Current.CancellationToken));

        Assert.Contains("未注册", ex.Message);
    }
}