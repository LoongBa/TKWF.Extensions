using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// T5（2026-10-09 三层边界）：外部 IdP 桥接消费门面 <see cref="ExternalIdpLoginService"/> 测试——
/// 桥接认证（<see cref="IExternalIdpAuthenticator"/> 契约）→ 映射（<see cref="ISsoChannelMapService"/>）→
/// 建号/复用（<see cref="IAuthAccountService"/>/<see cref="IAuthAccountQueryService"/>）→ 签 token1
/// （authType=<c>federated</c> + <c>channel_type</c>）。
/// <para>分层逻辑单测：StubDomainUser 直构门面（经基类 User 取上下文）+ Fake 依赖注册进桩 ServiceProvider
/// （<c>Use&lt;接口&gt;()</c> GetRequiredService 解析）；fail-hard 用例不注册 <see cref="IExternalIdpAuthenticator"/>
/// → <c>Use&lt;T&gt;()</c> 抛 <see cref="InvalidOperationException"/>（Federation 未装配守卫语义）。</para>
/// </summary>
public class ExternalIdpLoginServiceTests
{
    private const string ChannelType = "wechat_oauth";
    private const string ChannelId = "fake-mp";

    private static (ExternalIdpLoginService Service, FakeExternalIdpAuthenticator Auth, FakeSsoChannelMapService Map,
        FakeAuthAccountService AccountSvc, FakeAuthAccountQueryService AccountQuery, FakeExternalTokenService Token)
        CreateService(Action<IServiceCollection, StubDomainUser>? register = null)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var auth = new FakeExternalIdpAuthenticator();
        var map = new FakeSsoChannelMapService();
        var accountSvc = new FakeAuthAccountService();
        var accountQuery = new FakeAuthAccountQueryService();
        var token = new FakeExternalTokenService();

        var stub = AuthenticationTestHost.CreateStub(fsql, register: (services, user) =>
        {
            services.AddSingleton<IExternalIdpAuthenticator>(auth);
            services.AddSingleton<ISsoChannelMapService>(map);
            services.AddSingleton<IAuthAccountService>(accountSvc);
            services.AddSingleton<IAuthAccountQueryService>(accountQuery);
            services.AddSingleton<ITokenService>(token);
            register?.Invoke(services, user);
        });

        var service = new ExternalIdpLoginService(stub, NullLogger<ExternalIdpLoginService>.Instance);
        return (service, auth, map, accountSvc, accountQuery, token);
    }

    private static Dictionary<string, string?> Params()
        => new() { ["channel_id"] = ChannelId, ["code"] = "wx-code-1" };

    /// <summary>(a) 成功路径：Fake 桥接认证成功（openid123/AuthLevel=2）→ 无映射 → 建号 + LinkAsync → 回读 →
    /// 签发 token1（authType=federated + channel_type 透传）。</summary>
    [Fact]
    public async Task Login_Success_CreatesAccount_IssuesFederatedToken()
    {
        var (service, _, map, accountSvc, accountQuery, token) = CreateService();
        accountQuery.Account = new AuthAccountEntity
        {
            UId = "u-external-1",
            Phone = null,
            AuthLevel = (int)AuthLevel.Federated,
            IsEnabled = true,
            TokenVersion = 0
        };

        var result = await service.LoginAsync(ChannelType, Params());

        Assert.True(result.Success);
        Assert.Equal("u-external-1", result.UserId);
        Assert.NotNull(result.Token);
        // 桥接被调（channelType + 参数透传）
        // 建号 + LinkAsync（无映射）
        Assert.True(accountSvc.CreateCalled);
        Assert.Equal(1, map.LinkCalls);
        // token1 契约：authType=federated + channel_type=channelType
        Assert.Equal(AuthTypes.Federated, token.LastRequest!.AuthType);
        Assert.Equal(ChannelType, token.LastRequest.ChannelType);
    }

    /// <summary>(a-2) 桥接认证失败 → FailReason 透传（不建号不签发）。</summary>
    [Fact]
    public async Task Login_AuthFailure_ReturnsFailReason()
    {
        var (service, auth, _, accountSvc, _, token) = CreateService();
        auth.Result = new ExternalIdpAuthResult(false, null, "CHANNEL_NOT_FOUND", 0);

        var result = await service.LoginAsync(ChannelType, Params());

        Assert.False(result.Success);
        Assert.Equal("CHANNEL_NOT_FOUND", result.FailReason);
        Assert.False(accountSvc.CreateCalled);
        Assert.Null(token.LastRequest);
    }

    /// <summary>(a-3) 认证成功但外部用户标识缺失 → EXTERNAL_USER_ID_MISSING。</summary>
    [Fact]
    public async Task Login_AuthSuccessButNoExternalUserId_ReturnsMissing()
    {
        var (service, auth, _, accountSvc, _, _) = CreateService();
        auth.Result = new ExternalIdpAuthResult(true, null, null, 2);

        var result = await service.LoginAsync(ChannelType, Params());

        Assert.False(result.Success);
        Assert.Equal("EXTERNAL_USER_ID_MISSING", result.FailReason);
        Assert.False(accountSvc.CreateCalled);
    }

    /// <summary>(b) fail-hard：Federation 未装配（IExternalIdpAuthenticator 未注册）→ User.Use 抛守卫（InvalidOperationException）。
    /// 门面内不 catch 不降级——异常向表现层传播（端点映射 503 EXTERNAL_IDP_NOT_CONFIGURED）。</summary>
    [Fact]
    public async Task Login_FederationNotAssembled_ThrowsGuard()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        // ⚠️ 不注册 IExternalIdpAuthenticator——模拟 Federation 未装配（桥接契约无实现）
        var stub = AuthenticationTestHost.CreateStub(fsql, register: (services, _) =>
        {
            services.AddSingleton<ISsoChannelMapService>(new FakeSsoChannelMapService());
            services.AddSingleton<IAuthAccountService>(new FakeAuthAccountService());
            services.AddSingleton<IAuthAccountQueryService>(new FakeAuthAccountQueryService());
            services.AddSingleton<ITokenService>(new FakeExternalTokenService());
        });
        var service = new ExternalIdpLoginService(stub, NullLogger<ExternalIdpLoginService>.Instance);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.LoginAsync(ChannelType, Params()));
        Assert.Contains("IExternalIdpAuthenticator", ex.Message);   // GetRequiredService 缺实现守卫
    }

    /// <summary>(c-1) 已映射账号复用：映射命中 → 不建号不 Link → 直接回读签发。</summary>
    [Fact]
    public async Task Login_MappedAccount_Reuses_NoCreate()
    {
        var (service, _, map, accountSvc, accountQuery, token) = CreateService();
        map.Mapping = new SsoChannelMapDto("u-mapped-1", ChannelId, "openid123");
        accountQuery.Account = new AuthAccountEntity
        {
            UId = "u-mapped-1",
            AuthLevel = (int)AuthLevel.Federated,
            IsEnabled = true,
            TokenVersion = 0
        };

        var result = await service.LoginAsync(ChannelType, Params());

        Assert.True(result.Success);
        Assert.Equal("u-mapped-1", result.UserId);
        Assert.False(accountSvc.CreateCalled);   // 复用不建号
        Assert.Equal(0, map.LinkCalls);          // 复用不 Link
        Assert.Equal(AuthTypes.Federated, token.LastRequest!.AuthType);
        Assert.Equal(ChannelType, token.LastRequest.ChannelType);
    }

    /// <summary>(c-2) 无映射建号：LinkAsync 收建号 uid + channelId + externalUserId。</summary>
    [Fact]
    public async Task Login_NoMapping_CreatesAccount_AndLinks()
    {
        var (service, _, map, accountSvc, accountQuery, _) = CreateService();
        accountQuery.Account = new AuthAccountEntity
        {
            UId = "u-created-1",
            AuthLevel = (int)AuthLevel.Federated,
            IsEnabled = true,
            TokenVersion = 0
        };

        var result = await service.LoginAsync(ChannelType, Params());

        Assert.True(result.Success);
        Assert.True(accountSvc.CreateCalled);
        Assert.Equal(1, map.LinkCalls);
        Assert.Equal(accountSvc.CreatedUid, map.LastLinkedUid);      // 建号 uid 进 Link
        Assert.Equal(ChannelId, map.LastLinkedChannelId);
        Assert.Equal("openid123", map.LastLinkedExternalUserId);
    }

    /// <summary>(d) 参数透传：parameters 原样进桥接（不裁剪/不重写——平台协议字段归平台库通道解析）。</summary>
    [Fact]
    public async Task Login_ParametersPassThroughToBridge()
    {
        var (service, auth, _, accountSvc, accountQuery, _) = CreateService();
        accountQuery.Account = new AuthAccountEntity { UId = "u-p-1", AuthLevel = 2, IsEnabled = true };

        var parameters = new Dictionary<string, string?>
        {
            ["channel_id"] = ChannelId,
            ["code"] = "wx-code-9",
            ["state"] = "csrf-token-abc",
            ["scope"] = "snsapi_userinfo",
        };
        await service.LoginAsync("wechat_oauth", parameters);

        Assert.Equal("wechat_oauth", auth.LastChannelType);
        Assert.NotNull(auth.LastParameters);
        Assert.Equal(parameters.Count, auth.LastParameters!.Count);
        foreach (var (key, value) in parameters)
            Assert.True(auth.LastParameters.TryGetValue(key, out var got) && got == value,
                $"参数 {key} 未原样透传（期望 {value}，实际 {got}）");
    }

    /// <summary>(e) 缺 channel_id → EXTERNAL_CHANNEL_ID_REQUIRED（映射键必需，fail-closed）。</summary>
    [Fact]
    public async Task Login_MissingChannelId_ReturnsRequired()
    {
        var (service, _, _, accountSvc, _, _) = CreateService();

        var result = await service.LoginAsync(ChannelType, new Dictionary<string, string?> { ["code"] = "wx-code-2" });

        Assert.False(result.Success);
        Assert.Equal("EXTERNAL_CHANNEL_ID_REQUIRED", result.FailReason);
        Assert.False(accountSvc.CreateCalled);
    }

    // ── 测试桩（Fake 门面——仅测试项目，不注册进生产） ──

    /// <summary>Fake 桥接（IExternalIdpAuthenticator）——Invoked/LastChannelType/LastParameters 捕获 + 可配置 Result。</summary>
    internal sealed class FakeExternalIdpAuthenticator : IExternalIdpAuthenticator
    {
        public bool Invoked { get; private set; }
        public string? LastChannelType { get; private set; }
        public IReadOnlyDictionary<string, string?>? LastParameters { get; private set; }

        public ExternalIdpAuthResult Result { get; set; } = new(true, "openid123", null, 2);

        public Task<ExternalIdpAuthResult> AuthenticateAsync(string channelType, IReadOnlyDictionary<string, string?> parameters, CancellationToken ct = default)
        {
            Invoked = true;
            LastChannelType = channelType;
            LastParameters = parameters;
            return Task.FromResult(Result);
        }
    }

    /// <summary>Fake SSO 渠道映射——可配置 Mapping（命中复用）/ Link 捕获。</summary>
    internal sealed class FakeSsoChannelMapService : ISsoChannelMapService
    {
        public SsoChannelMapDto? Mapping { get; set; }
        public int LinkCalls { get; private set; }
        public string? LastLinkedUid { get; private set; }
        public string? LastLinkedChannelId { get; private set; }
        public string? LastLinkedExternalUserId { get; private set; }

        public Task<SsoChannelMapDto?> GetByChannelAsync(string channelId, string externalUserId, CancellationToken ct = default)
            => Task.FromResult(Mapping);

        public Task<SsoChannelMapDto> LinkAsync(string uid, string channelId, string externalUserId, CancellationToken ct = default)
        {
            LinkCalls++;
            LastLinkedUid = uid;
            LastLinkedChannelId = channelId;
            LastLinkedExternalUserId = externalUserId;
            Mapping = new SsoChannelMapDto(uid, channelId, externalUserId);
            return Task.FromResult(Mapping);
        }
    }

    /// <summary>Fake 账号写门面——CreateAsync 捕获（建号断言）。</summary>
    internal sealed class FakeAuthAccountService : IAuthAccountService
    {
        public bool CreateCalled { get; private set; }
        public string? CreatedUid { get; private set; }

        public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default) => Task.FromResult<AuthAccountEntity?>(null);
        public Task CreateAsync(AuthAccountEntity account, CancellationToken ct = default)
        {
            CreateCalled = true;
            CreatedUid = account.UId;
            return Task.CompletedTask;
        }
        public Task UpdateAsync(AuthAccountEntity account, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> IncrementTokenVersionAsync(string uid, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<int> SetPasswordAsync(string uid, string newClientHash, string salt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task ChangePasswordAsync(string uid, string oldClientHash, string oldSalt, string newClientHash, string newSalt, CancellationToken ct = default) => throw new NotSupportedException();
        public Task FreezeAsync(string uid, DateTime? freezeEnd, string operatorName, CancellationToken ct = default) => throw new NotSupportedException();
        public Task UnfreezeAsync(string uid, string operatorName, CancellationToken ct = default) => throw new NotSupportedException();
    }

    /// <summary>Fake 账号查询门面——GetByUIdAsync 返回可配置 Account（回读断言）。</summary>
    internal sealed class FakeAuthAccountQueryService : IAuthAccountQueryService
    {
        public AuthAccountEntity? Account { get; set; }

        public Task<AuthAccountEntity?> GetByUIdAsync(string uid, CancellationToken ct = default) => Task.FromResult(Account);
        public Task<AuthAccountEntity?> GetByPhoneAsync(string phone, CancellationToken ct = default) => Task.FromResult<AuthAccountEntity?>(null);
        public Task<UserProfileEntity?> GetProfileByUIdAsync(string uid, CancellationToken ct = default) => Task.FromResult<UserProfileEntity?>(null);
        public Task<bool> IsWechatBoundAsync(string uid, CancellationToken ct = default) => Task.FromResult(false);
    }

    /// <summary>Fake 令牌服务——IssueTokenAsync 捕获请求（authType/channel_type 断言）。</summary>
    internal sealed class FakeExternalTokenService : ITokenService
    {
        public TokenIssueRequest? LastRequest { get; private set; }

        public Task<TokenIssueResult> IssueTokenAsync(TokenIssueRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            return Task.FromResult(new TokenIssueResult("at-ext", "rt-ext", 7200));
        }
        public Task<TokenValidationResult> ValidateTokenAsync(string accessToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task<TokenRefreshResult> RefreshTokenAsync(string refreshToken, CancellationToken ct = default) => throw new NotSupportedException();
        public Task RevokeTokenAsync(string jti, string reason, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
