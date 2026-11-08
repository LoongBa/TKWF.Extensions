using System;
using System.Net;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Exceptions;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Utility.RateLimitChecks;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// AC-02（V0.13.1）IP 维度频控用例——登录门面（密码/短信）与短信发送面 IRateLimitCheck IP 点检查：
/// <c>login-ip:{ip}</c>（PasswordPerHourPerIp 默认 20/h）+ <c>sms-ip:{ip}</c> 单桶（SmsHourlyLimitPerIp 默认 20/h——
/// 登录 + 发送共用同一配额，防"换号不换 IP"刷验证码/刷登录双口，任一端口消费同一 20/h 配额）。
/// <para>生产路径等价：StubDomainUser 直构门面（DataService 经 User.Use&lt;具体类&gt;() 直建）+ MemoryRateLimitCheck
/// （Initializer fallback 同款）+ FakeIHttpContextAccessor（服务端 RemoteIpAddress 取信源模拟——不可由客户端伪造）。</para>
/// </summary>
public class LoginRateLimitIpTests
{
    // ── 测试桩 ─────────────────────────────────────────────────────────

    /// <summary>Fake IHttpContextAccessor——模拟服务端 RemoteIpAddress 取信源；ip=null 表示 IHttpContextAccessor 未注册/无帧（HttpContext null → unknown 桶）。</summary>
    private sealed class FakeHttpContextAccessor(string? ip) : IHttpContextAccessor
    {
        public HttpContext? HttpContext { get; set; } = Create(ip);

        private static HttpContext? Create(string? ip)
        {
            if (ip is null) return null;
            var ctx = new DefaultHttpContext();
            ctx.Connection.RemoteIpAddress = IPAddress.Parse(ip);
            return ctx;
        }
    }

    private static readonly FakeHttpContextAccessor Ip10 = new("10.0.0.10");
    private static readonly FakeHttpContextAccessor Ip20 = new("10.0.0.20");

    private static StubDomainUser CreateStub()
    {
        // SmsAuthenticationProvider 内部经 User.Use<ISmsVerificationService>() 校验验证码——注册短信验证门面
        //（dev mock：无 sender，验证码日志输出；IP 频控用例只关心认证失败语义，不关心验证码有效性）
        return AuthenticationTestHost.CreateStub(
            AuthenticationTestHost.CreateInMemoryFreeSql(),
            register: (services, u) => services.AddSingleton<ISmsVerificationService>(
                new SmsVerificationService(u, null, Options.Create(AuthenticationTestHost.CreateOptions()), NullLogger<SmsVerificationService>.Instance)));
    }

    private static string NewSalt()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    private static string ClientHash(string password, byte[] salt)
        => Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32));

    private static PasswordLoginService CreatePasswordLoginService(StubDomainUser stub, AuthCenterOptions options, IRateLimitCheck rateLimit, IHttpContextAccessor? accessor)
        => new(stub,
            [new PasswordAuthenticationProvider(stub, new FakeCredentialProtector(), NullLogger<PasswordAuthenticationProvider>.Instance)],
            Options.Create(options), rateLimit, NullLogger<PasswordLoginService>.Instance, accessor);

    private static SmsLoginService CreateSmsLoginService(StubDomainUser stub, AuthCenterOptions options, IRateLimitCheck rateLimit, IHttpContextAccessor? accessor)
        => new(stub,
            [new SmsAuthenticationProvider(stub, NullLogger<SmsAuthenticationProvider>.Instance)],
            Options.Create(options), NullLogger<SmsLoginService>.Instance, rateLimit, accessor);

    private static SmsVerificationService CreateSmsVerificationService(StubDomainUser stub, AuthCenterOptions options, IRateLimitCheck rateLimit, IHttpContextAccessor? accessor)
        => new(stub, new FakeSender(), Options.Create(options), NullLogger<SmsVerificationService>.Instance, rateLimit, accessor);

    private sealed class FakeSender : ISmsSender
    {
        public int SendCount { get; private set; }
        public Task SendAsync(SmsMessage message, System.Threading.CancellationToken ct = default)
        {
            SendCount++;
            return Task.CompletedTask;
        }
    }

    // ── AC-02 用例 ─────────────────────────────────────────────────────

    [Fact]
    public async Task PasswordLogin_IpBurst_429()
    {
        // 同一 IP 连续 PasswordPerHourPerIp 次（轮换 identifier——identifier 维度不干扰）→ 第 N+1 次 429 PASSWORD_RATE_LIMITED
        var stub = CreateStub();
        var options = AuthenticationTestHost.CreateOptions();
        options.LoginProtection.PasswordPerHourPerIp = 3;
        var rateLimit = new MemoryRateLimitCheck();
        var service = CreatePasswordLoginService(stub, options, rateLimit, Ip10);

        var salt = NewSalt();
        for (var i = 0; i < 3; i++)
        {
            // 前 3 次：IP 检查 pass（耗尽额度）→ identifier 点检查（轮换新键不干扰）→ 认证失败（无账号）→ DomainException
            await Assert.ThrowsAsync<DomainException>(() =>
                service.LoginAsync(new PasswordLoginRequest($"u-ip-burst-{i}", ClientHash("x", Convert.FromHexString(salt)), salt), ct: TestContext.Current.CancellationToken));
        }

        var ex = await Assert.ThrowsAsync<RateLimitedException>(() =>
            service.LoginAsync(new PasswordLoginRequest("u-ip-burst-3", ClientHash("x", Convert.FromHexString(salt)), salt), ct: TestContext.Current.CancellationToken));
        Assert.Equal("PASSWORD_RATE_LIMITED", ex.ErrorCode);
    }

    [Fact]
    public async Task SmsLogin_IpBurst_429()
    {
        // 同一 IP 轮换手机号登录超 SmsHourlyLimitPerIp（sms-ip:{ip} 桶）→ 429 SMS_RATE_LIMITED
        var stub = CreateStub();
        var options = AuthenticationTestHost.CreateOptions();
        options.LoginProtection.SmsHourlyLimitPerIp = 3;
        var rateLimit = new MemoryRateLimitCheck();
        var service = CreateSmsLoginService(stub, options, rateLimit, Ip10);

        for (var i = 0; i < 3; i++)
        {
            // 前 3 次：IP 检查 pass（耗尽额度）→ 认证失败（无验证码/无账号）→ DomainException（认证失败直透）
            await Assert.ThrowsAsync<DomainException>(() =>
                service.LoginAsync(new SmsLoginRequest($"1380000{i:D4}", "000000"), ct: TestContext.Current.CancellationToken));
        }

        var ex = await Assert.ThrowsAsync<RateLimitedException>(() =>
            service.LoginAsync(new SmsLoginRequest("13800009999", "000000"), ct: TestContext.Current.CancellationToken));
        Assert.Equal("SMS_RATE_LIMITED", ex.ErrorCode);
    }

    [Fact]
    public async Task SmsIpBucket_SharedBetweenSendAndLogin()
    {
        // 发送 + 登录共用 SmsHourlyLimitPerIp 单桶（key 恒 sms-ip:{ip}）——正向：先发送打满再登录 → 登录被拒；
        // 反向：先登录打满再发送 → 发送被拒（P1-1 单桶语义契约激活断言——任一端口消费同一配额）
        var stubSend = CreateStub();
        var stubLogin = CreateStub();
        var options = AuthenticationTestHost.CreateOptions();
        options.LoginProtection.SmsHourlyLimitPerIp = 3;
        var rateLimit = new MemoryRateLimitCheck();
        var send = CreateSmsVerificationService(stubSend, options, rateLimit, Ip20);
        var login = CreateSmsLoginService(stubLogin, options, rateLimit, Ip20);

        // 正向：发送面打满（轮换手机号——50s 重发间隔不干扰，IP 桶扣同额）
        for (var i = 0; i < 3; i++)
            await send.SendCodeAsync(new SendSmsCodeRequest($"1390000{i:D4}", SmsScenes.Login), ct: TestContext.Current.CancellationToken);

        // 同 IP 登录 → sms-ip 桶耗尽 → 登录被拒（SMS_RATE_LIMITED）
        var loginEx = await Assert.ThrowsAsync<RateLimitedException>(() =>
            login.LoginAsync(new SmsLoginRequest("13900009999", "000000"), ct: TestContext.Current.CancellationToken));
        Assert.Equal("SMS_RATE_LIMITED", loginEx.ErrorCode);

        // 反向：新 IP 新桶打满登录 → 同 IP 发送被拒
        var rateLimitReverse = new MemoryRateLimitCheck();
        var sendReverse = CreateSmsVerificationService(CreateStub(), options, rateLimitReverse, Ip10);
        var loginReverse = CreateSmsLoginService(CreateStub(), options, rateLimitReverse, Ip10);
        for (var i = 0; i < 3; i++)
            await Assert.ThrowsAsync<DomainException>(() =>
                loginReverse.LoginAsync(new SmsLoginRequest($"1370000{i:D4}", "000000"), ct: TestContext.Current.CancellationToken));

        var sendEx = await Assert.ThrowsAsync<RateLimitedException>(() =>
            sendReverse.SendCodeAsync(new SendSmsCodeRequest("13700009999", SmsScenes.Login), ct: TestContext.Current.CancellationToken));
        Assert.Equal("SMS_RATE_LIMITED", sendEx.ErrorCode);
    }

    [Fact]
    public async Task IpKey_Unknown_Fallback()
    {
        // 无 IHttpContextAccessor（HttpContext null——非 Web 调用路径）→ unknown 桶（fail-closed 不空放：额度耗尽仍拒）
        var stub = CreateStub();
        var options = AuthenticationTestHost.CreateOptions();
        options.LoginProtection.PasswordPerHourPerIp = 2;
        var rateLimit = new MemoryRateLimitCheck();
        var service = CreatePasswordLoginService(stub, options, rateLimit, accessor: null);

        var salt = NewSalt();
        for (var i = 0; i < 2; i++)
            await Assert.ThrowsAsync<DomainException>(() =>
                service.LoginAsync(new PasswordLoginRequest($"u-unknown-{i}", ClientHash("x", Convert.FromHexString(salt)), salt), ct: TestContext.Current.CancellationToken));

        var ex = await Assert.ThrowsAsync<RateLimitedException>(() =>
            service.LoginAsync(new PasswordLoginRequest("u-unknown-2", ClientHash("x", Convert.FromHexString(salt)), salt), ct: TestContext.Current.CancellationToken));
        Assert.Equal("PASSWORD_RATE_LIMITED", ex.ErrorCode);
    }

    [Fact]
    public async Task IdentifierRateLimit_StillEffective()
    {
        // 既有 pwd:login:{identifier} 5 次/10min 不回归——identifier 维度独立于 IP 维度（IP 桶不干扰 identifier 桶）
        var stub = CreateStub();
        var options = AuthenticationTestHost.CreateOptions();
        options.LoginProtection.PasswordPerHourPerIp = 20;   // 保持默认高额度——确保拒绝源为 identifier 桶而非 IP 桶
        var rateLimit = new MemoryRateLimitCheck();
        var service = CreatePasswordLoginService(stub, options, rateLimit, Ip10);

        var salt = NewSalt();
        for (var i = 0; i < 5; i++)
        {
            await Assert.ThrowsAsync<DomainException>(() =>
                service.LoginAsync(new PasswordLoginRequest("u-id-rate", ClientHash("x", Convert.FromHexString(salt)), salt), ct: TestContext.Current.CancellationToken));
        }

        // 第 6 次同 identifier → PASSWORD_RATE_LIMITED（identifier 点检查 5 次/10min 生效）
        var ex = await Assert.ThrowsAsync<RateLimitedException>(() =>
            service.LoginAsync(new PasswordLoginRequest("u-id-rate", ClientHash("x", Convert.FromHexString(salt)), salt), ct: TestContext.Current.CancellationToken));
        Assert.Equal("PASSWORD_RATE_LIMITED", ex.ErrorCode);
    }
}