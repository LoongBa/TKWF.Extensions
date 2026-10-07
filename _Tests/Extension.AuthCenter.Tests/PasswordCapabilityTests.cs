using System;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain.AuthController;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Utility.RateLimitChecks;
using TKWF.Ext.SecurityLog;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// V0.9.0（B.9-B.11 + ADR-密码策略与口令协议 决策 1）密码能力用例——SecurePassword 协议（clientHash 语义，服务端零明文）：
/// PasswordAuthenticationProvider（解保护 + 组装解析 + FixedTimeEquals 比对）/ SetPasswordAsync（clientHash 只存不算，
/// AES-GCM 加密落库）/ ChangePasswordAsync（零明文验旧）/ 找回三通道 / 账号冻结（SecurityLog 直写降级）/
/// 密码策略（历史防重用）/ 频控（IRateLimitCheck 锁定 + AuthLoginAttempt COUNT 审计分支）/ UserProfile 档案读写。
/// <para>宿主：StubDomainUser 直构门面（走生产 NoAop 路径——DataService 经 User.Use&lt;具体类&gt;() 直建，
/// IEntityDAC 从 DI 解析）；ICredentialProtector/IRateLimitCheck 为基础设施 ctor 注入——测试直构显式传。</para>
/// <para>⚠️ 盐语义（SecurePassword 协议）：clientHash = hex(PBKDF2(password, salt, 600000, 32bytes))——
/// 测试内先生成 salt，用<b>同盐</b>算 clientHash 存库 + 提交验证（等价服务端 GetSaltAsync 下发路径）；
/// 冻结/解冻后登录验证必须复用存储盐，否则 PASSWORD_MISMATCH 掩盖 ACCOUNT_FROZEN/恢复断言。</para>
/// </summary>
public class PasswordCapabilityTests
{
    // ── 测试宿主辅助（SecurePassword 客户端语义） ──────────────────────

    private static ICredentialProtector CreateProtector() => new FakeCredentialProtector();
    private static IRateLimitCheck CreateRateLimit() => new MemoryRateLimitCheck();

    /// <summary>模拟客户端 PBKDF2（600000 迭代——DomainOptions.Auth.Pbkdf2Iterations 单一来源）→ clientHash hex。</summary>
    private static string ClientHash(string password, byte[] salt)
        => Convert.ToHexString(Rfc2898DeriveBytes.Pbkdf2(password, salt, 600_000, HashAlgorithmName.SHA256, 32));

    /// <summary>模拟客户端随机盐（32 bytes）→ salt hex。</summary>
    private static string NewSalt()
        => Convert.ToHexString(RandomNumberGenerator.GetBytes(32));

    /// <summary>直构"受保护的组装格式"blob（不经 SetPasswordAsync——TokenVersion 不 bump，供验旧/组装解析测试直接种子）。
    /// 组装 <c>{iterations}.{b64salt}.{b64hash}</c> → FakeProtector.Protect（base64 往返——协议格式 + Unprotect 解析路径全覆盖）。</summary>
    private static string AssembleProtectedClientHash(string clientHashHex, string saltHex, int iterations = 600_000)
    {
        var assembled = $"{iterations}.{Convert.ToBase64String(Convert.FromHexString(saltHex))}.{Convert.ToBase64String(Convert.FromHexString(clientHashHex))}";
        return new FakeCredentialProtector().Protect(System.Text.Encoding.UTF8.GetBytes(assembled));
    }

    /// <summary>直构 AuthAccountService（SecurePassword 协议依赖全套：SecurityLog/Domain/AuthCenter Options + ICredentialProtector + ILogger）。</summary>
    private static AuthAccountService CreateAuthAccountService(StubDomainUser stub)
        => new(stub,
            Options.Create(new SecurityLoggingOptions()),
            Options.Create(new DomainOptions()),                          // DomainOptions.Auth.Pbkdf2Iterations=600000
            Options.Create(AuthenticationTestHost.CreateOptions()),       // AuthCenterOptions（PasswordPolicy/LoginProtection）
            CreateProtector(),
            NullLogger<AuthAccountService>.Instance);

    /// <summary>种子账号（经生产路径 SetPasswordAsync 落 SecurePassword blob——clientHash 只存不算）。</summary>
    /// <returns>账号 + 存储盐（测试须用同盐算 clientHash 提交验证——SecurePassword 协议等价 GetSaltAsync 下发）。</returns>
    private static async Task<(AuthAccountEntity Account, string StoreSalt)> SeedAccountWithPasswordAsync(
        StubDomainUser stub, string uid, string password, string phone)
    {
        var account = new AuthAccountEntity { UId = uid, Phone = phone, AuthLevel = (int)AuthLevel.Phone };
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(account);

        var write = CreateAuthAccountService(stub);
        var storeSalt = NewSalt();
        await write.SetPasswordAsync(uid, ClientHash(password, Convert.FromHexString(storeSalt)), storeSalt);
        return (account, storeSalt);
    }

    // ── PasswordAuthenticationProvider（SecurePassword 验证） ───────────

    [Fact]
    public async Task PasswordProvider_ValidCredentials_Authenticates()
    {
        var stub = CreateStub(out _);
        var storeSalt = NewSalt();
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity
        {
            UId = "u-pp-1",
            Phone = "13800000001",
            AuthLevel = (int)AuthLevel.Phone,
        });
        var write = CreateAuthAccountService(stub);
        // 同盐：先算 clientHash 存库（SecurePassword 只存不算）——测试内先生成盐，用同盐算 clientHash 存 + 验证
        await write.SetPasswordAsync("u-pp-1", ClientHash("Secret123!", Convert.FromHexString(storeSalt)), storeSalt);

        var provider = new PasswordAuthenticationProvider(stub, CreateProtector(), NullLogger<PasswordAuthenticationProvider>.Instance);
        var result = await provider.AuthenticateAsync(new ProviderAuthenticateContext(
            Identifier: "13800000001", ClientHash: ClientHash("Secret123!", Convert.FromHexString(storeSalt)), Salt: storeSalt));

        Assert.True(result.Success);
        Assert.Equal("u-pp-1", result.UserId);
        Assert.Equal(AuthLevel.Phone, result.AuthLevel);
    }

    [Fact]
    public async Task PasswordProvider_WrongPassword_Fails()
    {
        var stub = CreateStub(out _);
        await SeedAccountWithPasswordAsync(stub, "u-pp-2", "Secret123!", "13800000002");
        var provider = new PasswordAuthenticationProvider(stub, CreateProtector(), NullLogger<PasswordAuthenticationProvider>.Instance);

        var salt = NewSalt();   // 错密码 + 不同盐 → clientHash 段必然不匹配
        var result = await provider.AuthenticateAsync(new ProviderAuthenticateContext(
            Identifier: "13800000002", ClientHash: ClientHash("wrong", Convert.FromHexString(salt)), Salt: salt));

        Assert.False(result.Success);
        Assert.Equal("PASSWORD_MISMATCH", result.FailReason);
    }

    [Fact]
    public async Task PasswordProvider_NoPasswordSet_FailsClosed()
    {
        var stub = CreateStub(out _);
        var account = new AuthAccountEntity { UId = "u-pp-3", Phone = "13800000003", AuthLevel = (int)AuthLevel.Phone };
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(account);
        var provider = new PasswordAuthenticationProvider(stub, CreateProtector(), NullLogger<PasswordAuthenticationProvider>.Instance);

        var salt = NewSalt();
        var result = await provider.AuthenticateAsync(new ProviderAuthenticateContext(
            Identifier: "u-pp-3", ClientHash: ClientHash("anything", Convert.FromHexString(salt)), Salt: salt));

        Assert.False(result.Success);
        Assert.Equal("PASSWORD_NOT_SET", result.FailReason);
    }

    [Fact]
    public async Task PasswordLoginService_EnabledAuthTypes_FiltersProvider()
    {
        // EnabledAuthTypes 不含 password → 门面返回 PASSWORD_PROVIDER_NOT_ENABLED（P1-1/P2-6 fail-closed）
        var stub = CreateStub(out _);
        var options = AuthenticationTestHost.CreateOptions();
        options.EnabledAuthTypes = ["sms", "wechat"];   // 显式不含 password
        var service = new PasswordLoginService(
            stub,
            [new PasswordAuthenticationProvider(stub, CreateProtector(), NullLogger<PasswordAuthenticationProvider>.Instance)],
            Options.Create(options),
            CreateRateLimit(),
            NullLogger<PasswordLoginService>.Instance);

        var salt = NewSalt();
        var result = await service.LoginAsync("u-anything", ClientHash("x", Convert.FromHexString(salt)), salt);

        Assert.False(result.Success);
        Assert.Equal("PASSWORD_PROVIDER_NOT_ENABLED", result.FailReason);
    }

    // ── SetPasswordAsync / ChangePasswordAsync（SecurePassword clientHash 语义） ────────

    [Fact]
    public async Task SetPassword_HashStored_AndTokenVersionIncremented()
    {
        var stub = CreateStub(out _);
        var account = new AuthAccountEntity { UId = "u-sp-1", Phone = "13800000101", AuthLevel = (int)AuthLevel.Phone, TokenVersion = 0 };
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(account);
        var write = CreateAuthAccountService(stub);

        var salt = NewSalt();
        var affected = await write.SetPasswordAsync("u-sp-1", ClientHash("NewSecret456!", Convert.FromHexString(salt)), salt);

        Assert.Equal(1, affected);
        var reloaded = await stub.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-sp-1");
        Assert.NotNull(reloaded);
        Assert.False(string.IsNullOrEmpty(reloaded!.PasswordHash));   // protected blob（AES-GCM 密文——非明文可读）
        Assert.NotEqual("NewSecret456!", reloaded.PasswordHash);       // 服务端零明文——存的是加密组装格式
        Assert.NotEqual(ClientHash("NewSecret456!", Convert.FromHexString(salt)), reloaded.PasswordHash); // 亦非 clientHash 明文
        Assert.Equal(1, reloaded.TokenVersion);                        // TokenVersion++（旧 refresh 失效）
    }

    [Fact]
    public async Task ChangePassword_WrongOldPassword_Throws()
    {
        var stub = CreateStub(out _);
        var storeSalt = NewSalt();
        // 直种组装格式 blob（不经 SetPasswordAsync——TokenVersion 保持 0，隔离验旧失败"不改写"断言）
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity
        {
            UId = "u-sp-2",
            Phone = "13800000102",
            AuthLevel = (int)AuthLevel.Phone,
            PasswordHash = AssembleProtectedClientHash(ClientHash("OldSecret123!", Convert.FromHexString(storeSalt)), storeSalt),
            TokenVersion = 0,
        });
        var write = CreateAuthAccountService(stub);

        var wrongOldSalt = NewSalt();
        var newSalt = NewSalt();
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => write.ChangePasswordAsync(
            "u-sp-2",
            ClientHash("wrong-old", Convert.FromHexString(wrongOldSalt)), wrongOldSalt,
            ClientHash("NewSecret456!", Convert.FromHexString(newSalt)), newSalt));
        Assert.Equal("PASSWORD_MISMATCH", ex.Message);
        var reloaded = await stub.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-sp-2");
        Assert.Equal(0, reloaded!.TokenVersion);   // 验旧失败不改写（直种 0 + 失败 0）
    }

    [Fact]
    public async Task ChangePassword_CorrectOld_PasswordUpdated_TokenVersionIncremented()
    {
        var stub = CreateStub(out _);
        var storeSalt = NewSalt();
        // 直种组装格式 blob（不经 SetPasswordAsync——TokenVersion 保持 0；改密成功后 0→1 精确断言，与验旧失败用例同源）
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity
        {
            UId = "u-sp-3",
            Phone = "13800000103",
            AuthLevel = (int)AuthLevel.Phone,
            PasswordHash = AssembleProtectedClientHash(ClientHash("OldSecret123!", Convert.FromHexString(storeSalt)), storeSalt),
            TokenVersion = 0,
        });
        var write = CreateAuthAccountService(stub);

        var newSalt = NewSalt();
        await write.ChangePasswordAsync("u-sp-3",
            ClientHash("OldSecret123!", Convert.FromHexString(storeSalt)), storeSalt,
            ClientHash("NewSecret456!", Convert.FromHexString(newSalt)), newSalt);

        var reloaded = await stub.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-sp-3");
        Assert.Equal(1, reloaded!.TokenVersion);   // 直种 0 → 改密成功 TokenVersion++ = 1

        // 新密码可登录（SecurePassword 验证经 Unprotect 比对——新 clientHash 匹配）
        var provider = new PasswordAuthenticationProvider(stub, CreateProtector(), NullLogger<PasswordAuthenticationProvider>.Instance);
        var login = await provider.AuthenticateAsync(new ProviderAuthenticateContext(
            Identifier: "13800000103", ClientHash: ClientHash("NewSecret456!", Convert.FromHexString(newSalt)), Salt: newSalt));
        Assert.True(login.Success);
    }

    // ── 密码策略：历史防重用（⑥ 决策 5） ──────────────────────────────

    [Fact]
    public async Task SetPassword_HistoryReuseRejected()
    {
        var stub = CreateStub(out _);
        var account = new AuthAccountEntity { UId = "u-pol-1", Phone = "13800000601", AuthLevel = (int)AuthLevel.Phone };
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(account);
        var write = CreateAuthAccountService(stub);

        var salt1 = NewSalt();
        await write.SetPasswordAsync("u-pol-1", ClientHash("FirstPass1!", Convert.FromHexString(salt1)), salt1);
        var salt2 = NewSalt();
        await write.SetPasswordAsync("u-pol-1", ClientHash("SecondPass2!", Convert.FromHexString(salt2)), salt2);

        // 历史防重用（EnforcePolicy=true 默认；HistoryRetentionCount=3 默认）：复用第 1 代密码 → PASSWORD_REUSE_REJECTED
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => write.SetPasswordAsync(
            "u-pol-1", ClientHash("FirstPass1!", Convert.FromHexString(salt1)), salt1));
        Assert.Equal("PASSWORD_REUSE_REJECTED", ex.Message);
    }

    // ── 账号冻结（④ 决策 3/6/7） ──────────────────────────────────────

    [Fact]
    public async Task Freeze_BlocksLogin_ThenUnfreeze_Restores()
    {
        var stub = CreateStub(out _);
        var (_, storeSalt) = await SeedAccountWithPasswordAsync(stub, "u-frz-1", "Secret123!", "13800000701");
        var write = CreateAuthAccountService(stub);

        // 冻结（永久——无到期）
        await write.FreezeAsync("u-frz-1", null, "admin-op");
        var frozen = await stub.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-frz-1");
        Assert.True(frozen!.IsFrozen);

        // 冻结拦截新签发（Provider 认证路径——ACCOUNT_FROZEN 先于 hash 比对；用存储盐算 clientHash 保证语义真实）
        var provider = new PasswordAuthenticationProvider(stub, CreateProtector(), NullLogger<PasswordAuthenticationProvider>.Instance);
        var blocked = await provider.AuthenticateAsync(new ProviderAuthenticateContext(
            Identifier: "13800000701", ClientHash: ClientHash("Secret123!", Convert.FromHexString(storeSalt)), Salt: storeSalt));
        Assert.False(blocked.Success);
        Assert.Equal("ACCOUNT_FROZEN", blocked.FailReason);

        // 解冻 → 恢复（同盐验证——正密码可登录）
        await write.UnfreezeAsync("u-frz-1", "admin-op");
        var unfrozen = await stub.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-frz-1");
        Assert.False(unfrozen!.IsFrozen);
        var restored = await provider.AuthenticateAsync(new ProviderAuthenticateContext(
            Identifier: "13800000701", ClientHash: ClientHash("Secret123!", Convert.FromHexString(storeSalt)), Salt: storeSalt));
        Assert.True(restored.Success);
    }

    [Fact]
    public async Task Freeze_WithFreezeEnd_AutoExpires()
    {
        var stub = CreateStub(out _);
        var (_, storeSalt) = await SeedAccountWithPasswordAsync(stub, "u-frz-2", "Secret123!", "13800000702");
        var write = CreateAuthAccountService(stub);

        // 冻结 + 已过期到期时间 → IsFrozenEffective 立即 false（自动失效）
        // ⚠️ 用 -30h 而非 -1min：SQLite 内存库 DateTime 按本地时区存取（FreeSql SQLite 无时区列），
        //    UTC 值落库/回读会整体偏移 ±14h 内——-30h 余量保证任何时区下回读仍为过去（到期判定稳）。
        await write.FreezeAsync("u-frz-2", DateTime.UtcNow.AddHours(-30), "admin-op");
        var expired = await stub.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-frz-2");
        Assert.True(expired!.IsFrozen);            // 列仍 true
        Assert.False(expired.IsFrozenEffective);   // 但到期自动视为已解冻

        // 到期后放行（用存储盐——正密码可登录）
        var provider = new PasswordAuthenticationProvider(stub, CreateProtector(), NullLogger<PasswordAuthenticationProvider>.Instance);
        var ok = await provider.AuthenticateAsync(new ProviderAuthenticateContext(
            Identifier: "13800000702", ClientHash: ClientHash("Secret123!", Convert.FromHexString(storeSalt)), Salt: storeSalt));
        Assert.True(ok.Success);
    }

    // ── 频控：IRateLimitCheck 锁定（②③ 决策 2/3） ─────────────────────

    [Fact]
    public async Task PasswordLoginService_RateLimited_AfterThreshold()
    {
        var stub = CreateStub(out _);
        var options = AuthenticationTestHost.CreateOptions();
        options.LoginProtection.PasswordPerMinutePerSubject = 1;   // 阈值 1：首次失败后立即锁定
        var service = new PasswordLoginService(
            stub,
            [new PasswordAuthenticationProvider(stub, CreateProtector(), NullLogger<PasswordAuthenticationProvider>.Instance)],
            Options.Create(options),
            CreateRateLimit(),
            NullLogger<PasswordLoginService>.Instance);

        // 首次（错密码——Provider 失败，但 TryAcquire 成功消耗额度）
        var salt = NewSalt();
        var first = await service.LoginAsync("u-fl-1", ClientHash("wrong", Convert.FromHexString(salt)), salt);
        Assert.False(first.Success);

        // 第二次 → TryAcquire 失败 → PASSWORD_RATE_LIMITED（锁定=限流，ADR 决策 2/3）
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.LoginAsync(
            "u-fl-1", ClientHash("wrong", Convert.FromHexString(salt)), salt));
        Assert.Equal("PASSWORD_RATE_LIMITED", ex.Message);
    }

    // ── AuthLoginAttempt Password 频控分支（B.9/ADR15 COUNT 模式，审计面） ────

    [Fact]
    public async Task IsRateLimited_PasswordSubject_CountsInWindow()
    {
        var stub = CreateStub(out _);
        var options = AuthenticationTestHost.CreateOptions();
        options.LoginProtection.PasswordPerMinutePerSubject = 3;
        var service = new AuthLoginAttemptService(stub, Options.Create(options), NullLogger<AuthLoginAttemptService>.Instance);
        var ds = stub.Use<AuthLoginAttemptEntityDataService>();

        // 前 2 次不触发（< 阈值 3）
        for (var i = 0; i < 2; i++)
        {
            await ds.CreateAsync(new AuthLoginAttemptEntity
            {
                UserIdentity = "u-fl-1",
                AuthType = AuthTypes.Password,
                IsSuccess = false,
                AttemptTime = DateTime.UtcNow,
            });
        }
        Assert.False(await service.IsRateLimitedAsync("u-fl-1", AuthTypes.Password));

        // 第 3 次 ≥ 阈值 3 → 限流
        await ds.CreateAsync(new AuthLoginAttemptEntity
        {
            UserIdentity = "u-fl-1",
            AuthType = AuthTypes.Password,
            IsSuccess = false,
            AttemptTime = DateTime.UtcNow,
        });
        Assert.True(await service.IsRateLimitedAsync("u-fl-1", AuthTypes.Password));
    }

    [Fact]
    public async Task IsRateLimited_PasswordIp_SecondDimension()
    {
        // P0-NEW-2：双维度——单账号 60s 阈值内，同 IP 多账号 60min 命中 PasswordPerHourPerIp 也限流
        var stub = CreateStub(out _);
        var options = AuthenticationTestHost.CreateOptions();
        options.LoginProtection.PasswordPerMinutePerSubject = 100;   // 单账号维度放宽——隔离验证 IP 维度
        options.LoginProtection.PasswordPerHourPerIp = 2;
        var service = new AuthLoginAttemptService(stub, Options.Create(options), NullLogger<AuthLoginAttemptService>.Instance);
        var ds = stub.Use<AuthLoginAttemptEntityDataService>();

        // 同一 IP 1 条记录（模拟分布式爆破起始）——< 阈值 2 → 不限流
        await ds.CreateAsync(new AuthLoginAttemptEntity { UserIdentity = "u-a", IpAddress = "1.2.3.4", AuthType = AuthTypes.Password, IsSuccess = false, AttemptTime = DateTime.UtcNow });
        Assert.False(await service.IsRateLimitedAsync("u-c", "1.2.3.4", AuthTypes.Password));

        // 第 2 条同 IP → IP 维度 ≥ 2 → 限流（即使单账号维度未命中——u-c 自身无记录）
        await ds.CreateAsync(new AuthLoginAttemptEntity { UserIdentity = "u-b", IpAddress = "1.2.3.4", AuthType = AuthTypes.Password, IsSuccess = false, AttemptTime = DateTime.UtcNow });
        Assert.True(await service.IsRateLimitedAsync("u-c", "1.2.3.4", AuthTypes.Password));

        // IP 为 null → 退化单维度（不按 IP 限流——u-c 单账号 60s 内 0 条 → false）
        Assert.False(await service.IsRateLimitedAsync("u-c", null, AuthTypes.Password));
    }

    // ── 找回：SMS 统一完成（P1-NEW-1——clientHash 语义） ───────────────

    /// <summary>P1-NEW-1：SMS 找回统一完成——验证码校验通过 → 设新密码（SecurePassword clientHash）+ TokenVersion++。</summary>
    [Fact]
    public async Task CompleteResetSms_ValidCode_SetsNewPassword()
    {
        var stub = CreateStub(out _, register: (services, user) =>
        {
            services.AddSingleton<IAuthAccountQueryService>(new AuthAccountQueryService(user));
            services.AddSingleton<IAuthAccountService>(CreateAuthAccountService(user));
            services.AddSingleton<ISmsVerificationService>(new FakeSmsVerificationService(verifyResult: true));
        });
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity { UId = "u-sms-reset", Phone = "13800000501", AuthLevel = (int)AuthLevel.Phone });
        var service = new PasswordResetService(stub, stub.ServiceProvider!, CreateRateLimit(), NullLogger<PasswordResetService>.Instance);

        var newSalt = NewSalt();
        await service.CompleteResetSmsAsync("u-sms-reset", "13800000501", "123456",
            ClientHash("NewSecret456!", Convert.FromHexString(newSalt)), newSalt);

        var reloaded = await stub.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-sms-reset");
        Assert.False(string.IsNullOrEmpty(reloaded!.PasswordHash));   // protected blob 落库
        Assert.Equal(1, reloaded.TokenVersion);                       // TokenVersion++
    }

    /// <summary>P1-NEW-1：SMS 找回完成——验证码校验失败 → 抛 SMS_CODE_MISMATCH，不改写。</summary>
    [Fact]
    public async Task CompleteResetSms_InvalidCode_Throws_NoWrite()
    {
        var stub = CreateStub(out _, register: (services, user) =>
        {
            services.AddSingleton<IAuthAccountQueryService>(new AuthAccountQueryService(user));
            services.AddSingleton<IAuthAccountService>(CreateAuthAccountService(user));
            services.AddSingleton<ISmsVerificationService>(new FakeSmsVerificationService(verifyResult: false));
        });
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity { UId = "u-sms-reset2", Phone = "13800000502", AuthLevel = (int)AuthLevel.Phone });
        var service = new PasswordResetService(stub, stub.ServiceProvider!, CreateRateLimit(), NullLogger<PasswordResetService>.Instance);

        var newSalt = NewSalt();
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.CompleteResetSmsAsync(
            "u-sms-reset2", "13800000502", "000000",
            ClientHash("NewSecret456!", Convert.FromHexString(newSalt)), newSalt));
        Assert.Equal("SMS_CODE_MISMATCH", ex.Message);
        var reloaded = await stub.Use<AuthAccountEntityDataService>().GetByUIdAsync("u-sms-reset2");
        Assert.Null(reloaded!.PasswordHash);   // 未改写
        Assert.Equal(0, reloaded.TokenVersion);
    }

    /// <summary>P0-NEW-1 安全回归哨兵：重置码生成须加密安全路径（RandomNumberGenerator——弃 new Random 可预测种子）。</summary>
    [Fact]
    public void GenerateResetCode_IsCryptographicallyRandom()
    {
        var method = typeof(PasswordResetService).GetMethod("GenerateResetCode", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        Assert.NotNull(method);

        // 100 个 8 位码——全唯一 + 数字格式（熵面哨兵：弱 RNG 亦大概率唯一，但标识生成器运行；源码审计保证 GetInt32 路径）
        var codes = Enumerable.Range(0, 100).Select(_ => (string)method!.Invoke(null, null)!).ToList();
        Assert.All(codes, c => Assert.Matches(@"^\d{8}$", c));
        Assert.Equal(100, codes.Distinct().Count());
    }

    // ── Email 找回（B.10/B.11 可空依赖降级） ───────────────────────────

    [Fact]
    public async Task EmailReset_NoEmailSender_ReturnsNotConfigured()
    {
        var stub = CreateStub(out _);
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity { UId = "u-res-1", Phone = "13800000401", AuthLevel = (int)AuthLevel.Phone });
        var service = new PasswordResetService(stub, stub.ServiceProvider!, CreateRateLimit(), NullLogger<PasswordResetService>.Instance);

        var result = await service.InitiateEmailResetAsync("u-res-1");

        Assert.Equal("EMAIL_SENDER_NOT_CONFIGURED", result);
    }

    [Fact]
    public async Task EmailReset_NoEmailBound_ReturnsNotBound()
    {
        // 密码找回门面依赖 IAuthAccountQueryService（接口——经 stub 注册，C1 懒加载解析）
        var stub = CreateStub(out _, register: (services, user) =>
        {
            services.AddSingleton<TKWF.Ext.Emailing.IEmailSender>(new FakeEmailSender());
            services.AddSingleton<IAuthAccountQueryService>(new AuthAccountQueryService(user));
        });
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity { UId = "u-res-2", Phone = "13800000402", AuthLevel = (int)AuthLevel.Phone });
        // 无 UserProfile（未绑定邮箱）
        var service = new PasswordResetService(stub, stub.ServiceProvider!, CreateRateLimit(), NullLogger<PasswordResetService>.Instance);

        var result = await service.InitiateEmailResetAsync("u-res-2");

        Assert.Equal("EMAIL_NOT_BOUND", result);
    }

    // ── UserProfile 档案读写（T3） ────────────────────────────────────

    [Fact]
    public async Task UserProfile_CreateOrUpdate_UpsertByIdentity()
    {
        var stub = CreateStub(out _);
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(new AuthAccountEntity { UId = "u-up-1", Phone = "13800000301", AuthLevel = (int)AuthLevel.Phone });
        var ds = stub.Use<UserProfileEntityDataService>();

        // 首次创建
        var created = await ds.CreateOrUpdateAsync(new UserProfileEntity { UId = "u-up-1", Nickname = "初名" });
        Assert.Equal("初名", created.Nickname);

        // 二次 upsert（更新）
        await ds.CreateOrUpdateAsync(new UserProfileEntity { UId = "u-up-1", Nickname = "改名", Email = "a@b.c" });
        var reloaded = await ds.GetByUIdAsync("u-up-1");
        Assert.Equal("改名", reloaded!.Nickname);
        Assert.Equal("a@b.c", reloaded.Email);
    }

    // ── 测试桩 ───────────────────────────────────────────────────────

    private static StubDomainUser CreateStub(out IFreeSql fsql, Action<IServiceCollection, StubDomainUser>? register = null)
    {
        fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        return AuthenticationTestHost.CreateStub(fsql, register: register);
    }

    private sealed class FakeEmailSender : TKWF.Ext.Emailing.IEmailSender
    {
        public Task SendAsync(TKWF.Ext.Emailing.EmailMessage message, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;
    }

    /// <summary>Fake ISmsVerificationService（P1-NEW-1 测试桩——verify 结果可配置；SendCode 直成功）。</summary>
    private sealed class FakeSmsVerificationService : ISmsVerificationService
    {
        private readonly bool _verifyResult;
        public FakeSmsVerificationService(bool verifyResult) => _verifyResult = verifyResult;

        public Task SendCodeAsync(string phone, string scene, System.Threading.CancellationToken ct = default)
            => Task.CompletedTask;
        public Task<bool> VerifyCodeAsync(string phone, string code, string scene, System.Threading.CancellationToken ct = default)
            => Task.FromResult(_verifyResult);
    }
}
