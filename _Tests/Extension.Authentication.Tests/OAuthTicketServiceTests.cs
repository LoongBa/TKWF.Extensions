using System;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>D8：OAuthTicket——TTL 5min / 单次消费 / PKCE 校验矩阵 / 白名单 / 跨应用拒绝。</summary>
public class OAuthTicketServiceTests
{
    private const string RedirectUri = "https://app.example.com/callback";

    private static (OAuthTicketService Service, OAuthTicketEntityDataService Ds, TokenService TokenService) CreateService(AuthCenterOptions options)
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = new StubDomainUser();
        var ticketDs = new OAuthTicketEntityDataService(
            stub, new FreeSqlEntityDAC<OAuthTicketEntity>(new UnitOfWorkManager(fsql)));
        var accountDs = new AuthAccountEntityDataService(
            stub, new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)));
        var refreshDs = new AuthRefreshTokenEntityDataService(
            stub, new FreeSqlEntityDAC<AuthRefreshTokenEntity>(new UnitOfWorkManager(fsql)));
        var blacklistDs = new AuthTokenBlacklistEntityDataService(
            stub, new FreeSqlEntityDAC<AuthTokenBlacklistEntity>(new UnitOfWorkManager(fsql)));
        stub.Register(ticketDs);
        stub.Register(accountDs);
        stub.Register(refreshDs);
        stub.Register(blacklistDs);
        var tokenService = new TokenService(
            Options.Create(options), stub,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            NullLogger<TokenService>.Instance);
        stub.Register<ITokenService>(tokenService);
        tokenService.EnsureKeysLoaded();
        return (new OAuthTicketService(Options.Create(options), stub, NullLogger<OAuthTicketService>.Instance), ticketDs, tokenService);
    }

    [Fact]
    public async Task Issue_Ticket_WithPkce_Exchange_Succeeds()
    {
        var (service, ds, tokenService) = CreateService(AuthenticationTestHost.CreateOptions());
        var codeVerifier = "abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz"; // 57 位

        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri, State: "s1", CodeVerifier: codeVerifier));

        // 绑定用户（签发方绑定）
        var entity = await ds.GetByTicketAsync(ticket);
        entity!.UserId = "u-123";
        await ds.UpdateAsync(entity); // 绑定后回填（生产由签发方绑定后 Update）

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
        var (service, ds, _) = CreateService(AuthenticationTestHost.CreateOptions());
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri, CodeVerifier: "abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz"));
        var entity = await ds.GetByTicketAsync(ticket);
        entity!.UserId = "u-123";
        await ds.UpdateAsync(entity);

        // hash 非 null + verifier null → 拒绝（防无 verifier 请求绕过——C4）
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, null, "app-1", null)));
        Assert.Equal(OAuthTicketErrorCodes.TicketStateMismatch, ex.Message);
    }

    [Fact]
    public async Task Exchange_WrongAppId_Rejected()
    {
        var (service, ds, _) = CreateService(AuthenticationTestHost.CreateOptions());
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri));
        var entity = await ds.GetByTicketAsync(ticket);
        entity!.UserId = "u-123";
        await ds.UpdateAsync(entity);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, null, "app-2", null)));
        Assert.Equal(OAuthTicketErrorCodes.TicketStateMismatch, ex.Message);
    }

    [Fact]
    public async Task Issue_RedirectUriNotInWhitelist_Rejected()
    {
        var (service, _, _) = CreateService(AuthenticationTestHost.CreateOptions());
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.IssueAsync(new OAuthTicketIssueRequest("app-1", "https://evil.example.com/callback")));
    }

    [Fact]
    public async Task Exchange_PkceMatrix_ServerTrust_NullNull_Allowed()
    {
        // C4 第一行：hash null（服务端/trust 签发）+ verifier null（换取）→ 放行（信任服务端路径——依赖 AppId 白名单 + 内网信任）
        var (service, ds, tokenService) = CreateService(AuthenticationTestHost.CreateOptions());
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri));
        var entity = await ds.GetByTicketAsync(ticket);
        entity!.UserId = "u-123";
        await ds.UpdateAsync(entity);

        var result = await service.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, null, "app-1", null));
        Assert.NotNull(result.AccessToken);
        Assert.NotNull(result.RefreshToken);
    }

    [Fact]
    public async Task Exchange_PkceMatrix_VerifierMismatch_Rejected()
    {
        // C4 第三行：hash 非 null + verifier 非 null 但 SHA256 不匹配 → 拒绝
        var (service, ds, _) = CreateService(AuthenticationTestHost.CreateOptions());
        var codeVerifier = "abcdefghijklmnopqrstuvwxyz0123456789abcdefghijklmnopqrstuvwxyz";
        var ticket = await service.IssueAsync(new OAuthTicketIssueRequest("app-1", RedirectUri, CodeVerifier: codeVerifier));
        var entity = await ds.GetByTicketAsync(ticket);
        entity!.UserId = "u-123";
        await ds.UpdateAsync(entity);

        var wrongVerifier = codeVerifier[..^1] + "A"; // 末位改 A（仍在 unreserved 集合）
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() =>
            service.ExchangeAsync(new OAuthTicketExchangeRequest(ticket, wrongVerifier, "app-1", null)));
        Assert.Equal(OAuthTicketErrorCodes.TicketStateMismatch, ex.Message);
    }

    [Fact]
    public async Task Exchange_ExpiredTicket_Rejected()
    {
        var (service, ds, _) = CreateService(AuthenticationTestHost.CreateOptions());
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
}
