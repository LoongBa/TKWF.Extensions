using System;
using System.Security.Authentication;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// accesscode 服务测试（T3——授权码：签发/消费/重放/PKCE）。
/// <para>原子 CAS 经 EntityUpdateWhereAsync（ADR89）——同 code 二次消费须拒（TICKET_CONSUMED）。</para>
/// </summary>
public class SsoAccessCodeServiceTests
{
    private static (SsoAccessCodeService Service, SsoAccessCodeEntityDataService Ds) CreateService()
    {
        var fsql = SsoTestHost.CreateInMemoryFreeSql();
        var stub = SsoTestHost.CreateStub(fsql);
        var options = SsoTestHost.CreateOptions();
        var service = new SsoAccessCodeService(stub, Options.Create(options), NullLogger<SsoAccessCodeService>.Instance);
        return (service, stub.Use<SsoAccessCodeEntityDataService>());
    }

    [Fact]
    public async Task Issue_StoresHashOnly_ReturnsCode()
    {
        var (service, ds) = CreateService();
        var issued = await service.IssueAsync(new SsoAccessCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", OpenId: "openid-xxx", IpAddress: "1.2.3.4"), default);

        Assert.NotNull(issued.Code);
        Assert.Equal(120, issued.ExpiresIn);

        // 落库只存 SHA256 hash——原文绝不出现在库中
        var row = await ds.EntityGetAsync(m => m.CodeHash == Token2Service.Sha256Hex(issued.Code), default);
        Assert.NotNull(row);
        Assert.Equal("u-100", row!.UId);
        Assert.Equal("app-1", row.TargetAppId);
        Assert.Equal("mp-1", row.ChannelId);
        Assert.Null(row.CodeVerifierHash);   // 未传 verifier
    }

    [Fact]
    public async Task Consume_SingleUse_ReplayRejected()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(new SsoAccessCodeIssueRequest("mp-1", "u-100", "app-1", "profile:basic"), default);

        var consumed = await service.ConsumeAsync(issued.Code, default);
        Assert.Equal("u-100", consumed.UId);
        Assert.Equal("app-1", consumed.TargetAppId);

        // 同 code 二次消费 → TICKET_CONSUMED（原子 CAS 重放拒）
        await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync(issued.Code, default));
    }

    [Fact]
    public async Task Consume_UnknownCode_Throws()
    {
        var (service, _) = CreateService();
        await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync("not-a-real-code", default));
    }

    [Fact]
    public async Task Consume_WithPkce_MissingVerifier_Rejected()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(new SsoAccessCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", CodeVerifier: "verifier-abc"), default);

        // 签发时有 verifier → 消费必传，缺失拒
        await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync(issued.Code, default));
    }

    [Fact]
    public async Task Consume_WithPkce_WrongVerifier_Rejected()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(new SsoAccessCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", CodeVerifier: "verifier-abc"), default);

        await Assert.ThrowsAsync<AuthenticationException>(() => service.ConsumeAsync(issued.Code, "wrong-verifier", default));
    }

    [Fact]
    public async Task Consume_WithPkce_CorrectVerifier_Succeeds()
    {
        var (service, _) = CreateService();
        var issued = await service.IssueAsync(new SsoAccessCodeIssueRequest(
            "mp-1", "u-100", "app-1", "profile:basic", CodeVerifier: "verifier-abc"), default);

        var consumed = await service.ConsumeAsync(issued.Code, "verifier-abc", default);
        Assert.Equal("u-100", consumed.UId);
    }
}
