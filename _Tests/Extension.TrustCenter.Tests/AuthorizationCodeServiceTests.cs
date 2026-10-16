using System;
using System.Security.Authentication;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Ticket;

namespace TKWF.Ext.TrustCenter.Tests;

/// <summary>
/// AuthorizationCode 专项测试（面②——2026-10-16 拆分命名 ADR109 D4：<see cref="AuthorizationCodeService"/>，
/// 表 TKWF_AuthorizationCode，独立 <see cref="IAuthorizationCodeService"/> 契约）。
/// <para>语义 = OAuth 授权码（原 AccessCodeService 联邦流语义平移）：单次原子 CAS + SHA256 存储 + PKCE 可选 +
/// 失败抛异常（错误码沿用 accesscode 系列：ACCESS_CODE_NOT_FOUND/TICKET_EXPIRED/TICKET_CONSUMED/TICKET_STATE_MISMATCH）。</para>
/// </summary>
public class AuthorizationCodeServiceTests
{
    private static (AuthorizationCodeService Service, AuthorizationCodeEntityDataService Ds) CreateService(
        int? authorizationCodeRetentionDays = null)
    {
        var fsql = TrustCenterTestHost.CreateInMemoryFreeSql();
        var stub = TrustCenterTestHost.CreateStub(fsql);
        var options = TrustCenterTestHost.CreateOptions();
        if (authorizationCodeRetentionDays is { } days)
            options.AuthorizationCodeRetentionDays = days;
        var service = new AuthorizationCodeService(stub, Options.Create(options), NullLogger<AuthorizationCodeService>.Instance);
        return (service, stub.Use<AuthorizationCodeEntityDataService>());
    }

    [Fact]
    public async Task Issue_StoresHashOnly_ReturnsCode()
    {
        var (service, ds) = CreateService();
        var issued = await service.IssueAsync(new AuthorizationCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", OpenId: "openid-xxx", IpAddress: "1.2.3.4"), default);

        Assert.NotNull(issued.Code);
        Assert.Equal(120, issued.ExpiresIn);

        // 落库只存 SHA256 hash——原文绝不出现
        var row = await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued.Code), default);
        Assert.NotNull(row);
        Assert.Equal("u-100", row!.UId);
        Assert.Equal("app-1", row.TargetAppId);
        Assert.Equal("mp-1", row.ChannelId);
        Assert.Equal("1.2.3.4", row.IpAddress);
        Assert.Null(row.CodeVerifierHash);   // 未传 verifier
    }

    [Fact]
    public async Task Consume_SingleUse_ReplayRejected()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(new AuthorizationCodeIssueRequest("mp-1", "u-100", "app-1", "profile:basic"), default);

        var consumed = await service.ConsumeAsync(issued.Code, default);
        Assert.Equal("u-100", consumed.UId);
        Assert.Equal("app-1", consumed.TargetAppId);

        // 同 code 二次消费 → TICKET_CONSUMED（原子 CAS 重放拒）
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync(issued.Code, default));
        Assert.Contains("TICKET_CONSUMED", ex.Message);
    }

    [Fact]
    public async Task Consume_UnknownCode_Throws()
    {
        var (service, _) = CreateService();
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync("not-a-real-code", default));
        Assert.Contains("ACCESS_CODE_NOT_FOUND", ex.Message);
    }

    [Fact]
    public async Task Consume_Expired_Throws()
    {
        var (service, ds) = CreateService();
        var issued = await service.IssueAsync(new AuthorizationCodeIssueRequest("mp-1", "u-100", "app-1", "profile:basic"), default);

        // 确定性老化（对齐既有模式）
        await ds.EntityUpdateWhereAsync(
            m => m.CodeHash == Token2Service.Sha256Hex(issued.Code),
            m => new { ExpiresAt = DateTime.UtcNow.AddMinutes(-1) },
            default);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync(issued.Code, default));
        Assert.Contains("TICKET_EXPIRED", ex.Message);
    }

    [Fact]
    public async Task Consume_WithPkce_MissingVerifier_Rejected()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(new AuthorizationCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", CodeVerifier: "verifier-abc"), default);

        // 签发时有 verifier → 消费必传，缺失拒（TICKET_STATE_MISMATCH）
        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync(issued.Code, default));
        Assert.Contains("TICKET_STATE_MISMATCH", ex.Message);
    }

    [Fact]
    public async Task Consume_WithPkce_WrongVerifier_Rejected()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(new AuthorizationCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", CodeVerifier: "verifier-abc"), default);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync(issued.Code, "wrong-verifier", default));
        Assert.Contains("TICKET_STATE_MISMATCH", ex.Message);
    }

    [Fact]
    public async Task Consume_WithPkce_CorrectVerifier_Succeeds()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(new AuthorizationCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", CodeVerifier: "verifier-abc"), default);

        var consumed = await service.ConsumeAsync(issued.Code, "verifier-abc", default);
        Assert.Equal("u-100", consumed.UId);
    }

    [Fact]
    public async Task CleanupExpired_RemovesStaleRows_KeepsActive()
    {
        var (service, ds) = CreateService();
        var issued1 = await service.IssueAsync(new AuthorizationCodeIssueRequest("mp-1", "u-100", "app-1", "profile:basic"), default);
        var issued2 = await service.IssueAsync(new AuthorizationCodeIssueRequest("mp-1", "u-200", "app-1", "profile:basic"), default);

        var old = DateTime.UtcNow.AddDays(-30);
        await ds.EntityUpdateWhereAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued1.Code), m => new { ExpiresAt = old }, default);
        await ds.EntityUpdateWhereAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued2.Code), m => new { ExpiresAt = old }, default);

        // 未过期行（新签发仍有效）不受影响
        var issued3 = await service.IssueAsync(new AuthorizationCodeIssueRequest("mp-1", "u-300", "app-1", "profile:basic"), default);

        var deleted = await service.CleanupExpiredAsync();
        Assert.Equal(2, deleted);

        Assert.Null(await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued1.Code), default));
        Assert.Null(await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued2.Code), default));
        Assert.NotNull(await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued3.Code), default));
    }

    [Fact]
    public async Task CleanupExpired_UsesConfiguredRetentionDays_ZeroSkips()
    {
        var (service, ds) = CreateService(authorizationCodeRetentionDays: 0);
        var issued = await service.IssueAsync(new AuthorizationCodeIssueRequest("mp-1", "u-100", "app-1", "profile:basic"), default);
        await ds.EntityUpdateWhereAsync(
            m => m.CodeHash == Token2Service.Sha256Hex(issued.Code),
            m => new { ExpiresAt = DateTime.UtcNow.AddDays(-2) },
            default);

        // retention=0 → 跳过清理（返回 0，不删除）
        Assert.Equal(0, await service.CleanupExpiredAsync());
        Assert.NotNull(await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued.Code), default));
    }
}
