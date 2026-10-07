using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>
/// V0.6.0（SSO 立项，ADR-SSO Oracle P1-2）：SSO 消费面契约验证——
/// <see cref="ISsoAccountQueryService"/>（返回不可变 DTO，敏感字段不出契约包）+ <see cref="ISsoChannelMapService"/>
/// （(channel_id, external_user_id) → uid 联邦映射 upsert）+ 联盟锚点列（<see cref="AuthAccountEntity.FederationAnchorOpenId"/>）。
/// </summary>
public class SsoContractTests
{
    private static (AuthAccountQueryService AccountService, PlatformAccountMapService MapService, AuthAccountEntityDataService AuthDs, PlatformAccountMapEntityDataService MapDs, UserProfileEntityDataService ProfileDs) CreateServices()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var accountService = new AuthAccountQueryService(stub);
        var mapService = new PlatformAccountMapService(stub, NullLogger<PlatformAccountMapService>.Instance);
        return (accountService, mapService,
            stub.Use<AuthAccountEntityDataService>(), stub.Use<PlatformAccountMapEntityDataService>(),
            stub.Use<UserProfileEntityDataService>());   // V0.9.0（T3）：档案 DataService（凭据/档案分离）
    }

    [Fact]
    public async Task SsoAccountQuery_ReturnsDto_WithoutSensitiveFields()
    {
        var (service, _, authDs, _, profileDs) = CreateServices();
        var entity = new AuthAccountEntity
        {
            UId = "u-100",
            Phone = "13800138000",
            FederationAnchorOpenId = "anchor-abc",
            AuthLevel = 2,
            TokenVersion = 5,           // 敏感字段——DTO 不得含
            IsEnabled = true,           // 敏感字段——DTO 不得含
        };
        await authDs.EntityCreateAsync(entity, default);

        // V0.9.0（T3/A.1）：档案（Nickname/Avatar）从 AuthAccount 迁 UserProfile 1:1 表——SSO DTO 档案经档案表读
        await profileDs.CreateOrUpdateAsync(new UserProfileEntity
        {
            UId = "u-100",
            Nickname = "张三",
            Avatar = "https://cdn.example.com/a.png",
        }, default);

        var dto = await service.GetByUIdAsync("u-100");   // IAuthAccountQueryService（实体形态，既有）
        var ssoDto = await ((ISsoAccountQueryService)service).GetByUIdAsync("u-100", default);  // SSO 消费面（DTO 形态，显式实现）

        Assert.NotNull(dto);
        Assert.Equal("u-100", dto!.UId);

        Assert.NotNull(ssoDto);
        Assert.Equal("u-100", ssoDto!.UId);
        Assert.Equal("anchor-abc", ssoDto.FederationAnchorOpenId);
        Assert.Equal("张三", ssoDto.Nickname);              // 档案经 UserProfile 读（V0.9.0 凭据/档案分离）
        Assert.Equal("https://cdn.example.com/a.png", ssoDto.AvatarUrl);
        Assert.Equal(2, ssoDto.AuthLevel);
    }

    [Fact]
    public async Task SsoAccountQuery_UnknownUid_ReturnsNull()
    {
        var (service, _, _, _, _) = CreateServices();
        var ssoDto = await ((ISsoAccountQueryService)service).GetByUIdAsync("u-not-exist", default);
        Assert.Null(ssoDto);
    }

    [Fact]
    public async Task SsoChannelMap_Link_CreatesAndIdempotentUpsert()
    {
        var (_, service, _, mapDs, _) = CreateServices();

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
        var (_, service, _, _, _) = CreateServices();
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

    // ── SSO.WeChat 评审 P1-1：ISsoAccountLinkService（联盟锚点写契约）──

    [Fact]
    public async Task LinkService_SetFederationAnchor_And_GetByAnchor()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var authDs = stub.Use<AuthAccountEntityDataService>();
        var linkSvc = new AuthAccountQueryService(stub);

        await authDs.EntityCreateAsync(new AuthAccountEntity { UId = "u-100", Phone = "13800138000" }, default);

        // 写锚点 → 落库
        var updated = await linkSvc.SetFederationAnchorAsync("u-100", "anchor-abc", default);
        Assert.Equal("u-100", updated.UId);
        Assert.Equal("anchor-abc", updated.FederationAnchorOpenId);

        // 反查（后续任一 openid 直认链路）
        var byAnchor = await linkSvc.GetByFederationAnchorAsync("anchor-abc", default);
        Assert.NotNull(byAnchor);
        Assert.Equal("u-100", byAnchor!.UId);

        // 未绑定锚点账号反查 → null；未知账号写锚点 → 异常
        Assert.Null(await linkSvc.GetByFederationAnchorAsync("not-exist", default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => linkSvc.SetFederationAnchorAsync("u-not-exist", "anchor-x", default));
    }

    [Fact]
    public async Task LinkService_AnchorUniqueness_ConflictThrows()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var authDs = stub.Use<AuthAccountEntityDataService>();
        var linkSvc = new AuthAccountQueryService(stub);

        await authDs.EntityCreateAsync(new AuthAccountEntity { UId = "u-1", Phone = "13800138000" }, default);
        await authDs.EntityCreateAsync(new AuthAccountEntity { UId = "u-2", Phone = "13800138001" }, default);
        await linkSvc.SetFederationAnchorAsync("u-1", "anchor-dup", default);

        // 同一锚点绑到另一账号 → 唯一索引冲突（唯一约束日志可含 account：确保共用 SQLite 语义）
        await Assert.ThrowsAnyAsync<System.Exception>(() => linkSvc.SetFederationAnchorAsync("u-2", "anchor-dup", default));
    }
}
