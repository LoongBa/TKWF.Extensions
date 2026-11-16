using System.Security.Authentication;
using TKWF.Ext.AuthCenter;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuthSurface.Tests;

/// <summary>
/// v0.3.0 码侧契约测试（T-4/T-5/T-6/T-8）——expireAt 绝对有效期与 validity 互斥 / Revoked 兑换归类 /
/// 按明文读码（含批次 test/channel）/ 兑换历史分页 / 管理端 payload 解密。
/// </summary>
public class AuthSurfaceV030ContractTests
{
    private static ServiceProvider CreateProvider(IFreeSql fsql, Action<IServiceCollection>? configure = null)
        => AuthSurfaceTestHost.CreateProvider(fsql, configure);

    private static DomainUser<TestUserInfo> BindUser(ServiceProvider sp, string userId = "u-1001")
    {
        DomainUser<TestUserInfo>.BindScope(sp);
        return new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo(userId, "测试用户") };
    }

    private static async Task<long> IdByHashAsync(IFreeSql fsql, string plain)
        => (await fsql.Select<RedemptionCodeEntity>().Where(e => e.CodeHash == RedemptionCodeGenerator.Hash(plain)).ToOneAsync())!.Id;

    // ── T-4：expireAtUtc 绝对 UTC + 与 validity 互斥 ──

    [Fact]
    public async Task CreateCode_ExpireAt_PersistsAbsoluteUtc_AndValidityExclusive()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var commands = user.Use<IRedemptionCommandService>();

        var expireAt = new DateTime(2027, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var plain = await commands.CreateCodeAsync("券A", "peilian", ct: CancellationToken.None, expireAtUtc: expireAt);
        var row = await fsql.Select<RedemptionCodeEntity>()
            .Where(e => e.CodeHash == RedemptionCodeGenerator.Hash(plain)).ToOneAsync();
        Assert.NotNull(row!.ExpireAtUtc);
        // 绝对时间点语义：落库值与本机时区解释下的 expireAt 日期一致（SQLite 往返丢 Kind——README 已知限制；生产 PG 无偏移）
        Assert.Equal(expireAt.ToLocalTime().Date, row.ExpireAtUtc!.Value.Date);

        await Assert.ThrowsAsync<ArgumentException>(() => commands.CreateCodeAsync(
            "券B", "peilian", TimeSpan.FromDays(30), CancellationToken.None, null, expireAt));
    }

    // ── T-3：Revoked 兑换归类（403 语义）──

    [Fact]
    public async Task Redeem_RevokedCode_ThrowsRedemptionCodeRevoked()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        await AuthSurfaceTestHost.SeedAccountAsync(fsql, "u-1001");
        var commands = user.Use<IRedemptionCommandService>();

        var plain = await commands.CreateCodeAsync("券C", "peilian", ct: CancellationToken.None);
        var id = await IdByHashAsync(fsql, plain);
        await fsql.Update<RedemptionCodeEntity>().Where(e => e.Id == id).Set(e => e.Status, 3).ExecuteAffrowsAsync();

        var ex = await Assert.ThrowsAsync<AuthenticationException>(() => commands.RedeemAsync("u-1001", plain, CancellationToken.None));
        Assert.Equal(RedemptionErrorCodes.RedemptionCodeRevoked, ex.Message);
    }

    // ── T-5：按明文读码（含批次 test/channel；不存在 → null）──

    [Fact]
    public async Task GetByCode_Existing_ReturnsLookupWithBatchInfo_AndNullForMissing()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var batches = user.Use<ICodeBatchService>();
        var queries = user.Use<IRedemptionQueryService>();

        var batch = await batches.CreateBatchAsync(new CreateCodeBatchRequest(
            Name: "测试批", ProductName: "英语陪练", TargetAppId: "peilian", ValidityDays: 30,
            ExpireAtUtc: null, Channel: "campaign", Quantity: 3, Test: true, CreatedBy: "admin"), CancellationToken.None);
        var plains = new List<string>();
        await batches.GenerateBatchAsync(batch.Id, (p, _) => { plains.Add(p); return Task.CompletedTask; }, CancellationToken.None);

        var lookup = await queries.GetByCodeAsync(plains[0], CancellationToken.None);
        Assert.NotNull(lookup);
        Assert.Equal("peilian", lookup!.TargetAppId);
        Assert.Equal(0, lookup.Status);
        Assert.Equal(batch.Id, lookup.BatchId);
        Assert.True(lookup.BatchTest);                        // 头行两跳取 test
        Assert.Equal("campaign", lookup.Channel);

        Assert.Null(await queries.GetByCodeAsync("EDU-XXXX-XXXX-XXXX", CancellationToken.None));
    }

    // ── T-8：payload 解密（可重复只读；无 payload → null）──

    [Fact]
    public async Task GetPayload_ReturnsPlaintextRepeatable_AndNullWhenAbsent()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var commands = user.Use<IRedemptionCommandService>();
        var queries = user.Use<IRedemptionQueryService>();

        const string payload = "{\"buyer\":\"张三\",\"orderNo\":\"A001\"}";
        var plain = await commands.CreateCodeAsync("券D", "peilian", ct: CancellationToken.None, payload: payload);
        var id = await IdByHashAsync(fsql, plain);

        Assert.Equal(payload, await queries.GetPayloadAsync(id, CancellationToken.None));
        Assert.Equal(payload, await queries.GetPayloadAsync(id, CancellationToken.None));   // 可重复只读（不消费不擦除）

        var noPayload = await commands.CreateCodeAsync("券E", "peilian", ct: CancellationToken.None);
        Assert.Null(await queries.GetPayloadAsync(await IdByHashAsync(fsql, noPayload), CancellationToken.None));
        Assert.Null(await queries.GetPayloadAsync(999_999, CancellationToken.None));        // 不存在 → null
    }

    // ── T-7 / C-3：兑换 → redeem 源授权写入链路（消费方编排形态）──

    [Fact]
    public async Task Redeem_ThenRecordRedeemGrant_WritesRedeemSourceWithValidUntil()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        await AuthSurfaceTestHost.SeedAccountAsync(fsql, "u-1001");

        var plain = await user.Use<IRedemptionCommandService>().CreateCodeAsync("券F", "peilian", ct: CancellationToken.None);
        await user.Use<IRedemptionCommandService>().RedeemAsync("u-1001", plain, CancellationToken.None);

        var validUntil = DateTime.UtcNow.AddDays(30);
        await user.Use<TKWF.Ext.AuthCenter.IAuthGrantCommandService>().RecordGrantAsync(
            "u-1001", "peilian", TKWF.Ext.AuthCenter.AuthGrantSources.Redeem, validUntil, CancellationToken.None);

        var grant = await fsql.Select<AuthGrantEntity>()
            .Where(e => e.UserId == "u-1001" && e.AppId == "peilian" && e.Source == "redeem").ToOneAsync();
        Assert.NotNull(grant);
        Assert.Equal(0, grant!.Status);
        Assert.NotNull(grant.ValidUntil);
        // 物理原子性（CAS + grant 写同一事务真回滚）归 PostgreSQL——docs/测试约定-SQLite物理事务与Noop限制.md §2.2
    }

    // ── T-6：兑换历史分页（null=全量）──

    [Fact]
    public async Task GetRedemptions_Paging_NullMeansAll()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        await AuthSurfaceTestHost.SeedAccountAsync(fsql, "u-1001");
        var commands = user.Use<IRedemptionCommandService>();
        var queries = user.Use<IRedemptionQueryService>();

        for (var i = 0; i < 3; i++)
        {
            var plain = await commands.CreateCodeAsync($"券{i}", "peilian", ct: CancellationToken.None);
            await commands.RedeemAsync("u-1001", plain, CancellationToken.None);
        }

        Assert.Equal(3, (await queries.GetRedemptionsAsync("u-1001", ct: CancellationToken.None)).Count);
        Assert.Equal(2, (await queries.GetRedemptionsAsync("u-1001", page: 1, pageSize: 2, ct: CancellationToken.None)).Count);
        Assert.Single(await queries.GetRedemptionsAsync("u-1001", page: 2, pageSize: 2, ct: CancellationToken.None));
        Assert.Empty(await queries.GetRedemptionsAsync("u-1001", page: 9, pageSize: 2, ct: CancellationToken.None));
    }
}
