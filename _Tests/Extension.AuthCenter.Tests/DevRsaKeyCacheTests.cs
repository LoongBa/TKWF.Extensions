using System;
using System.Collections.Generic;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Utility.Caching;
using Xunit;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// E4 密钥管理抽象（V0.7.0）——开发模式临时 RSA 密钥缓存（<see cref="DevKeyCache{TKey}"/> DI 单例）回归测试。
/// 继 V0.5.3 DevRsaKeyCache（进程内静态缓存，已删）——现由主框架 <see cref="DevKeyCache{TKey}"/> 泛型实现替代
/// （TokenService ctor 注入，LoadKeysCore dev 分支经 <c>_devKeys.GetOrCreate</c> 共享）。
/// <para>T1（核心）：两个独立 TokenService 实例共享同一 DevKeyCache → 签发实例 A / 验签实例 B 成功
/// （跨实例密钥一致性——缺陷消除唯一证明）；T2：独立缓存实例互不共享密钥集 → 新实例拒旧 token
/// （隔离有效——原 ResetForTests 隔离语义等价验证；DevKeyCache.ResetForTests 为 TKWF.Utility internal 钩子，
/// 未对扩展测试 IVT，按"禁反射"约束改用独立缓存实例验证）；T3：DevKeyCache 工厂仅首次执行
/// （GetOrCreate 两次返回同一实例）。</para>
/// </summary>
public class DevRsaKeyCacheTests
{
    // 每用例新实例——DevKeyCache 为实例态（非静态），无跨测试类/跨用例泄漏，无需 ctor Reset
    private readonly DevKeyCache<TokenService.RsaKeySet> _devKeys = new();

    [Fact]
    public async Task DevMode_TwoInstances_IssueThenVerify_Succeeds()
    {
        // 缺陷复现场景：两个独立 scoped 实例（各持 Lazy<RsaKeySet>）——共享同一 DevKeyCache（DI 单例）→
        // 签发/验签密钥一致性（修前 A 签发、B 验签必 INVALID_SIGNATURE）
        var options = new AuthCenterOptions { IsProduction = false, Issuer = "auth-test" };
        var (issuer, issuerStub) = CreateService(options, _devKeys);
        var (verifier, verifierStub) = CreateService(options, _devKeys);

        var account = CreateAccount();
        await issuerStub.Use<AuthAccountEntityDataService>().CreateAsync(account, default);

        var issued = await issuer.IssueTokenAsync(
            new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false), default);

        // 核心断言：跨实例验签成功（同一 DevKeyCache 密钥集——缺陷消除的唯一证明）
        var validation = await verifier.ValidateTokenAsync(issued.AccessToken, default);
        Assert.Equal(account.UId, validation.UserId);
        Assert.Equal(AuthTypes.Sms, validation.AuthType);
    }

    [Fact]
    public async Task SeparateCache_NewInstanceRejectsOldToken()
    {
        // 先经实例 A（缓存 I）填充密钥集 + 签发 token
        var options = new AuthCenterOptions { IsProduction = false, Issuer = "auth-test" };
        var (issuer, issuerStub) = CreateService(options, _devKeys);
        var account = CreateAccount();
        await issuerStub.Use<AuthAccountEntityDataService>().CreateAsync(account, default);
        var issued = await issuer.IssueTokenAsync(
            new TokenIssueRequest(account.UId, AuthTypes.Sms, (int)AuthLevel.Phone, false), default);

        // 隔离：新实例使用独立 DevKeyCache（另一密钥集——工厂独立生成）——验签旧 token 必须失败
        //（密钥集不共享 → 隔离有效；等价于原 ResetForTests 清场后重建密钥集的隔离语义）
        var freshCache = new DevKeyCache<TokenService.RsaKeySet>();
        var (freshVerifier, _) = CreateService(options, freshCache);
        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => freshVerifier.ValidateTokenAsync(issued.AccessToken, default));
        Assert.Contains("INVALID_SIGNATURE", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetOrCreate_RunsFactoryOnce_ReturnsSameInstance()
    {
        var cache = new DevKeyCache<TokenService.RsaKeySet>();
        var factoryCalls = 0;

        TokenService.RsaKeySet Factory()
        {
            factoryCalls++;
            return new TokenService.RsaKeySet("rsa-key-1", RSA.Create(2048), new Dictionary<string, RSA>());
        }

        // 双重校验锁语义：工厂仅首次执行，后续 GetOrCreate 直接返回已缓存实例（同引用）
        var first = cache.GetOrCreate(Factory);
        var second = cache.GetOrCreate(Factory);
        Assert.Same(first, second);
        Assert.Equal(1, factoryCalls);
    }

    // ── helpers（对齐 TokenServiceTests.CreateService 形态——独立实例直构 + 显式传入 DevKeyCache）──

    private static (TokenService Service, StubDomainUser Stub) CreateService(AuthCenterOptions options, DevKeyCache<TokenService.RsaKeySet> devKeys)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var service = new TokenService(
            stub, devKeys,
            Options.Create(options),
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
