using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Threading.Tasks;
using TKW.Framework.Core.AuthController;
using TKW.Framework.Domain.AuthController;
using TKW.Framework.Enumerations;
using TKWF.Ext.SecurityLog.Tests;

namespace TKWF.Ext.SecurityLog.Tests;

/// <summary>
/// SecurityLogFilterAttribute 测试——经真实 StaticDomainInterceptor AOP 管线（采集路径实测）。
/// <para>覆盖：登录成功/失败（脱敏 Detail + 尝试用户名）、锁定判定（C4）、登出/改密/注册/重置事件类型、
/// 白名单判定（C1）、非认证异常不记录、Options 零开销、事件类型开关、Store 异常静默、真实 SQLite 落库。</para>
/// </summary>
public class SecurityLogFilterTests
{
    // ── 登录成功 → Login/Success（User/IP/UA/CorrelationId 断言，D2）──

    [Fact]
    public async Task LoginByPassword_Success_RecordsLoginSuccessWithClientInfo()
    {
        var entries = await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Target = new FakeAuthController();
            ctx.MethodName = "LoginByPasswordAsync";
            ctx.Arguments = new object[] { "alice", "super-secret-password" };
            ctx.Ambient = new SecurityLogFilterPipeline.FakeAmbientContext()
                .With(SecurityLogFilterAttribute<TestUserInfo>.ClientIpAmbientKey, "203.0.113.7")
                .With(SecurityLogFilterAttribute<TestUserInfo>.UserAgentAmbientKey, "Mozilla/5.0 (test)");
            ctx.CorrelationIdProvider = new SecurityLogFilterPipeline.FakeCorrelationIdProvider { CurrentId = "corr-abc-123" };
            ctx.Proceed = (user, invocation) =>
            {
                user.UserInfo = new TestUserInfo("100", "alice");
                invocation.ReturnValue = new LoginPayload(true, "alice", "Alice", "sess-1");
                return Task.CompletedTask;
            };
        });

        var entry = Assert.Single(entries);
        Assert.Equal("Login", entry.EventType);
        Assert.Equal("Authentication", entry.EventCategory);
        Assert.Equal("Success", entry.Result);
        Assert.Equal("alice", entry.UserName);
        Assert.Equal(100, entry.UserId);
        Assert.Equal("203.0.113.7", entry.IpAddress);
        Assert.Equal("Mozilla/5.0 (test)", entry.UserAgent);
        Assert.Equal("corr-abc-123", entry.CorrelationId);
    }

    // ── 登录失败（错误密码）→ Login/Failed + 脱敏 Detail + 尝试用户名（D3）──

    [Fact]
    public async Task LoginByPassword_WrongPassword_RecordsFailedWithSanitizedDetail()
    {
        var store = new SecurityLogFilterPipeline.CapturingSecurityLogStore();

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            SecurityLogFilterPipeline.RunAsync(ctx =>
            {
                ctx.Store = store;
                ctx.Target = new FakeAuthController();
                ctx.MethodName = "LoginByPasswordAsync";
                ctx.Arguments = new object[] { "alice", "my-secret-password-123" };
                ctx.Ambient = new SecurityLogFilterPipeline.FakeAmbientContext()
                    .With(SecurityLogFilterAttribute<TestUserInfo>.ClientIpAmbientKey, "198.51.100.9");
                ctx.Proceed = (_, _) => throw new AuthenticationException("密码错误");
            }));

        Assert.Equal("密码错误", ex.Message);
        var entry = Assert.Single(store.Entries);
        Assert.Equal("Login", entry.EventType);
        Assert.Equal("Failed", entry.Result);
        // 尝试用户名 = 请求输入（防枚举语义保留审计来源）
        Assert.Equal("alice", entry.UserName);
        // 脱敏：Detail 只含异常消息，绝不含密码明文
        Assert.Equal("密码错误", entry.Detail);
        Assert.DoesNotContain("my-secret-password-123", entry.Detail);
        Assert.Equal("198.51.100.9", entry.IpAddress);
        Assert.Null(entry.UserId);
    }

    // ── 锁定 → Lockout/Failed（C4：AuthenticationException 消息含"锁定"关键字）──

    [Fact]
    public async Task LoginByContext_LockedAccount_RecordsLockoutEvent()
    {
        var store = new SecurityLogFilterPipeline.CapturingSecurityLogStore();

        await Assert.ThrowsAsync<AuthenticationException>(() =>
            SecurityLogFilterPipeline.RunAsync(ctx =>
            {
                ctx.Store = store;
                ctx.Target = new FakeAuthController();
                ctx.MethodName = "LoginByContextAsync";
                ctx.Arguments = new object[] { new LoginContextInput("bob", "cred", EnumLoginFrom.PcWeb, EnumLoginAuthType.Password) };
                ctx.Proceed = (_, _) => throw new AuthenticationException("账户已锁定，请联系管理员解锁");
            }));

        var entry = Assert.Single(store.Entries);
        Assert.Equal("Lockout", entry.EventType);   // C4 关键字判定 → 记 Lockout 而非 Login
        Assert.Equal("Failed", entry.Result);
        Assert.Equal("bob", entry.UserName);
        Assert.Equal("账户已锁定，请联系管理员解锁", entry.Detail);
    }

    // ── 非白名单方法 → 不记录（C1 白名单判定）──

    [Fact]
    public async Task NonSecurityMethod_NotIntercepted_NoRecord()
    {
        var entries = await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Target = new NonSecurityService();
            ctx.MethodName = "DoBusiness";
            ctx.Arguments = Array.Empty<object>();
            ctx.Proceed = (_, invocation) =>
            {
                invocation.ReturnValue = "ok";
                return Task.CompletedTask;
            };
        });

        Assert.Empty(entries);
    }

    // ── 非认证异常 → 不记录（保持原异常语义，不吞不遮蔽）──

    [Fact]
    public async Task Login_NonAuthenticationException_NotRecorded()
    {
        var store = new SecurityLogFilterPipeline.CapturingSecurityLogStore();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SecurityLogFilterPipeline.RunAsync(ctx =>
            {
                ctx.Store = store;
                ctx.Target = new FakeAuthController();
                ctx.MethodName = "LoginByPasswordAsync";
                ctx.Arguments = new object[] { "alice", "pwd" };
                ctx.Proceed = (_, _) => throw new InvalidOperationException("数据库不可用");
            }));

        Assert.Empty(store.Entries);
    }

    // ── 登出 → Logout/Success（EventType 正确，D4）──

    [Fact]
    public async Task Logout_ReturnsNormally_RecordsLogoutSuccess()
    {
        var entries = await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Target = new FakeAuthController();
            ctx.MethodName = "LogoutAsync";
            ctx.Arguments = new object[] { false };
            ctx.Proceed = (user, invocation) =>
            {
                user.UserInfo = new TestUserInfo("100", "alice");
                invocation.ReturnValue = new LoginPayload(false, null, null, null); // 登出后 Success=false 但调用本身成功
                return Task.CompletedTask;
            };
        });

        var entry = Assert.Single(entries);
        Assert.Equal("Logout", entry.EventType);
        Assert.Equal("Success", entry.Result);
        Assert.Equal("alice", entry.UserName);   // 登出无 args 用户名 → 回退当前认证用户
    }

    // ── 注册业务失败（RegisterResult(false)）→ Register/Failed + Detail（D4）──

    [Fact]
    public async Task Register_ReturnsFailedResult_RecordsRegisterFailedWithDetail()
    {
        var entries = await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Target = new FakeAuthController();
            ctx.MethodName = "RegisterSecureAsync";
            ctx.Arguments = new object[] { new RegisterSecureInput("carol", "clienthash", "salt") };
            ctx.Proceed = (_, invocation) =>
            {
                invocation.ReturnValue = new RegisterResult(false, "注册失败，请稍后重试");
                return Task.CompletedTask;
            };
        });

        var entry = Assert.Single(entries);
        Assert.Equal("Register", entry.EventType);
        Assert.Equal("Failed", entry.Result);
        Assert.Equal("注册失败，请稍后重试", entry.Detail);
        Assert.Equal("carol", entry.UserName);   // 尝试用户名 = RegisterSecureInput.UserName
    }

    // ── 改密成功 → PasswordChange/Success（D4）──

    [Fact]
    public async Task ChangePassword_Success_RecordsPasswordChange()
    {
        var entries = await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Target = new FakeAuthController();
            ctx.MethodName = "ChangePasswordSecureAsync";
            ctx.Arguments = new object[] { new ChangePasswordSecureInput("oldResp", "challengeToken", "newHash", "newSalt") };
            ctx.Proceed = (user, invocation) =>
            {
                user.UserInfo = new TestUserInfo("100", "alice");
                invocation.ReturnValue = new RegisterResult(true);
                return Task.CompletedTask;
            };
        });

        var entry = Assert.Single(entries);
        Assert.Equal("PasswordChange", entry.EventType);
        Assert.Equal("Success", entry.Result);
        // ChangePasswordSecureInput 无 UserName → 回退当前认证用户（不污染 record ToString）
        Assert.Equal("alice", entry.UserName);
    }

    // ── 密码重置（IPasswordResetFlow 目标，C3）→ PasswordReset ──

    [Fact]
    public async Task PasswordReset_InitiateSuccess_RecordsPasswordReset()
    {
        var entries = await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Target = new FakePasswordResetFlow();
            ctx.MethodName = "InitiateResetAsync";
            ctx.Arguments = new object[] { "dave" };
            ctx.Proceed = (_, invocation) =>
            {
                invocation.ReturnValue = true;
                return Task.CompletedTask;
            };
        });

        var entry = Assert.Single(entries);
        Assert.Equal("PasswordReset", entry.EventType);
        Assert.Equal("Success", entry.Result);
        Assert.Equal("dave", entry.UserName);
    }

    [Fact]
    public async Task PasswordReset_CompleteFailed_RecordsPasswordResetFailedWithDetail()
    {
        var entries = await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Target = new FakePasswordResetFlow();
            ctx.MethodName = "CompleteResetAsync";
            ctx.Arguments = new object[] { "dave", "123456", "newHash", "salt" };
            ctx.Proceed = (_, invocation) =>
            {
                invocation.ReturnValue = new ResetResult(false, "无效或已过期的重置码");
                return Task.CompletedTask;
            };
        });

        var entry = Assert.Single(entries);
        Assert.Equal("PasswordReset", entry.EventType);
        Assert.Equal("Failed", entry.Result);
        Assert.Equal("无效或已过期的重置码", entry.Detail);
        Assert.Equal("dave", entry.UserName);
    }

    // ── Options.Enabled=false → 零开销（D6）──

    [Fact]
    public async Task Options_Disabled_ZeroOverhead_NoRecord()
    {
        var entries = await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Options = new SecurityLoggingOptions { Enabled = false };
            ctx.Target = new FakeAuthController();
            ctx.MethodName = "LoginByPasswordAsync";
            ctx.Arguments = new object[] { "alice", "pwd" };
            ctx.Proceed = (_, invocation) =>
            {
                invocation.ReturnValue = new LoginPayload(true, "alice", "Alice", "s");
                return Task.CompletedTask;
            };
        });

        Assert.Empty(entries);
    }

    // ── Options.EventTypes 事件类型开关 ──

    [Fact]
    public async Task Options_EventTypesFilter_OnlyConfiguredTypesRecorded()
    {
        var options = new SecurityLoggingOptions
        {
            EventTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Logout" },
        };

        // 登录（未配置）→ 不记录
        var loginEntries = await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Options = options;
            ctx.Target = new FakeAuthController();
            ctx.MethodName = "LoginByPasswordAsync";
            ctx.Arguments = new object[] { "alice", "pwd" };
            ctx.Proceed = (_, invocation) =>
            {
                invocation.ReturnValue = new LoginPayload(true, "alice", "Alice", "s");
                return Task.CompletedTask;
            };
        });
        Assert.Empty(loginEntries);

        // 登出（已配置）→ 记录
        var logoutEntries = await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Options = options;
            ctx.Target = new FakeAuthController();
            ctx.MethodName = "LogoutAsync";
            ctx.Arguments = new object[] { false };
            ctx.Proceed = (_, invocation) =>
            {
                invocation.ReturnValue = new LoginPayload(false, null, null, null);
                return Task.CompletedTask;
            };
        });
        var entry = Assert.Single(logoutEntries);
        Assert.Equal("Logout", entry.EventType);
    }

    // ── Store 异常静默：落库失败不阻断登录流程（D6）──

    [Fact]
    public async Task Store_Throws_SilentlySwallowed_LoginNotBlocked()
    {
        var store = new SecurityLogFilterPipeline.CapturingSecurityLogStore { ThrowOnSave = new InvalidOperationException("db down") };

        // 不抛异常（SaveAsync 异常被 Store 静默，且过滤器兜底）——登录流程正常返回
        var entries = await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Store = store;
            ctx.Target = new FakeAuthController();
            ctx.MethodName = "LoginByPasswordAsync";
            ctx.Arguments = new object[] { "alice", "pwd" };
            ctx.Proceed = (_, invocation) =>
            {
                invocation.ReturnValue = new LoginPayload(true, "alice", "Alice", "s");
                return Task.CompletedTask;
            };
        });

        Assert.Empty(entries);   // 落库失败 → 无条目（异常被静默吞掉）
    }

    // ── 端到端：过滤器 → 真实 SecurityLogStore → DataService → SQLite 落库 ──

    [Fact]
    public async Task Filter_RealStore_Sqlite_EndToEndPersists()
    {
        using var fsql = SecurityLogTestHost.CreateInMemoryFreeSql();
        var store = SecurityLogTestHost.CreateStore(fsql);

        await SecurityLogFilterPipeline.RunAsync(ctx =>
        {
            ctx.Store = store;
            ctx.Target = new FakeAuthController();
            ctx.MethodName = "LoginByPasswordAsync";
            ctx.Arguments = new object[] { "alice", "pwd" };
            ctx.Ambient = new SecurityLogFilterPipeline.FakeAmbientContext()
                .With(SecurityLogFilterAttribute<TestUserInfo>.ClientIpAmbientKey, "10.0.0.1");
            ctx.Proceed = (user, invocation) =>
            {
                user.UserInfo = new TestUserInfo("100", "alice");
                invocation.ReturnValue = new LoginPayload(true, "alice", "Alice", "s");
                return Task.CompletedTask;
            };
        });

        // 经 DataService 回查（红线：断言不经裸 fsql.Select）
        var queryService = SecurityLogTestHost.CreateQueryService(fsql);
        var result = await queryService.GetListAsync(new SecurityLogQueryInput());
        Assert.Equal(1, result.Total);
        var item = Assert.Single(result.Items);
        Assert.Equal("Login", item.EventType);
        Assert.Equal("Success", item.Result);
        Assert.Equal("alice", item.UserName);
        Assert.Equal("10.0.0.1", item.IpAddress);
    }

    /// <summary>非安全方法服务桩（白名单外目标）。</summary>
    private sealed class NonSecurityService
    {
        public string DoBusiness() => "ok";
    }
}
