using System;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>
/// V0.5.3（转达-2026-10-05 修复）——开发模式临时 RSA 密钥跨实例一致性回归测试。
/// <para>缺陷：开发模式（SigningKeyPath 未配置 &amp;&amp; !IsProduction）下每个 TokenService scoped 实例
/// 各自 <c>Lazy&lt;RsaKeySet&gt;</c> → 签发实例与验签实例密钥集不同 → INVALID_SIGNATURE。</para>
/// <para>T1（核心）：两个独立实例 A 签发 / B 验签 → 必须成功（DevRsaKeyCache 进程内共享密钥集）；
/// T2：ResetForTests() 清场后，新实例无法验证旧实例签发的 token（密钥集已变化——隔离有效）。
/// setup 调 Reset 防跨测试类/跨用例泄漏（DevRsaKeyCache 静态缓存）。</para>
/// </summary>
public class DevRsaKeyCacheTests
{
    public DevRsaKeyCacheTests()
    {
        DevRsaKeyCache.ResetForTests();
    }

    [Fact]
    public async Task DevMode_TwoInstances_IssueThenVerify_Succeeds()
    {
        // 缺陷复现场景：两个独立 scoped 实例（各持 Lazy<RsaKeySet>）——修前 A 签发、B 验签必 INVALID_SIGNATURE
        var options = new AuthCenterOptions { IsProduction = false, Issuer = "auth-test" };
        var (issuer, issuerStub) = CreateService(options);
        var (verifier, verifierStub) = CreateService(options);

        var account = CreateAccount();
        await issuerStub.Use<AuthAccountEntityDataService>().CreateAsync(account, default);

        var issued = await issuer.IssueTokenAsync(
            new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false), default);

        // 核心断言：跨实例验签成功（同一 DevRsaKeyCache 密钥集——缺陷消除的唯一证明）
        var validation = await verifier.ValidateTokenAsync(issued.AccessToken, default);
        Assert.Equal(account.UId, validation.UserId);
        Assert.Equal(AuthTypes.Sms, validation.AuthType);
    }

    [Fact]
    public async Task ResetForTests_ClearsCache_NewInstanceRejectsOldToken()
    {
        // 先经实例 A 填充缓存 + 签发 token
        var options = new AuthCenterOptions { IsProduction = false, Issuer = "auth-test" };
        var (issuer, issuerStub) = CreateService(options);
        var account = CreateAccount();
        await issuerStub.Use<AuthAccountEntityDataService>().CreateAsync(account, default);
        var issued = await issuer.IssueTokenAsync(
            new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false), default);

        // 清场：Reset 后缓存已空（旧密钥集已 Dispose）
        DevRsaKeyCache.ResetForTests();

        // 新实例 B 触发重新生成——验签旧实例签发的 token 必须失败（密钥集已变化 → 隔离有效）
        var (freshVerifier, _) = CreateService(options);
        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => freshVerifier.ValidateTokenAsync(issued.AccessToken, default));
        Assert.Contains("INVALID_SIGNATURE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    // ── helpers（对齐 TokenServiceTests.CreateService 形态——独立实例直构）──

    private static (TokenService Service, StubDomainUser Stub) CreateService(AuthCenterOptions options)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var service = new TokenService(
            stub, Options.Create(options),
            new MemoryCache(new MemoryCacheOptions()), NullLogger<TokenService>.Instance);
        return (service, stub);
    }

    private static AuthAccountEntity CreateAccount(string? phone = "13800138000", int authLevel = 1)
        => new()
        {
            UId = AccountIdGenerator.NewUId(),
            Phone = phone,
            AuthLevel = authLevel,
            TokenVersion = 0
        };
}
