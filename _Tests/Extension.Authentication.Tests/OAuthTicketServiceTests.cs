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
        var ticketDs = new OAuthTicketEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<OAuthTicketEntity>(new UnitOfWorkManager(fsql)));
        var accountDs = new AuthAccountEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthAccountEntity>(new UnitOfWorkManager(fsql)));
        var refreshDs = new AuthRefreshTokenEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthRefreshTokenEntity>(new UnitOfWorkManager(fsql)));
        var blacklistDs = new AuthTokenBlacklistEntityDataService(
            new StubDomainUser(), new FreeSqlEntityDAC<AuthTokenBlacklistEntity>(new UnitOfWorkManager(fsql)));
        var tokenService = new TokenService(
            Options.Create(options), accountDs, refreshDs, blacklistDs,
            new Microsoft.Extensions.Caching.Memory.MemoryCache(new Microsoft.Extensions.Caching.Memory.MemoryCacheOptions()),
            NullLogger<TokenService>.Instance);
        tokenService.EnsureKeysLoaded();
        return (new OAuthTicketService(Options.Create(options), ticketDs, tokenService, NullLogger<OAuthTicketService>.Instance), ticketDs, tokenService);
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
