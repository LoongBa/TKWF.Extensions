using System;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Utility.Caching;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>D8：OAuthTicket——TTL 5min / 单次消费 / PKCE 校验矩阵 / 白名单 / 跨应用拒绝。
/// <para>V4.10.53（领域自治根治后重写）：门面继承 DomainServiceBase——StubDomainUser 直构（经基类 User 取上下文），
/// DataService/ITokenService 经 User.Use&lt;T&gt;() 懒加载（接口 GetRequiredService——CreateStub 注册 TokenService 实例；
/// 具体类 NoAop 直建）。业务断言语义不变。</para></summary>
public class OAuthTicketServiceTests
{
    private const string RedirectUri = "https://app.example.com/callback";

    private static (OAuthTicketService Service, OAuthTicketEntityDataService Ds) CreateService(AuthCenterOptions options)
    {
        var (service, stub) = CreateServiceWithUser(options, new StubDomainUser());
        return (service, stub.Use<OAuthTicketEntityDataService>());
    }

    /// <summary>已认证用户桩变体（BindTicketAsync 已认证帧测试）——返回 stub 供 CAS 并发第二服务共享。</summary>
    private static (OAuthTicketService Service, StubDomainUser Stub) CreateServiceWithUser(AuthCenterOptions options, StubDomainUser user)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql, user, (services, u) =>
        {
            var tokenService = new TokenService(
                u, new DevKeyCache<TokenService.RsaKeySet>(), // E4 密钥管理抽象（V0.7.0）：dev 分支密钥缓存
                Options.Create(options),
                new MemoryCache(new MemoryCacheOptions()), NullLogger<TokenService>.Instance);
            services.AddSingleton<ITokenService>(tokenService);   // OAuthTicketService.Use<ITokenService>() 接口懒加载源
            tokenService.EnsureKeysLoaded();

            // V0.8.0：应用授权写入门面（ExchangeAsync 成功落登录授权——生产路径经 User.Use<IAuthGrantCommandService>()）
            services.AddSingleton<IAuthGrantCommandService>(new AuthGrantCommandService(u));
            services.AddSingleton<IAuthGrantQueryService>(new AuthGrantQueryService(u));
        });
        var service = new OAuthTicketService(stub, Options.Create(options), NullLogger<OAuthTicketService>.Instance);
        return (service, stub);
    }

    /// <summary>文件模式 SQLite 已认证桩（CAS 并发双绑——两独立连接真实并发；:memory: 单连接池会串行化掩盖竞态）。</summary>
    private static (OAuthTicketService Service, StubDomainUser Stub) CreateFileServiceWithUser(string dbPath, StubDomainUser user)
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        AuthenticationTestHost.SyncSchema(fsql);
        var stub = AuthenticationTestHost.CreateStub(fsql, user); // Bind 路径不消费 ITokenService/grant 门面——免注册
        var service = new OAuthTicketService(stub, Options.Create(AuthenticationTestHost.CreateOptions()), NullLogger<OAuthTicketService>.Instance);
        return (service, stub);
    }

    [Fact]
    public async Task Issue_Ticket_WithPkce_Exchange_Succeeds()
    {
        var (service, _) = CreateService(AuthenticationTestHost.CreateOptions());
        var codeVerifier = "abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz"; // 57 位

        // 签发即绑定（V0.8.0 B5——IssueAsync UserId 非空落库，不再手动回填）
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri, State: "s1", CodeVerifier: codeVerifier, UserId: "u-123"));

        // PKCE 正确换取
        var result = await service.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, codeVerifier, "app-1", "s1"));
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);

        // 单次消费——二次换取拒绝
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, codeVerifier, "app-1", "s1")));
        Assert.Equal(OAuthTicketErrorCodes.TicketConsumed, ex.Message);
    }

    [Fact]
    public async Task Exchange_PkceMatrix_HashNonNull_VerifierNull_Rejected()
    {
        var (service, _) = CreateService(AuthenticationTestHost.CreateOptions());
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri, CodeVerifier: "abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz", UserId: "u-123"));

        // hash 非 null + verifier null → 拒绝（防无 verifier 请求绕过——C4）
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, null, "app-1", null)));
        Assert.Equal(OAuthTicketErrorCodes.TicketStateMismatch, ex.Message);
    }

    [Fact]
    public async Task Exchange_WrongAppId_Rejected()
    {
        var (service, _) = CreateService(AuthenticationTestHost.CreateOptions());
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri, UserId: "u-123"));

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, null, "app-2", null)));
        Assert.Equal(OAuthTicketErrorCodes.TicketStateMismatch, ex.Message);
    }

    [Fact]
    public async Task Issue_RedirectUriNotInWhitelist_Rejected()
    {
        var (service, _) = CreateService(AuthenticationTestHost.CreateOptions());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.IssueAsync(new OAuthTicketIssueRequest("app-1", "https://evil.example.com/callback")));
    }

    [Fact]
    public async Task Exchange_PkceMatrix_ServerTrust_NullNull_Allowed()
    {
        // C4 第一行：hash null（服务端/trust 签发）+ verifier null（换取）→ 放行（信任服务端路径——依赖 AppId 白名单 + 内网信任）
        var (service, _) = CreateService(AuthenticationTestHost.CreateOptions());
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri, UserId: "u-123"));

        var result = await service.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, null, "app-1", null));
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
    }

    [Fact]
    public async Task Exchange_PkceMatrix_VerifierMismatch_Rejected()
    {
        // C4 第三行：hash 非 null + verifier 非 null 但 SHA256 不匹配 → 拒绝
        var (service, _) = CreateService(AuthenticationTestHost.CreateOptions());
        var codeVerifier = "abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz";
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri, CodeVerifier: codeVerifier, UserId: "u-123"));

        var wrongVerifier = codeVerifier[..^1] + "A"; // 末位改 A（仍在 unreserved 集合）
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, wrongVerifier, "app-1", null)));
        Assert.Equal(OAuthTicketErrorCodes.TicketStateMismatch, ex.Message);
    }

    [Fact]
    public async Task Exchange_ExpiredTicket_Rejected()
    {
        var (service, ds) = CreateService(AuthenticationTestHost.CreateOptions());
        // 手动落过期票据
        var expired = new OAuthTicketEntity
        {
            Ticket = TokenService.Base64UrlEncode(new byte[32]),
            TicketType = OAuthTicketTypes.Login,
            AppId = "app-1",
            RedirectUri = RedirectUri,
            UserId = "u-123",
            ExpiresAt = DateTime.UtcNow.AddDays(-1), // AddDays(-1) 容错 SQLite DateTime 本地化 +8h 存取
            CreateTime = DateTime.UtcNow.AddMinutes(-6)
        };
        await ds.CreateAsync(expired);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.ExchangeAsync(new OAuthTicketExchangeRequest(expired.Ticket, null, "app-1", null)));
        Assert.Equal(OAuthTicketErrorCodes.TicketExpired, ex.Message);
    }

    // ═══════════════════ V0.8.0 B5 票据绑定（BindTicketAsync 全矩阵——Oracle P0-1 已认证帧 / P1-2 CAS） ═══════════════════

    [Fact]
    public async Task Bind_Ticket_AfterIssue_BindsAuthenticatedUser()
    {
        // 主路径：authorize 签发（未绑）→ 登录 → 绑定（已认证帧，userId 取 User.UserId）→ 换取成功
        var (service, _) = CreateServiceWithUser(AuthenticationTestHost.CreateOptions(), new AuthenticatedStubUser("u-123"));
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri));

        await service.BindTicketAsync(ticket);

        // 绑定后换取成功（不再 TICKET_NOT_BOUND——B5 闭环：第三方票据回调流 authorize→登录→绑定→exchange）
        var result = await service.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, null, "app-1", null));
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
    }

    [Fact]
    public async Task Bind_NoAuthenticatedFrame_Rejected()
    {
        // 无认证帧（匿名 stub——UserId null）→ 拒绝（抛守卫 InvalidOperationException——防票据劫持绑任意 userId）
        var (service, _) = CreateService(AuthenticationTestHost.CreateOptions());
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri));

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.BindTicketAsync(ticket));
    }

    [Fact]
    public async Task Bind_Consumed_Rejected()
    {
        // 已认证帧（否则在认证守卫处即拒绝——无法到达消费判定）
        var (service, stub) = CreateServiceWithUser(AuthenticationTestHost.CreateOptions(), new AuthenticatedStubUser("u-123"));
        var ds = stub.Use<OAuthTicketEntityDataService>();
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri));
        var entity = await ds.GetByTicketAsync(ticket);
        await ds.MarkConsumedAsync(entity!.Id, DateTime.UtcNow);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.BindTicketAsync(ticket));
        Assert.Equal(OAuthTicketErrorCodes.TicketConsumed, ex.Message);
    }

    [Fact]
    public async Task Bind_Expired_Rejected()
    {
        // 已认证帧（否则在认证守卫处即拒绝——无法到达过期判定）
        var (service, stub) = CreateServiceWithUser(AuthenticationTestHost.CreateOptions(), new AuthenticatedStubUser("u-123"));
        var ds = stub.Use<OAuthTicketEntityDataService>();
        var expired = new OAuthTicketEntity
        {
            Ticket = TokenService.Base64UrlEncode(new byte[32]),
            TicketType = OAuthTicketTypes.Login,
            AppId = "app-1",
            RedirectUri = RedirectUri,
            ExpiresAt = DateTime.UtcNow.AddDays(-1),
            CreateTime = DateTime.UtcNow.AddMinutes(-6)
        };
        await ds.CreateAsync(expired);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.BindTicketAsync(expired.Ticket));
        Assert.Equal(OAuthTicketErrorCodes.TicketExpired, ex.Message);
    }

    [Fact]
    public async Task Bind_AlreadyBound_Rejected()
    {
        // 签发即绑（IssueAsync UserId）后再补绑 → CAS（WHERE Id AND UserId IS NULL）败者 → TICKET_ALREADY_BOUND
        var (service, _) = CreateServiceWithUser(AuthenticationTestHost.CreateOptions(), new AuthenticatedStubUser("u-123"));
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri, UserId: "u-123"));

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.BindTicketAsync(ticket));
        Assert.Equal(OAuthTicketErrorCodes.TicketAlreadyBound, ex.Message);
    }

    [Fact]
    public async Task Bind_Concurrent_CasOnlyOneSucceeds()
    {
        // Oracle P1-2 CAS：两独立连接并发双绑同一票据——只 1 成功，败者 TICKET_ALREADY_BOUND
        // （防票据劫持：攻击者认证帧绑任意 userId——CAS 条件更新保证先到者赢）
        var dbPath = Path.Combine(Path.GetTempPath(), "tkwf-auth-ticket-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var (svc1, _) = CreateFileServiceWithUser(dbPath, new AuthenticatedStubUser("u-1"));
            var (svc2, _) = CreateFileServiceWithUser(dbPath, new AuthenticatedStubUser("u-2"));
            var ticket = await svc1.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri)); // 未绑定签发

            var results = await Task.WhenAll(
                CaptureBind(svc1.BindTicketAsync(ticket)),
                CaptureBind(svc2.BindTicketAsync(ticket)));

            Assert.Equal(1, results.Count(r => r is null));                              // 恰 1 成功
            Assert.Equal(1, results.Count(r => r == OAuthTicketErrorCodes.TicketAlreadyBound)); // 恰 1 CAS 败者
        }
        finally
        {
            if (File.Exists(dbPath))
            {
                try { File.Delete(dbPath); }
                catch (IOException) { /* 连接未完全释放时忽略，系统临时目录回收 */ }
            }
        }
    }

    /// <summary>Bind 捕获辅助——成功 null / 失败错误码（AuthenticationException.Message）。</summary>
    private static async Task<string?> CaptureBind(Task bindTask)
    {
        try
        {
            await bindTask;
            return null;
        }
        catch (AuthenticationException ex)
        {
            return ex.Message;
        }
    }
}
