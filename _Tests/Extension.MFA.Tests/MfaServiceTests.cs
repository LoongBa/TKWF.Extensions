using System;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.MFA;

namespace TKWF.Ext.MFA.Tests;

/// <summary>
/// MfaService 门面核心测试（public IMfaService 驱动）——覆盖方案 §五 MfaService 行：
/// TOTP/SMS 绑定全流程、EnrollToken 过期/重放（Oracle C3）、挑战-验证编排（统一 false）、
/// 尝试频控 + 恢复码同窗口（Oracle C4）、恢复码（P9/C1 明文不落库 + 并发单次消费）、防枚举（Oracle Q6）。
/// <para>V4.10.67 R3（IRateLimitCheck 迁移）：频控经 DI 注入点检查原语——生产路径 host 走
/// MFAExtensionInitializer TryAddSingleton fallback（MemoryRateLimitCheck，每宿主独立实例）；
/// 每用例独立 userId（key = mfa:verify:user:{userId}:{method} / mfa:sms:user:{userId}）保证隔离。</para>
/// </summary>
public class MfaServiceTests
{
    // ── 私有助手 ──

    private static string NewUserId() => Guid.NewGuid().ToString("N");

    /// <summary>从 provisioning URI 提取 Base32 secret 参数。</summary>
    private static string ExtractSecret(string provisioningUri)
    {
        var match = Regex.Match(provisioningUri, "[?&]secret=([^&]+)");
        Assert.True(match.Success, $"无法从 provisioning URI 提取 secret：{provisioningUri}");
        return match.Groups[1].Value;
    }

    /// <summary>从短信内容提取 6 位验证码（"您的 MFA 验证码是 {code}，..."）。</summary>
    private static string ExtractSmsCode(string content)
    {
        var match = Regex.Match(content, @"验证码是 (\d{6})");
        Assert.True(match.Success, $"无法从短信内容提取 6 位码：{content}");
        return match.Groups[1].Value;
    }

    /// <summary>按默认 Options（30s 步长 / 6 位）计算当前时间步 TOTP 码。</summary>
    private static string TotpCode(string secret)
        => TotpGenerator.ComputeCode(secret, DateTimeOffset.UtcNow.ToUnixTimeSeconds(), 30, 6);

    /// <summary>快捷：TOTP 绑定 + 确认激活（返回 Base32 secret）。</summary>
    private static async Task<string> EnableTotpAsync(MfaTestHost host, string userId)
    {
        var enroll = await host.Mfa.EnrollAsync(userId, "totp", new MfaEnrollContext(Issuer: "TestCo", DisplayName: "alice"));
        var secret = ExtractSecret(enroll.ProvisioningUri!);
        await host.Mfa.ConfirmEnrollAsync(userId, "totp", enroll.EnrollToken, TotpCode(secret));
        return secret;
    }

    /// <summary>快捷：SMS 绑定 + 确认激活（返回确认码）。</summary>
    private static async Task<string> EnableSmsAsync(MfaTestHost host, string userId, string phone)
    {
        var enroll = await host.Mfa.EnrollAsync(userId, "sms", new MfaEnrollContext(Phone: phone));
        var code = ExtractSmsCode(host.SmsSender.Messages[^1].Content);
        await host.Mfa.ConfirmEnrollAsync(userId, "sms", enroll.EnrollToken, code);
        return code;
    }

    // ── Enroll/Confirm/Disable 全流程（TOTP + SMS） ──

    [Fact]
    public async Task TotpEnroll_Confirm_Activate_Disable_FullFlow()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();

        // 绑定前：未启用
        Assert.False(await host.Mfa.IsMfaEnabledAsync(userId));

        // Enroll——生成 secret + provisioning URI + EnrollToken
        var enroll = await host.Mfa.EnrollAsync(userId, "totp", new MfaEnrollContext(Issuer: "TestCo", DisplayName: "alice"));
        Assert.Equal("totp", enroll.Method);
        Assert.False(string.IsNullOrEmpty(enroll.EnrollToken));
        Assert.NotNull(enroll.ProvisioningUri);
        Assert.StartsWith("otpauth://totp/", enroll.ProvisioningUri);

        // Confirm——按 provisioning URI secret 算一次码 → 激活 + 首次恢复码一次性返回
        var secret = ExtractSecret(enroll.ProvisioningUri!);
        var recoveryCodes = await host.Mfa.ConfirmEnrollAsync(userId, "totp", enroll.EnrollToken, TotpCode(secret));
        Assert.Equal(8, recoveryCodes.Count);
        Assert.True(await host.Mfa.IsMfaEnabledAsync(userId));

        // 启用方法列表
        var methods = await host.Mfa.GetEnabledMethodsAsync(userId);
        Assert.Equal(2, methods.Count);
        Assert.True(methods.Single(m => m.Method == "totp").IsEnabled);
        Assert.False(methods.Single(m => m.Method == "sms").IsEnabled);

        // 挑战-验证编排
        var challenge = await host.Mfa.RequestChallengeAsync(userId, "totp");
        Assert.False(string.IsNullOrEmpty(challenge.ChallengeId));
        Assert.True(await host.Mfa.VerifyChallengeAsync(userId, "totp", challenge.ChallengeId, TotpCode(secret)));

        // 单次消费——同 challengeId 再验失败（统一 false 防枚举）
        Assert.False(await host.Mfa.VerifyChallengeAsync(userId, "totp", challenge.ChallengeId, TotpCode(secret)));

        // Disable——解绑 + 级联删除绑定/恢复码
        await host.Mfa.DisableAsync(userId, "totp");
        Assert.False(await host.Mfa.IsMfaEnabledAsync(userId));
        Assert.Empty(await host.Secrets.GetByUserAsync(userId));
        Assert.Empty(await host.RecoveryCodes.GetByUserAsync(userId));

        // 解绑后挑战——统一"已发起"（假 challengeId 验证必败）
        var ch2 = await host.Mfa.RequestChallengeAsync(userId, "totp");
        Assert.False(string.IsNullOrEmpty(ch2.ChallengeId));
        Assert.False(await host.Mfa.VerifyChallengeAsync(userId, "totp", ch2.ChallengeId, TotpCode(secret)));
    }

    [Fact]
    public async Task SmsEnroll_Confirm_Activate_FullFlow()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();
        const string phone = "13800138000";

        // Enroll——绑手机 + 自动发码（记录型 Fake 收到）
        var enroll = await host.Mfa.EnrollAsync(userId, "sms", new MfaEnrollContext(Phone: phone));
        Assert.Equal("sms", enroll.Method);
        Assert.False(string.IsNullOrEmpty(enroll.EnrollToken));
        Assert.Null(enroll.ProvisioningUri);
        Assert.Equal(1, host.SmsSender.SendCount);
        Assert.Equal(phone, host.SmsSender.Messages[0].Phone);

        // Confirm——Fake 捕获的 6 位码确认激活
        var code = ExtractSmsCode(host.SmsSender.Messages[0].Content);
        var recoveryCodes = await host.Mfa.ConfirmEnrollAsync(userId, "sms", enroll.EnrollToken, code);
        Assert.Equal(8, recoveryCodes.Count);
        Assert.True(await host.Mfa.IsMfaEnabledAsync(userId));
    }

    [Fact]
    public async Task GetEnabledMethods_NoEnrollment_BothFalse()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();

        var methods = await host.Mfa.GetEnabledMethodsAsync(userId);

        Assert.Equal(2, methods.Count);
        Assert.False(methods.Single(m => m.Method == "totp").IsEnabled);
        Assert.False(methods.Single(m => m.Method == "sms").IsEnabled);
    }

    // ── EnrollToken 过期/重放（Oracle C3） ──

    [Fact]
    public async Task EnrollToken_Expired_Fails()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();
        var enroll = await host.Mfa.EnrollAsync(userId, "totp", new MfaEnrollContext());
        var secret = ExtractSecret(enroll.ProvisioningUri!);

        // 测试侧操纵时间轴——把待激活绑定 EnrollExpireAt 改为过去（经 DataService 更新）
        var pending = await host.Secrets.GetPendingSecretAsync(userId, "totp");
        Assert.NotNull(pending);
        pending!.EnrollExpireAt = DateTime.UtcNow.AddHours(-24);
        await host.Secrets.UpdateAsync(pending);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Mfa.ConfirmEnrollAsync(userId, "totp", enroll.EnrollToken, TotpCode(secret)));

        Assert.Contains("绑定确认失败", ex.Message);
        Assert.False(await host.Mfa.IsMfaEnabledAsync(userId));   // 激活未发生
    }

    [Fact]
    public async Task EnrollToken_Replay_AfterActivation_Fails()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();
        var enroll = await host.Mfa.EnrollAsync(userId, "totp", new MfaEnrollContext());
        var secret = ExtractSecret(enroll.ProvisioningUri!);

        await host.Mfa.ConfirmEnrollAsync(userId, "totp", enroll.EnrollToken, TotpCode(secret));

        // 激活成功后同 enrollToken 重放——待激活绑定已消失（单次消费）→ 统一失败
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Mfa.ConfirmEnrollAsync(userId, "totp", enroll.EnrollToken, TotpCode(secret)));

        Assert.Contains("绑定确认失败", ex.Message);
    }

    [Fact]
    public async Task EnrollTokenHash_PersistedBeforeActivation_ClearedAfter()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();
        var enroll = await host.Mfa.EnrollAsync(userId, "totp", new MfaEnrollContext());
        var secret = ExtractSecret(enroll.ProvisioningUri!);

        // 激活前——EnrollTokenHash 落库（SHA256 hex）+ 待激活态
        var before = (await host.Secrets.GetByUserAsync(userId)).Single();
        Assert.False(before.IsConfirmed);
        Assert.NotNull(before.EnrollTokenHash);
        Assert.Equal(64, before.EnrollTokenHash!.Length);
        Assert.NotNull(before.EnrollExpireAt);
        Assert.NotNull(before.SecretEncrypted);

        await host.Mfa.ConfirmEnrollAsync(userId, "totp", enroll.EnrollToken, TotpCode(secret));

        // 激活后——EnrollTokenHash/EnrollExpireAt 清空；secret 密文保持（CanUpdate=false 不可改）
        var after = (await host.Secrets.GetByUserAsync(userId)).Single();
        Assert.True(after.IsConfirmed);
        Assert.Null(after.EnrollTokenHash);
        Assert.Null(after.EnrollExpireAt);
        Assert.Equal(before.SecretEncrypted, after.SecretEncrypted);
    }

    // ── 挑战-验证编排：不存在/已消费/过期/非本人 → 统一 false ──

    [Fact]
    public async Task ChallengeVerify_Nonexistent_Consumed_Expired_NotOwner_AllUnifiedFalse()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();
        var secret = await EnableTotpAsync(host, userId);

        // ① 挑战不存在
        Assert.False(await host.Mfa.VerifyChallengeAsync(userId, "totp", "999999999", TotpCode(secret)));

        // ② 正常验证成功 → 同挑战再验（已消费）失败
        var challenge = await host.Mfa.RequestChallengeAsync(userId, "totp");
        Assert.True(await host.Mfa.VerifyChallengeAsync(userId, "totp", challenge.ChallengeId, TotpCode(secret)));
        Assert.False(await host.Mfa.VerifyChallengeAsync(userId, "totp", challenge.ChallengeId, TotpCode(secret)));

        // ③ 过期挑战（测试侧直接创建过去 ExpireAt 的票据——GetActiveById 视为失效）
        var expired = await host.Challenges.CreateAsync(new MfaChallengeEntity
        {
            UserId = userId,
            Method = "totp",
            CodeHash = null,
            ExpireAt = DateTime.UtcNow.AddHours(-24),
            CreateTime = DateTime.UtcNow,
        });
        Assert.False(await host.Mfa.VerifyChallengeAsync(userId, "totp", expired.Id.ToString(), TotpCode(secret)));

        // ④ 非本人挑战（他用户活动票据 → 归属校验拒绝）
        var otherUser = NewUserId();
        var other = await host.Challenges.CreateAsync(new MfaChallengeEntity
        {
            UserId = otherUser,
            Method = "totp",
            CodeHash = null,
            ExpireAt = DateTime.UtcNow.AddHours(1),
            CreateTime = DateTime.UtcNow,
        });
        Assert.False(await host.Mfa.VerifyChallengeAsync(userId, "totp", other.Id.ToString(), TotpCode(secret)));
    }

    // ── 尝试频控：超限抛异常含剩余等待；恢复码验证同窗口生效（Oracle C4） ──

    [Fact]
    public async Task VerifyChallenge_RateLimitExceeded_Throws_WithRetryAfterSeconds()
    {
        using var host = MfaTestHost.Create(options: new MfaOptions { MaxVerifyAttemptsPerWindow = 1 });
        var userId = NewUserId();
        await EnableSmsAsync(host, userId, "13800000000");

        var challenge = await host.Mfa.RequestChallengeAsync(userId, "sms");
        var code = ExtractSmsCode(host.SmsSender.Messages[^1].Content);

        // 第 1 次验证（错码）——记录尝试，统一 false
        Assert.False(await host.Mfa.VerifyChallengeAsync(userId, "sms", challenge.ChallengeId, "000000"));

        // 第 2 次验证——per-(userId,method) 窗口超限（max=1）→ 抛异常含剩余等待
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Mfa.VerifyChallengeAsync(userId, "sms", challenge.ChallengeId, code));

        Assert.Contains("秒后重试", ex.Message);
    }

    [Fact]
    public async Task RecoveryCode_Verify_RateLimited_SameWindowPrimitive()
    {
        using var host = MfaTestHost.Create(options: new MfaOptions { MaxVerifyAttemptsPerWindow = 1 });
        var userId = NewUserId();
        var codes = await host.Mfa.GenerateRecoveryCodesAsync(userId);
        Assert.Equal(8, codes.Count);

        // 第 1 次恢复码验证 → 通过（记录尝试）
        Assert.True(await host.Mfa.VerifyRecoveryCodeAsync(userId, codes[0]));

        // 第 2 次——per-(UserId,"recovery") 窗口超限 → 抛异常（Oracle C4：恢复码 8 码 × 无限尝试可枚举，必须限流）
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Mfa.VerifyRecoveryCodeAsync(userId, codes[0]));

        Assert.Contains("秒后重试", ex.Message);
    }

    // ── 恢复码：格式/单次消费/错误码/再生成全量替换/明文不落库/并发同码恰一成功 ──

    [Fact]
    public async Task RecoveryCodes_Generate_Format_SingleUse_Regenerate_PlaintextNotStored()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();

        var codes = await host.Mfa.GenerateRecoveryCodesAsync(userId);
        Assert.Equal(8, codes.Count);
        // 格式：8 位字母数字（去易混淆字符 0/O/1/I/l——Oracle P9，32 字符字母表）
        foreach (var code in codes)
        {
            Assert.Equal(8, code.Length);
            Assert.Matches("^[23456789ABCDEFGHJKLMNPQRSTUVWXYZ]{8}$", code);
        }

        // 明文不落库——DB 仅 CodeHash（SHA256 hex 小写；Oracle C1 补充断言）
        var rows = await host.RecoveryCodes.GetByUserAsync(userId);
        Assert.Equal(8, rows.Count);
        Assert.All(rows, row =>
        {
            Assert.Equal(64, row.CodeHash.Length);
            Assert.True(codes.All(c => c != row.CodeHash), "恢复码明文不得等于落库 CodeHash");
        });

        // 验证正确码 → true；单次消费 → 再用失败
        Assert.True(await host.Mfa.VerifyRecoveryCodeAsync(userId, codes[0]));
        Assert.False(await host.Mfa.VerifyRecoveryCodeAsync(userId, codes[0]));

        // 错误码 → false（统一失败防枚举）
        Assert.False(await host.Mfa.VerifyRecoveryCodeAsync(userId, "ZZZZZZZZ"));

        // 再生成 = 全量替换（旧码失效、新码可用）
        var regenerated = await host.Mfa.GenerateRecoveryCodesAsync(userId);
        Assert.Equal(8, regenerated.Count);
        Assert.False(await host.Mfa.VerifyRecoveryCodeAsync(userId, codes[1]));    // 旧码失效
        Assert.True(await host.Mfa.VerifyRecoveryCodeAsync(userId, regenerated[0]));
    }

    [Fact]
    public async Task RecoveryCode_ConcurrentVerify_SameCode_ExactlyOneSucceeds()
    {
        // Oracle C1：恢复码单次消费并发竞态——同码并发验证恰一成功（文件模式 SQLite——:memory:
        // 单连接池并发争用 ObjectPool.Get() 超时先例）
        using var host = MfaTestHost.CreateFile(out _);
        var userId = NewUserId();
        var codes = await host.Mfa.GenerateRecoveryCodesAsync(userId);

        // 双独立 MfaService 实例（分层直构——各自独立 stub 用户 + DataService/UnitOfWorkManager 链）
        //（消除根容器单一实例→串行化→假绿；生产 ASP.NET 每请求一 scope 的真实形态）
        var mfa1 = MfaTestHost.BuildIsolatedMfaService(host.Fsql);
        var mfa2 = MfaTestHost.BuildIsolatedMfaService(host.Fsql);

        var results = await Task.WhenAll(
            mfa1.VerifyRecoveryCodeAsync(userId, codes[0], CancellationToken.None),
            mfa2.VerifyRecoveryCodeAsync(userId, codes[0], CancellationToken.None));

        Assert.Equal(1, results.Count(r => r));   // 恰一成功一失败
    }

    // ── 防枚举（Oracle Q6） ──

    [Fact]
    public async Task AntiEnumeration_NotEnabledUser_RequestChallengeUnified_VerifyFails_NoSmsSent()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();

        // 未启用用户 RequestChallenge → 统一"已发起"（随机假 challengeId——防用户存在性/绑定状态探测）
        var challenge = await host.Mfa.RequestChallengeAsync(userId, "totp");
        Assert.False(string.IsNullOrEmpty(challenge.ChallengeId));
        Assert.Equal("totp", challenge.Method);

        // 假 challengeId 验证必败（统一 false 不区分原因）
        Assert.False(await host.Mfa.VerifyChallengeAsync(userId, "totp", challenge.ChallengeId, "000000"));

        // SMS 未启用同样统一"已发起"——且不发码（防枚举面收敛：无短信计费副作用）
        var smsChallenge = await host.Mfa.RequestChallengeAsync(userId, "sms");
        Assert.False(string.IsNullOrEmpty(smsChallenge.ChallengeId));
        Assert.Equal(0, host.SmsSender.SendCount);
    }

    [Fact]
    public async Task AntiEnumeration_AlreadyEnabled_Enroll_ReturnsUnifiedResponse()
    {
        using var host = MfaTestHost.Create();
        var userId = NewUserId();
        await EnableTotpAsync(host, userId);

        // 已启用用户 EnrollAsync → 统一响应（EnrollToken=""，防绑定状态探测——消费方可据此跳绑定 UI）
        var enroll = await host.Mfa.EnrollAsync(userId, "totp", new MfaEnrollContext());

        Assert.Equal("totp", enroll.Method);
        Assert.Equal("", enroll.EnrollToken);
        Assert.Null(enroll.ProvisioningUri);
    }
}
