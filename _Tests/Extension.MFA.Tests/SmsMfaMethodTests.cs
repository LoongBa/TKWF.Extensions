using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.MFA;

namespace TKWF.Ext.MFA.Tests;

/// <summary>
/// SMS MFA 方法行为测试（public IMfaService 门面驱动——SMS 渠道经记录型 Fake 断言）。
/// <para>覆盖方案 §五 SmsMfaMethod 行：发码/验证/单次消费、TTL 内重发拒绝、过期挑战失败、
/// 并发单次消费竞态（Oracle C1——文件模式 SQLite）。</para>
/// </summary>
public class SmsMfaMethodTests
{
    // ── 私有助手 ──

    private static string NewUserId() => Guid.NewGuid().ToString("N");

    /// <summary>从短信内容提取 6 位验证码（"您的 MFA 验证码是 {code}，..."）。</summary>
    private static string ExtractSmsCode(string content)
    {
        var match = Regex.Match(content, @"验证码是 (\d{6})");
        Assert.True(match.Success, $"无法从短信内容提取 6 位码：{content}");
        return match.Groups[1].Value;
    }

    private static string Sha256Hex(string value)
        => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();

    /// <summary>快捷：SMS 绑定 + 确认激活（返回确认码——即绑定时发送的 6 位码）。</summary>
    private static async Task<string> EnableSmsAsync(MfaTestHost host, string userId, string phone)
    {
        var enroll = await host.Mfa.EnrollAsync(userId, "sms", new MfaEnrollContext(Phone: phone));
        var code = ExtractSmsCode(host.SmsSender.Messages[^1].Content);
        await host.Mfa.ConfirmEnrollAsync(userId, "sms", enroll.EnrollToken, code);
        return code;
    }

    // ── 发码/验证/单次消费 ──

    [Fact]
    public async Task Send_Verify_SingleUse_FullFlow()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();
        const string phone = "13900000000";

        // 绑定时发码（SendCount=1）→ 确认激活（确认码单次消费）
        var enroll = await host.Mfa.EnrollAsync(userId, "sms", new MfaEnrollContext(Phone: phone));
        Assert.Equal(1, host.SmsSender.SendCount);
        var enrollCode = ExtractSmsCode(host.SmsSender.Messages[0].Content);
        await host.Mfa.ConfirmEnrollAsync(userId, "sms", enroll.EnrollToken, enrollCode);

        // 激活后挑战发码（SendCount=2）→ 正确码通过
        var challenge = await host.Mfa.RequestChallengeAsync(userId, "sms");
        Assert.Equal(2, host.SmsSender.SendCount);
        var challengeCode = ExtractSmsCode(host.SmsSender.Messages[1].Content);
        Assert.True(await host.Mfa.VerifyChallengeAsync(userId, "sms", challenge.ChallengeId, challengeCode));

        // 单次消费——同 challengeId 复用同码失败（统一 false 防枚举）
        Assert.False(await host.Mfa.VerifyChallengeAsync(userId, "sms", challenge.ChallengeId, challengeCode));
    }

    [Fact]
    public async Task Verify_WrongCode_ReturnsFalse()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();
        await EnableSmsAsync(host, userId, "13900000001");

        var challenge = await host.Mfa.RequestChallengeAsync(userId, "sms");
        var code = ExtractSmsCode(host.SmsSender.Messages[^1].Content);

        // 错码（不同于落库码）→ 统一 false（恒定时间比对）
        Assert.False(await host.Mfa.VerifyChallengeAsync(userId, "sms", challenge.ChallengeId, WrongCode(code)));
    }

    /// <summary>构造与正确码不同的 6 位码（取反最后一位）。</summary>
    private static string WrongCode(string code)
        => code[..^1] + (code[^1] == '9' ? '8' : '9');

    // ── TTL 内重发拒绝 ──

    [Fact]
    public async Task Resend_WithinTtl_Rejected_NoSecondSend()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();
        await EnableSmsAsync(host, userId, "13900000002");

        // 挑战发码成功（SendCount=2——含绑定 1 次）
        var challenge = await host.Mfa.RequestChallengeAsync(userId, "sms");
        Assert.Equal(2, host.SmsSender.SendCount);

        // TTL 内活动挑战未消费 → 重发拒绝（InvalidOperationException 含"短信验证码已发送"）
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Mfa.RequestChallengeAsync(userId, "sms"));
        Assert.Contains("短信验证码已发送", ex.Message);
        Assert.Equal(2, host.SmsSender.SendCount);   // 未重发

        // 消费后 → 可重发（SendCount=3）
        var code = ExtractSmsCode(host.SmsSender.Messages[^1].Content);
        Assert.True(await host.Mfa.VerifyChallengeAsync(userId, "sms", challenge.ChallengeId, code));
        var challenge2 = await host.Mfa.RequestChallengeAsync(userId, "sms");
        Assert.Equal(3, host.SmsSender.SendCount);
        Assert.False(string.IsNullOrEmpty(challenge2.ChallengeId));
    }

    // ── 过期挑战验证失败（测试侧创建过去 ExpireAt 票据） ──

    [Fact]
    public async Task ExpiredChallenge_Verify_Fails()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();
        await EnableSmsAsync(host, userId, "13900000003");

        // 直接创建过期挑战（测试侧操纵时间轴——ExpireAt 过去 → GetActiveById 视为失效）
        const string code = "654321";
        var expired = await host.Challenges.CreateAsync(new MfaChallengeEntity
        {
            UserId = userId,
            Method = "sms",
            CodeHash = Sha256Hex(code),
            ExpireAt = DateTime.UtcNow.AddHours(-24),
            CreateTime = DateTime.UtcNow,
        });

        // 码正确但票据过期 → 统一 false
        Assert.False(await host.Mfa.VerifyChallengeAsync(userId, "sms", expired.Id.ToString(), code));
    }

    // ── 并发单次消费竞态（Oracle C1——文件模式 SQLite） ──

    [Fact]
    public async Task ConcurrentVerify_SameChallengeId_ExactlyOneSucceeds()
    {
        // Oracle C1：挑战单次消费并发竞态——两并发 VerifyChallengeAsync 同 challengeId → 恰一成功一失败
        //（IsConsumed 原子翻转；文件模式 SQLite 多连接共享——:memory: 单连接池并发争用 ObjectPool.Get() 超时先例）
        using var host = MfaTestHost.CreateFile(out _);
        var userId = NewUserId();
        await EnableSmsAsync(host, userId, "13900000004");

        var challenge = await host.Mfa.RequestChallengeAsync(userId, "sms");
        var code = ExtractSmsCode(host.SmsSender.Messages[^1].Content);

        // 双独立 scope——每 scope 解析独立 IMfaService → 独立 ChallengeDataService/UnitOfWorkManager
        //（消除根容器单一实例→串行化→假绿；生产 ASP.NET 每请求一 scope 的真实形态）
        using var scope1 = host.ServiceProvider.CreateScope();
        using var scope2 = host.ServiceProvider.CreateScope();
        var mfa1 = scope1.ServiceProvider.GetRequiredService<IMfaService>();
        var mfa2 = scope2.ServiceProvider.GetRequiredService<IMfaService>();

        var results = await Task.WhenAll(
            mfa1.VerifyChallengeAsync(userId, "sms", challenge.ChallengeId, code, CancellationToken.None),
            mfa2.VerifyChallengeAsync(userId, "sms", challenge.ChallengeId, code, CancellationToken.None));

        Assert.Equal(1, results.Count(r => r));   // 恰一成功一失败
    }

    // ── 发送失败清理孤儿挑战行（Oracle7 C1——防阻塞重发至 TTL） ──

    [Fact]
    public async Task SendSmsFailure_CleansOrphanChallenge_AllowsImmediateResend()
    {
        // Oracle7 C1：发送抛异常 → 孤儿挑战行清理（码未发出挑战无意义）→ 渠道恢复后立即重发成功
        //（修复前孤儿挑战阻塞重发至 TTL 5min——体验缺陷）
        var sender = new SwitchableMfaSmsSender();
        using var host = MfaTestHost.CreateWithSmsSender(sender);
        var userId = NewUserId();
        const string phone = "13900000009";

        // 绑定发送成功（渠道初始正常）→ 确认激活
        var enroll = await host.Mfa.EnrollAsync(userId, "sms", new MfaEnrollContext(Phone: phone));
        Assert.Equal(1, sender.SendCount);
        var enrollCode = ExtractSmsCode(sender.Messages[0].Content);
        await host.Mfa.ConfirmEnrollAsync(userId, "sms", enroll.EnrollToken, enrollCode);

        // 模拟渠道故障——挑战发码抛异常 → 孤儿挑战行被清理（无活动挑战阻塞重发）
        sender.SetThrowOnSend(true);
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mfa.RequestChallengeAsync(userId, "sms"));
        Assert.Empty(await host.Challenges.GetActiveByUserMethodAsync(userId, "sms"));

        // 渠道恢复 → 立即重发成功（未被孤儿挑战阻塞至 TTL）→ 正确码可验证
        sender.SetThrowOnSend(false);
        var challenge = await host.Mfa.RequestChallengeAsync(userId, "sms");
        Assert.Equal(2, sender.SendCount);
        var code = ExtractSmsCode(sender.Messages[^1].Content);
        Assert.True(await host.Mfa.VerifyChallengeAsync(userId, "sms", challenge.ChallengeId, code));
    }

    [Fact]
    public async Task SmsSendFailure_CleansChallenge_NoOrphanBlocksResend()
    {
        // Oracle7 C1 简化验收：恒抛异常渠道 + 直接建激活绑定 → 挑战发码失败 → 无活动挑战残留
        using var host = MfaTestHost.CreateWithSmsSender(new ThrowingMfaSmsSender());
        var userId = NewUserId();

        // 直接建已激活绑定（跳过绑定时发送——渠道恒抛异常无法走 Enroll 流程）
        await host.Secrets.CreateAsync(new MfaSecretEntity
        {
            UserId = userId,
            Method = "sms",
            Phone = "13900000008",
            IsConfirmed = true,
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        });

        // 挑战发码 → 发送失败抛异常（异常自然传播不吞）→ 孤儿挑战行已清理
        await Assert.ThrowsAsync<InvalidOperationException>(() => host.Mfa.RequestChallengeAsync(userId, "sms"));
        Assert.Empty(await host.Challenges.GetActiveByUserMethodAsync(userId, "sms"));
    }
}

/// <summary>
/// 发送恒抛异常的 SMS Fake——模拟渠道故障（Oracle7 C1 发送失败清理孤儿挑战行用例）。
/// </summary>
internal sealed class ThrowingMfaSmsSender : IMfaSmsSender
{
    public Task SendAsync(MfaSmsMessage message, CancellationToken ct = default)
        => throw new InvalidOperationException("SMS 渠道模拟故障——发送失败");
}

/// <summary>
/// 可切换行为的 SMS Fake——测试经 <see cref="SetThrowOnSend"/> 模拟渠道故障/恢复
/// （Oracle7 C1 验证失败清理孤儿挑战 + 渠道恢复后立即重发成功）。
/// <para>仅成功发送计入 <see cref="Messages"/>（对齐 RecordingMfaSmsSender 语义）；行为切换线程安全。</para>
/// </summary>
internal sealed class SwitchableMfaSmsSender : IMfaSmsSender
{
    private readonly object _gate = new();
    private readonly List<MfaSmsMessage> _messages = new();
    private bool _throwOnSend;

    /// <summary>切换发送行为（true = 发送抛 <see cref="InvalidOperationException"/> 模拟渠道故障）。</summary>
    public void SetThrowOnSend(bool throwOnSend)
    {
        lock (_gate) _throwOnSend = throwOnSend;
    }

    /// <summary>已发送消息（按发送顺序；仅成功发送捕获）。</summary>
    public IReadOnlyList<MfaSmsMessage> Messages
    {
        get { lock (_gate) return _messages.ToArray(); }
    }

    /// <summary>发送总次数（成功发送才计数）。</summary>
    public int SendCount
    {
        get { lock (_gate) return _messages.Count; }
    }

    public Task SendAsync(MfaSmsMessage message, CancellationToken ct = default)
    {
        bool shouldThrow;
        lock (_gate) shouldThrow = _throwOnSend;
        if (shouldThrow)
            throw new InvalidOperationException("SMS 渠道模拟故障——发送失败");
        lock (_gate) _messages.Add(message);
        return Task.CompletedTask;
    }
}
