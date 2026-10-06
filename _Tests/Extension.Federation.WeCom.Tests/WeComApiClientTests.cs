using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TKWF.Federation.WeCom;
using Xunit;

namespace TKWF.Federation.WeCom.Tests;

/// <summary>
/// <see cref="WeComApiClient"/> 出站客户端直构测试（public 类型——new HttpClient(stub) + NullLogger，
/// 不经 DI 宿主）——gettoken 企业 access_token / getuserinfo 三路径身份解析（P0-1）/
/// user_ticket 敏感信息即用即弃（P1-4 数据流闭环入口）。
/// </summary>
public class WeComApiClientTests
{
    private const string TestCorpId = "ww_test_corp";
    private const string TestCorpSecret = "test-secret";
    private const string TestUserId = "usr_123";
    private const string TestOpenUserId = "ou_test_1";
    private const string TestOpenId = "oa_test_openid_1";

    private static WeComApiClient CreateClient(
        WeComMemberType memberType = WeComMemberType.SelfMember,
        bool failOauth = false,
        bool failSensitive = false,
        bool withUserTicket = false)
    {
        var handler = WeComTestHost.CreateStubHandler(
            memberType, corpId: TestCorpId, failOauth: failOauth, failSensitive: failSensitive, withUserTicket: withUserTicket);
        return new WeComApiClient(new HttpClient(handler), NullLogger<WeComApiClient>.Instance);
    }

    [Fact]
    public async Task GetAccessTokenAsync_Valid_ReturnsToken()
    {
        var client = CreateClient();
        var token = await client.GetAccessTokenAsync(TestCorpId, TestCorpSecret, CancellationToken.None);
        Assert.Equal("stub-token", token);
    }

    [Fact]
    public async Task GetIdentityAsync_SelfMember_ReturnsUserId_AndUserTicket()
    {
        // 自建应用企业成员（P0-1 路径①）——UserId + UserTicket 条件性透出（P1-4：snsapi_privateinfo 响应）
        var client = CreateClient(WeComMemberType.SelfMember, withUserTicket: true);
        var identity = await client.GetIdentityAsync(TestCorpId, TestCorpSecret, "code-1", isThirdParty: false, CancellationToken.None);

        Assert.Equal(TestUserId, identity.UserId);
        Assert.Null(identity.OpenUserid);
        Assert.Null(identity.OpenId);
        Assert.Equal("stub-ticket", identity.UserTicket);  // 敏感信息票据透出（供装配层按需 GetSensitiveInfoAsync）
    }

    [Fact]
    public async Task GetIdentityAsync_ThirdParty_ReturnsOpenUserid()
    {
        // 三方应用企业成员（P0-1 路径②）——getuserinfo3rd → Userid + OpenUserID（全局唯一优先作 external_uid）
        var client = CreateClient(WeComMemberType.ThirdPartyMember);
        var identity = await client.GetIdentityAsync(TestCorpId, TestCorpSecret, "code-3rd", isThirdParty: true, CancellationToken.None);

        Assert.Equal(TestOpenUserId, identity.OpenUserid);
        Assert.Equal(TestUserId, identity.UserId);  // 组织维度辅助（DTO 透出）
        Assert.Null(identity.OpenId);
    }

    [Fact]
    public async Task GetIdentityAsync_NonMember_ReturnsOpenId()
    {
        // 非企业成员（P0-1 路径③）——OpenId（对当前企业唯一）
        var client = CreateClient(WeComMemberType.NonMember);
        var identity = await client.GetIdentityAsync(TestCorpId, TestCorpSecret, "code-openid", isThirdParty: false, CancellationToken.None);

        Assert.Equal(TestOpenId, identity.OpenId);
        Assert.Null(identity.UserId);
    }

    [Fact]
    public async Task GetIdentityAsync_ApiError_Throws()
    {
        // getuserinfo errcode（code 非法/已消费）→ InvalidOperationException（channel 捕获转 FailReason）
        var client = CreateClient(failOauth: true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetIdentityAsync(TestCorpId, TestCorpSecret, "stale-code", isThirdParty: false, CancellationToken.None));
    }

    [Fact]
    public async Task GetSensitiveInfoAsync_Valid_ReturnsSensitiveInfo()
    {
        // user_ticket 换敏感信息（P1-4 数据流闭环入口——即用即弃不落库）
        var client = CreateClient();
        var info = await client.GetSensitiveInfoAsync("stub-ticket", CancellationToken.None);

        Assert.Equal("usr_123", info.UserId);
        Assert.Equal("张三", info.Name);       // 敏感字段（防日志泄露标注——P2-3）
        Assert.Equal("138****1234", info.Mobile);
        Assert.Equal("a@b.c", info.Email);
        Assert.Equal(1, info.Gender);
    }

    [Fact]
    public async Task GetSensitiveInfoAsync_ApiError_Throws()
    {
        var client = CreateClient(failSensitive: true);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => client.GetSensitiveInfoAsync("stale-ticket", CancellationToken.None));
    }
}
