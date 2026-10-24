using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Exceptions;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// V0.10.0（P2-2 共享编排 + P1-1 fallback）+ B3b：联邦登录共享编排 <see cref="FederatedLoginOrchestrator"/> 测试——
/// identity → 账号回读（UId 直读优先）/ 映射复用 / 建号 fallback → 签 token1（authType=federated + channel_type）。
/// <para>B3b：成功返回 <see cref="TokenResponse"/>（断言 AccessToken + FakeTokenService.LastRequest 契约）；
/// 失败抛 <see cref="DomainException"/>（400，ErrorCode = ACCOUNT_FROZEN/ACCOUNT_DISABLED/ACCOUNT_NOT_FOUND/ACCOUNT_NOT_PROVISIONED）。</para>
/// <para>分层逻辑单测：StubDomainUser 直构门面（经基类 User 取上下文）+ Fake 依赖注册进桩 ServiceProvider
/// （Use&lt;接口&gt;() GetRequiredService 解析——IExternalIdentityService/IAuthAccountService/IAuthAccountQueryService/
/// ITokenService 全 Fake）。</para>
/// </summary>
public class FederatedLoginOrchestratorTests
{
    private const string ChannelId = "wechat-event-1";
    private const string ExternalUserId = "openid-event-1";

    private static (FederatedLoginOrchestrator Service, FakeExternalIdentityService Map,
        FakeAuthAccountService AccountSvc, FakeAuthAccountQueryService AccountQuery, FakeExternalTokenService Token)
        CreateService(Action<IServiceCollection, StubDomainUser>? register = null, AuthCenterOptions? options = null)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var map = new FakeExternalIdentityService();
        var accountSvc = new FakeAuthAccountService();
        var accountQuery = new FakeAuthAccountQueryService(accountSvc.CreatedAccounts);
        var token = new FakeExternalTokenService();

        var stub = AuthenticationTestHost.CreateStub(fsql, register: (services, user) =>
        {
            services.AddSingleton<IExternalIdentityService>(map);
            services.AddSingleton<IAuthAccountService>(accountSvc);
            services.AddSingleton<IAuthAccountQueryService>(accountQuery);
            services.AddSingleton<ITokenService>(token);
            AuthenticationTestHost.AddAuthRelationSupport(services, user);
            register?.Invoke(services, user);
        });

        var service = new FederatedLoginOrchestrator(stub, NullLogger<FederatedLoginOrchestrator>.Instance,
            Options.Create(options ?? new AuthCenterOptions()));
        return (service, map, accountSvc, accountQuery, token);
    }

    private static FederatedIdentity Identity(string? uid = null, string? channelType = "wechat_event")
        => new(uid ?? string.Empty, ChannelId, ExternalUserId, channelType, 2);

    private static AuthAccountEntity Account(string uid, bool enabled = true)
        => new()
        {
            UId = uid,
            IsEnabled = enabled,
            TokenVersion = 0
        };

    /// <summary>(a) UId 直读命中（P1-1 优先）：identity.UId 非空 → GetByUIdAsync 直读复用——不建号、不查映射、不 Link。</summary>
    [Fact]
    public async Task Login_UidDirectHit_Reuses_NoCreate_NoMapLookup()
    {
        var (service, map, accountSvc, accountQuery, token) = CreateService();
        accountQuery.Account = Account("u-direct-1");

        var result = await service.LoginAsync(Identity(uid: "u-direct-1"));

        Assert.NotNull(result.AccessToken);
        Assert.False(accountSvc.CreateCalled);   // 直读命中不建号
        Assert.Equal(0, map.GetCalls);           // 直读命中不查映射
        Assert.Equal(0, map.LinkCalls);          // 直读命中不 Link
        Assert.Equal(1, accountQuery.GetByUidCalls);   // P2-4 审核收敛：仅一次直读——免二次回读（直读优化核心）
        Assert.Equal(AuthTypes.Federated, token.LastRequest!.AuthType);
        Assert.Equal("wechat_event", token.LastRequest.ChannelType);
        Assert.Equal("u-direct-1", token.LastRequest.UserId);   // 签发 UserId 与回读实体 UId 一致（P2-1 收敛）
    }

    /// <summary>(b) UId 直读未命中 → fallthrough 到映射命中（P1-1 fallback）：复用既有映射，不建号。</summary>
    [Fact]
    public async Task Login_UidDirectMiss_FallsBackToMapHit_Reuses()
    {
        var (service, map, accountSvc, accountQuery, token) = CreateService();
        accountQuery.Account = null;                       // UId 直读未命中
        map.Mapping = new ExternalIdentityDto("u-mapped-1", ChannelId, ExternalUserId, DateTime.UtcNow);
        accountQuery.MappedAccount = Account("u-mapped-1");

        // ⚠️ Fake 语义：GetByUIdAsync 返回 Account；映射命中后 orchestrator 再 GetByUIdAsync(mapped.UId)——
        //   Fake 需区分"首次直读（null）"与"映射后回读（MappedAccount）"——见 FakeAuthAccountQueryService 实现
        var result = await service.LoginAsync(Identity(uid: "u-nonexist"));

        Assert.NotNull(result.AccessToken);
        Assert.False(accountSvc.CreateCalled);
        Assert.Equal(0, map.LinkCalls);
        Assert.Equal(1, map.GetCalls);
        Assert.Equal(ChannelId, map.LastGetChannelId);
        Assert.Equal(ExternalUserId, map.LastGetExternalUserId);
    }

    /// <summary>(c) UId 空：跳过直读 → 映射命中 → 复用（事件链签发 UId 空先例——SsoSubAppBridge 签 string.Empty）。</summary>
    [Fact]
    public async Task Login_EmptyUid_MapHit_Reuses()
    {
        var (service, map, accountSvc, accountQuery, token) = CreateService();
        map.Mapping = new ExternalIdentityDto("u-mapped-2", ChannelId, ExternalUserId, DateTime.UtcNow);
        accountQuery.MappedAccount = Account("u-mapped-2");

        var result = await service.LoginAsync(Identity(uid: null));

        Assert.NotNull(result.AccessToken);
        Assert.False(accountSvc.CreateCalled);
        Assert.Equal(0, map.LinkCalls);
        Assert.Equal(1, accountQuery.GetByUidCalls);            // 仅映射命中后回读一次
        Assert.Equal("u-mapped-2", accountQuery.LastGetByUidArg);  // 回读的是映射 uid——未发起空 UId 直读
    }

    /// <summary>(d) UId 空 + 无映射 → 建号 + LinkAsync → 回读 → 签发（完整建号 fallback 路径）。</summary>
    [Fact]
    public async Task Login_NoMapping_CreatesAccount_AndLinks()
    {
        var (service, map, accountSvc, accountQuery, token) = CreateService();
        accountQuery.MappedAccount = Account("u-created-ticket-1");

        var result = await service.LoginAsync(Identity(uid: null));

        Assert.NotNull(result.AccessToken);
        Assert.True(accountSvc.CreateCalled);
        Assert.Equal(1, map.LinkCalls);
        Assert.Equal(accountSvc.CreatedUid, map.LastLinkedUid);
        Assert.Equal(ChannelId, map.LastLinkedChannelId);
        Assert.Equal(ExternalUserId, map.LastLinkedExternalUserId);
        Assert.Equal(AuthTypes.Federated, token.LastRequest!.AuthType);
        Assert.Equal("wechat_event", token.LastRequest.ChannelType);
    }

    /// <summary>(e) 冻结拒（UId 直读命中但 IsFrozen）→ ACCOUNT_FROZEN（DomainException 400，不签发）。</summary>
    [Fact]
    public async Task Login_UidDirectHit_Frozen_ReturnsFrozen()
    {
        var freeze = new FakeFreezeStateProvider("u-frozen-1");
        var (service, _, accountSvc, accountQuery, token) = CreateService(
            (services, _) => services.AddSingleton<IFreezeStateProvider>(freeze));
        accountQuery.Account = Account("u-frozen-1");

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.LoginAsync(Identity(uid: "u-frozen-1")));

        Assert.Equal("ACCOUNT_FROZEN", ex.ErrorCode);
        Assert.False(accountSvc.CreateCalled);
        Assert.Null(token.LastRequest);
    }

    /// <summary>(f) 禁用拒 → ACCOUNT_DISABLED（DomainException 400，不签发）。</summary>
    [Fact]
    public async Task Login_UidDirectHit_Disabled_ReturnsDisabled()
    {
        var (service, _, accountSvc, accountQuery, token) = CreateService();
        accountQuery.Account = Account("u-disabled-1", enabled: false);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.LoginAsync(Identity(uid: "u-disabled-1")));

        Assert.Equal("ACCOUNT_DISABLED", ex.ErrorCode);
        Assert.False(accountSvc.CreateCalled);
        Assert.Null(token.LastRequest);
    }

    /// <summary>(g) channel_type null（P2-3）：identity.ChannelType null → IssueTokenAsync ChannelType null（不写 claim）。</summary>
    [Fact]
    public async Task Login_NullChannelType_IssueWithoutChannelTypeClaim()
    {
        var (service, _, _, accountQuery, token) = CreateService();
        accountQuery.Account = Account("u-nullct-1");
        accountQuery.MappedAccount = Account("u-nullct-1");

        var result = await service.LoginAsync(Identity(uid: "u-nullct-1", channelType: null));

        Assert.NotNull(result.AccessToken);
        Assert.Null(token.LastRequest!.ChannelType);   // null 不写 channel_type claim
    }

    /// <summary>(h) 映射命中但回读 null（数据不一致）→ ACCOUNT_NOT_FOUND（DomainException 400，fail-closed）。</summary>
    [Fact]
    public async Task Login_MappedButAccountMissing_ReturnsNotFound()
    {
        var (service, map, _, _, token) = CreateService();
        map.Mapping = new ExternalIdentityDto("u-mapped-3", ChannelId, ExternalUserId, DateTime.UtcNow);
        // MappedAccount 未设置 → 回读 null

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.LoginAsync(Identity(uid: null)));

        Assert.Equal("ACCOUNT_NOT_FOUND", ex.ErrorCode);
        Assert.Null(token.LastRequest);
    }

    /// <summary>(i) 无映射 + AutoProvision=false → ACCOUNT_NOT_PROVISIONED（DomainException 400，不建号/不绑定/不签发）。</summary>
    [Fact]
    public async Task Login_NoMapping_AutoProvisionDisabled_ReturnsNotProvisioned()
    {
        var options = new AuthCenterOptions();
        options.FederatedProvisioning.AutoProvision = false;
        var (service, map, accountSvc, _, token) = CreateService(options: options);

        var ex = await Assert.ThrowsAsync<DomainException>(() => service.LoginAsync(Identity(uid: null)));

        Assert.Equal("ACCOUNT_NOT_PROVISIONED", ex.ErrorCode);
        Assert.False(accountSvc.CreateCalled);
        Assert.Equal(0, map.LinkCalls);
        Assert.Null(token.LastRequest);
    }

    // ── 测试桩（Fake 门面——仅测试项目，不注册进生产） ──

    internal sealed class FakeExternalIdentityService : IExternalIdentityService
    {
        public ExternalIdentityDto? Mapping { get; set; }
        public int LinkCalls { get; private set; }
        public int GetCalls { get; private set; }
        public string? LastGetChannelId { get; private set; }
        public string? LastGetExternalUserId { get; private set; }
        public string? LastLinkedUid { get; private set; }
        public string? LastLinkedChannelId { get; private set; }
        public string? LastLinkedExternalUserId { get; private set; }

        public Task<ExternalIdentityDto?> GetByExternalIdAsync(string channelId, string externalUserId, CancellationToken ct = default)
        {
            GetCalls++;
            LastGetChannelId = channelId;
            LastGetExternalUserId = externalUserId;
            return Task.FromResult(Mapping);
        }

        public Task<ExternalIdentityDto> LinkAsync(string uid, string channelId, string externalUserId,
            string? operatorName = null, CancellationToken ct = default)
        {
            LinkCalls++;
            LastLinkedUid = uid;
            LastLinkedChannelId = channelId;
            LastLinkedExternalUserId = externalUserId;
            Mapping = new ExternalIdentityDto(uid, channelId, externalUserId, DateTime.UtcNow);
            return Task.FromResult(Mapping);
        }

        public Task UnlinkAsync(string uid, string channelId, string externalUserId,
            string? operatorName = null, CancellationToken ct = default)
            => Task.CompletedTask;

        public Task<IReadOnlyList<ExternalIdentityDto>> GetByAccountAsync(string uid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<ExternalIdentityDto>>(Array.Empty<ExternalIdentityDto>());
    }

    internal sealed class FakeFreezeStateProvider(params string[] frozenUIds) : IFreezeStateProvider
    {
        private readonly HashSet<string> _frozen = new(frozenUIds, StringComparer.Ordinal);

        public Task<bool> IsFrozenAsync(string uid, int? scope = null, CancellationToken ct = default)
            => Task.FromResult(_frozen.Contains(uid));

        public Task<IReadOnlyList<AuthAccountFreezeEntity>> GetActiveAsync(string uid, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<AuthAccountFreezeEntity>>(Array.Empty<AuthAccountFreezeEntity>());

        public void Invalidate(string uid) => _frozen.Remove(uid);
    }

    internal sealed class FakeAuthAccountService : IAuthAccountService
    {
        public bool CreateCalled { get; private set; }
        public string? CreatedUid { get; private set; }

        /// <summary>建号台账（CreateAsync 落库联动——FakeAuthAccountQueryService 回读共享字典）。</summary>
        public Dictionary<string, AuthAccountEntity> CreatedAccounts { get; } = new();

        public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
            => Task.FromResult(CreatedAccounts.TryGetValue(uid, out var entity) ? entity : null);
        public Task CreateAsync(AuthAccountEntity account, CancellationToken ct = default)
        {
            CreateCalled = true;
            CreatedUid = account.UId;
            CreatedAccounts[account.UId] = account;
            return Task.CompletedTask;
        }
        public Task UpdateAsync(AuthAccountEntity account, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> IncrementTokenVersionAsync(string uid, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SetPasswordAsync(string uid, string newClientHash, string salt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ChangePasswordAsync(string uid, string oldClientHash, string oldSalt, string newClientHash, string newSalt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task FreezeAsync(string uid, DateTime? freezeEnd, string operatorName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnfreezeAsync(string uid, string operatorName, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>
    /// Fake 账号查询——三路径语义：
    /// <list type="bullet">
    /// <item><see cref="Account"/> 非空 → 恒返回该账号（UId 直读命中断言 a/e/f/g）；</item>
    /// <item><see cref="MappedAccount"/> → 仅当查询 uid 与其 UId 匹配时返回（映射命中后回读断言 b/c/h）；</item>
    /// <item><see cref="FakeAuthAccountService.CreatedAccounts"/> → 建号后回读断言 d（CreateAsync 落库联动）。</item>
    /// </list>
    /// GetByUidCalls 计数（UId 空不触发直读断言 c）。
    /// </summary>
    internal sealed class FakeAuthAccountQueryService : IAuthAccountQueryService
    {
        private readonly Dictionary<string, AuthAccountEntity> _createdAccounts;

        public FakeAuthAccountQueryService(Dictionary<string, AuthAccountEntity> createdAccounts)
        {
            _createdAccounts = createdAccounts;
        }

        public AuthAccountEntity? Account { get; set; }
        public AuthAccountEntity? MappedAccount { get; set; }
        public int GetByUidCalls { get; private set; }
        public string? LastGetByUidArg { get; private set; }

        public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default)
        {
            GetByUidCalls++;
            LastGetByUidArg = uid;
            if (Account is not null) return Task.FromResult(Account);                                  // 直读命中（恒配置场景）
            if (_createdAccounts.TryGetValue(uid, out var created)) return Task.FromResult<AuthAccountEntity?>(created);  // 建号回读
            if (MappedAccount is not null && uid == MappedAccount.UId) return Task.FromResult(MappedAccount);  // 映射回读
            return Task.FromResult<AuthAccountEntity?>(null);                                           // 直读未命中
        }
        public Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default) => Task.FromResult<AuthAccountEntity?>(null);
        public Task<UserProfileEntity?> GetProfileByUIdAsync(string uid, CancellationToken ct = default) => Task.FromResult<UserProfileEntity?>(null);
        public Task<bool> IsWechatBoundAsync(string uid, CancellationToken ct = default) => Task.FromResult(false);
    }

    internal sealed class FakeExternalTokenService : ITokenService
    {
        public TokenIssueRequest? LastRequest { get; private set; }

        public Task<TokenIssueResult> IssueTokenAsync(TokenIssueRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new TokenIssueResult("at-vec", "rt-vec", 7200));
        }
        public Task<TokenValidationResult> ValidateTokenAsync(string accessToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TokenResponse> RefreshTokenAsync(RefreshRequest request, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeTokenAsync(string jti, string reason, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<VerifyResponse> GetCurrentTokenInfoAsync(CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeCurrentSessionAsync(CancellationToken ct = default) => throw new NotSupportedException();
    }
}