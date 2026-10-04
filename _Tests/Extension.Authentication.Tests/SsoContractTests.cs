using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>
/// V0.6.0（SSO 立项，ADR-SSO Oracle P1-2）：SSO 消费面契约验证——
/// <see cref="ISsoAccountQueryService"/>（返回不可变 DTO，敏感字段不出契约包）+ <see cref="ISsoChannelMapService"/>
/// （(channel_id, external_user_id) → uid 联邦映射 upsert）+ 联盟锚点列（<see cref="AuthAccountEntity.FederationAnchorOpenId"/>）。
/// </summary>
public class SsoContractTests
{
    private static (AuthAccountQueryService AccountService, PlatformAccountMapService MapService, AuthAccountEntityDataService AuthDs, PlatformAccountMapEntityDataService MapDs) CreateServices()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var accountService = new AuthAccountQueryService(stub);
        var mapService = new PlatformAccountMapService(stub, NullLogger<PlatformAccountMapService>.Instance);
        return (accountService, mapService, stub.Use<AuthAccountEntityDataService>(), stub.Use<PlatformAccountMapEntityDataService>());
    }

    [Fact]
    public async Task SsoAccountQuery_ReturnsDto_WithoutSensitiveFields()
    {
        var (service, _, authDs, _) = CreateServices();
        var entity = new AuthAccountEntity
        {
            UId = "u-100",
            Phone = "13800138000",
            FederationAnchorOpenId = "anchor-abc",
            Nickname = "张三",
            Avatar = "https://cdn.example.com/a.png",
            AuthLevel = 2,
            TokenVersion = 5,           // 敏感字段——DTO 不得含
            IsEnabled = true,           // 敏感字段——DTO 不得含
        };
        await authDs.EntityCreateAsync(entity, default);

        var dto = await service.GetByUIdAsync("u-100");   // IAuthAccountQueryService（实体形态，既有）
        var ssoDto = await ((ISsoAccountQueryService)service).GetByUIdAsync("u-100", default);  // SSO 消费面（DTO 形态，显式实现）

        Assert.NotNull(dto);
        Assert.Equal("u-100", dto!.UId);

        Assert.NotNull(ssoDto);
        Assert.Equal("u-100", ssoDto!.UId);
        Assert.Equal("anchor-abc", ssoDto.FederationAnchorOpenId);
        Assert.Equal("张三", ssoDto.Nickname);
        Assert.Equal("https://cdn.example.com/a.png", ssoDto.AvatarUrl);
        Assert.Equal(2, ssoDto.AuthLevel);
    }

    [Fact]
    public async Task SsoAccountQuery_UnknownUid_ReturnsNull()
    {
        var (service, _, _, _) = CreateServices();
        var ssoDto = await ((ISsoAccountQueryService)service).GetByUIdAsync("u-not-exist", default);
        Assert.Null(ssoDto);
    }

    [Fact]
    public async Task SsoChannelMap_Link_CreatesAndIdempotentUpsert()
    {
        var (_, service, _, mapDs) = CreateServices();

        var created = await ((ISsoChannelMapService)service).LinkAsync("u-100", "mp-account-1", "openid2-aaa", default);
        Assert.Equal("u-100", created.UId);
        Assert.Equal("mp-account-1", created.ChannelId);
        Assert.Equal("openid2-aaa", created.ExternalUserId);

        // 幂等：同 (channelId, externalUserId) 再次 Link → 不新建、返回既有映射
        var again = await ((ISsoChannelMapService)service).LinkAsync("u-100", "mp-account-1", "openid2-aaa", default);
        Assert.Equal(created.UId, again.UId);

        var all = await mapDs.EntitySelectAsync(m => true, ct: default);
        Assert.Single(all);   // upsert 不重复建行
    }

    [Fact]
    public async Task SsoChannelMap_GetByChannel_ReturnsDto()
    {
        var (_, service, _, _) = CreateServices();
        await ((ISsoChannelMapService)service).LinkAsync("u-100", "mp-account-1", "openid2-aaa", default);

        var found = await ((ISsoChannelMapService)service).GetByChannelAsync("mp-account-1", "openid2-aaa", default);
        Assert.NotNull(found);
        Assert.Equal("u-100", found!.UId);

        var notFound = await ((ISsoChannelMapService)service).GetByChannelAsync("mp-account-2", "openid2-aaa", default);
        Assert.Null(notFound);   // channel 隔离
    }

    [Fact]
    public async Task FederationAnchorOpenId_Column_UniqueAndNullable()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var authDs = stub.Use<AuthAccountEntityDataService>();

        // 可空：无锚点账号（微信便捷登录）不冲突
        await authDs.EntityCreateAsync(new AuthAccountEntity { UId = "u-1", Phone = "13800138000" }, default);
        await authDs.EntityCreateAsync(new AuthAccountEntity { UId = "u-2", Phone = "13800138001" }, default);

        // 唯一：同一锚点不可重复绑定
        var withAnchor = new AuthAccountEntity { UId = "u-3", Phone = "13800138002", FederationAnchorOpenId = "anchor-xyz" };
        await authDs.EntityCreateAsync(withAnchor, default);

        var dup = new AuthAccountEntity { UId = "u-4", Phone = "13800138003", FederationAnchorOpenId = "anchor-xyz" };
        await Assert.ThrowsAnyAsync<System.Exception>(() => authDs.EntityCreateAsync(dup, default));

        // 按锚点反查（Conditions 生成物）
        var byAnchor = await authDs.EntitySelectAsync(
            e => e.FederationAnchorOpenId == "anchor-xyz", ct: default);
        var list = byAnchor.ToList();
        Assert.Single(list);
        Assert.Equal("u-3", list[0].UId);
    }
}
