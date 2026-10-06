using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Enumerations;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>D9：身份适配层——工厂契约（CreateUserInfoFromAccount/FromToken 填充 Roles）+ ITokenVerifier 验签链 + IAuthorizationMapper 本地角色映射。
/// <para>登录全链路（UserHelper 钩子 → LoginAsUserAsync → 已认证 DomainUser）走消费方 Host（见使用指南 §二 第 3 步）——
/// 对齐 IdentityV03Tests 先例（工厂方法契约验证 + 注释明示全链路归消费方 Host）。</para>
/// <para>V4.10.53（领域自治根治后重写）：LocalJwtTokenVerifier 继承 DomainServiceBase——StubDomainUser 直构，
/// ITokenService 经 User.Use&lt;接口&gt;() 懒加载（CreateStub 注册 TokenService 实例）；TokenService 亦继承基类（User 首参）。</para></summary>
public class IdentityAdapterTests
{
    // ── 工厂契约 ─────────────────────────────────────────────────────

    [Fact]
    public void UserHelperBase_CreateUserInfoFromAccount_FillsRoles()
    {
        var account = new AuthAccountEntity { UId = "u-100", Phone = "13800138000", AuthLevel = (int)AuthLevel.Phone };
        var helper = new TestAuthUserHelper();
        var userInfo = helper.CreateUserInfoFromAccountForTest(account, ["Member"]);

        Assert.Equal("u-100", userInfo.UserIdString);
        Assert.Equal("13800138000", userInfo.UserName);
        Assert.Contains("Member", userInfo.Roles ?? new List<string>());
    }

    [Fact]
    public void UserHelperBase_CreateUserInfoFromToken_FillsRoles()
    {
        var token = new TokenValidationResult(
            "u-200", AuthTypes.Wechat, (int)AuthLevel.Wechat, false,
            "jti-1", DateTime.UtcNow.AddHours(2),
            new Dictionary<string, string> { ["iss"] = "auth-test" });
        var helper = new TestAuthUserHelper();
        var userInfo = helper.CreateUserInfoFromTokenForTest(token, ["Teacher"]);

        Assert.Equal("u-200", userInfo.UserIdString);
        Assert.Contains("Teacher", userInfo.Roles ?? new List<string>());
    }

    // ── IAuthorizationMapper 本地角色映射契约（消费方实现侧验证） ────

    [Fact]
    public async Task AuthorizationMapper_MapRoles_FromTokenClaims()
    {
        var mapper = new TestAuthorizationMapper();
        var token = new TokenValidationResult(
            "u-300", AuthTypes.Sms, (int)AuthLevel.Phone, true,
            "jti-2", DateTime.UtcNow.AddHours(2),
            new Dictionary<string, string> { ["teacher_verified"] = "true" });

        var roles = await mapper.MapRolesAsync(token.UserId, token);

        // teacher_verified 身份声明 → 本地教师角色映射（Oracle I4 语义）
        Assert.Contains("Teacher", roles);
    }

    // ── ITokenVerifier 验签链（本地公钥——TokenService 全链已测，此处验证契约接线） ──

    [Fact]
    public async Task LocalJwtTokenVerifier_DelegatesToTokenService()
    {
        // LocalJwtTokenVerifier 构造注入 ITokenService——契约接线（验签细节 TokenServiceTests 覆盖）
        var options = AuthenticationTestHost.CreateOptions();
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql, register: (services, user) =>
        {
            // LocalJwtTokenVerifier.Use<ITokenService>() 接口懒加载源（CreateStub 注册单例）
            var tokenService = new TokenService(
                user,
                new TKW.Framework.Utility.Caching.DevKeyCache<TokenService.RsaKeySet>(), // E4 密钥管理抽象（V0.7.0）：dev 分支密钥缓存
                Microsoft.Extensions.Options.Options.Create(options),
                new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
                Microsoft.Extensions.Logging.Abstractions.NullLogger<TokenService>.Instance);
            services.AddSingleton<ITokenService>(tokenService);
        });
        var verifier = new LocalJwtTokenVerifier(stub);
        var account = new AuthAccountEntity { UId = "u-400", Phone = "13900139000", AuthLevel = (int)AuthLevel.Phone, TokenVersion = 0 };
        await stub.Use<AuthAccountEntityDataService>().CreateAsync(account);

        var issued = await stub.GetService<ITokenService>().IssueTokenAsync(new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false));
        var result = await verifier.VerifyAsync(issued.AccessToken);

        Assert.Equal(account.UId, result.UserId);
    }

    // ── 测试桩 ───────────────────────────────────────────────────────

    private sealed class TestAuthUserHelper : AuthenticationUserHelperBase<TestUserInfo>
    {
        protected override TestUserInfo CreateUserInfoFromAccount(AuthAccountEntity account, IReadOnlyList<string> roles)
            => new(account.UId, account.Phone ?? account.UId) { Roles = roles.ToList() };

        protected override TestUserInfo CreateUserInfoFromToken(TokenValidationResult token, IReadOnlyList<string> roles)
            => new(token.UserId, token.UserId) { Roles = roles.ToList() };

        public TestUserInfo CreateUserInfoFromAccountForTest(AuthAccountEntity account, IReadOnlyList<string> roles)
            => CreateUserInfoFromAccount(account, roles);

        public TestUserInfo CreateUserInfoFromTokenForTest(TokenValidationResult token, IReadOnlyList<string> roles)
            => CreateUserInfoFromToken(token, roles);
    }

    private sealed class TestAuthorizationMapper : IAuthorizationMapper<TestUserInfo>
    {
        public Task<IReadOnlyList<string>> MapRolesAsync(string sub, TokenValidationResult token, System.Threading.CancellationToken ct = default)
        {
            // 教师核实身份声明 → 本地教师角色（Oracle I4——业务角色不进令牌，本地映射）
            var roles = token.TeacherVerified ? new List<string> { "Teacher" } : new List<string> { "Member" };
            return Task.FromResult<IReadOnlyList<string>>(roles);
        }
    }
}
