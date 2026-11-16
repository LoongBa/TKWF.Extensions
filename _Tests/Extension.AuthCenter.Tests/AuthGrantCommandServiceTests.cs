using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Xunit;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>V0.8.0 应用授权写入门面测试——upsert 幂等 + 唯一约束 UX_AuthGrant_User_App_Source 兜底（Oracle P0-2/P1-1）。
    /// <para>红线合规：经真实 FreeSqlEntityDAC（全部 9 实体）+ StubDomainUser 直构门面（经基类 User 取上下文），
    /// DataService 经 User.Use&lt;具体类&gt;() NoAop 直建——零 IFreeSql/IEntityDAC 直注入。</para>
    /// <para>V0.3.0（T-7 泛化）：<see cref="IAuthGrantCommandService.RecordGrantAsync"/> Source/ValidUntil 参数化——
    /// 冲突路径经 <c>RefreshActiveWithValidUntilAsync</c> 覆写 ValidUntil（现 RefreshActive 缺口的回归护栏）；
    /// <see cref="IAuthGrantCommandService.RecordLoginGrantAsync"/> 委托后行为不变（ValidUntil 恒 null）。
    /// ⚠️ C-3 跨门面事务用例（CAS + RecordGrantAsync 同一 TransactionScope 回滚）**留 AuthSurface 测试项目**
    /// （宿主双白名单 + 码实体在彼侧；本文件仅 AuthCenter 单侧用例）。</para></summary>
    public class AuthGrantCommandServiceTests
    {
        private static readonly DateTime UtcA = new(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime UtcB = new(2027, 6, 30, 0, 0, 0, DateTimeKind.Utc);

        /// <summary>SQLite UTC 往返容差断言（FreeSql SQLite provider 对 UTC DateTime 读回偏移——README 已知限制；
        /// 生产 PG/SQL Server 无偏移）。仅允许「无偏移」或「整本机时区偏移」两种结果，不放宽到任意时刻。</summary>
        private static void AssertUtcMomentRoughEqual(DateTime expectedUtc, DateTime? actual, string label = "ValidUntil")
        {
            Assert.NotNull(actual);
            var actualUtc = actual.Value.Kind == DateTimeKind.Utc ? actual.Value : DateTime.SpecifyKind(actual.Value, DateTimeKind.Utc);
            var deltaMinutes = Math.Abs((actualUtc - expectedUtc).TotalMinutes);
            var offsetMinutes = Math.Abs(TimeZoneInfo.Local.BaseUtcOffset.TotalMinutes);
            var matchesNoOffset = deltaMinutes <= 1;
            var matchesOffset = Math.Abs(deltaMinutes - offsetMinutes) <= 1;
            Assert.True(matchesNoOffset || matchesOffset, $"{label} 时刻不符：期望 {expectedUtc:O} 读回 {actual:O}");
        }
    private static (AuthGrantCommandService Command, AuthGrantQueryService Query, AuthGrantEntityDataService Ds) CreateServices()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var command = new AuthGrantCommandService(stub);
        var query = new AuthGrantQueryService(stub, new FakeTokenVerificationContext("u-100"));
        return (command, query, stub.Use<AuthGrantEntityDataService>());
    }

    /// <summary>文件模式 SQLite 独立实例（多连接共享同一库文件——并发测试用；:memory: 单连接池并发争用先例对齐 FileManagement）。</summary>
    private static (StubDomainUser Stub, AuthGrantCommandService Command) CreateFileService(string dbPath)
    {
        var fsql = new FreeSqlBuilder()
            .UseConnectionString(DataType.Sqlite, $"Data Source={dbPath}")
            .UseAutoSyncStructure(true)
            .Build();
        AuthenticationTestHost.SyncSchema(fsql);
        var stub = AuthenticationTestHost.CreateStub(fsql);
        return (stub, new AuthGrantCommandService(stub));
    }

    [Fact]
    public async Task RecordLoginGrant_CreatesActive()
    {
        var (command, query, _) = CreateServices();

        await command.RecordLoginGrantAsync("u-100", "app-1", CancellationToken.None);

        // B3b：门面返回 GrantsResponse（Grants 列表）；userId 读 ambient（FakeTokenVerificationContext("u-100")）——无 userId 参数
        var grants = await query.GetGrantsAsync(ct: TestContext.Current.CancellationToken);
        var grant = Assert.Single(grants.Grants);
        Assert.Equal("app-1", grant.AppId);
        Assert.Equal(0, grant.Status);                    // Active
        Assert.Equal(AuthGrantSources.Login, grant.Source);
        Assert.Empty(grant.Scopes);                        // login 源 v0.8.0 空串 → 拆分空列表
        Assert.Null(grant.ValidUntil);                    // 应用授权有效期 null = 持续至吊销
    }

    [Fact]
    public async Task RecordLoginGrant_SameKey_UpsertsSingle()
    {
        var (command, query, ds) = CreateServices();

        await command.RecordLoginGrantAsync("u-100", "app-1", CancellationToken.None);
        await command.RecordLoginGrantAsync("u-100", "app-1", CancellationToken.None);

        // upsert 幂等：同一复合键（UserId+AppId+Source=Login）两次调用 → 仅 1 行（存在刷新 Active 态）
        var grants = await query.GetGrantsAsync(ct: TestContext.Current.CancellationToken);
        Assert.Single(grants.Grants);
        Assert.Equal(0, grants.Grants[0].Status);

        // 明细：仅 1 行（DataService 直查兜底断言）
        var all = await ds.GetGrantsAsync("u-100", null, CancellationToken.None);
        Assert.Single(all);
    }

    [Fact]
    public async Task RecordLoginGrant_ConcurrentSameKey_OnlyOneRow()
    {
        // Oracle P0-2：唯一约束 UX_AuthGrant_User_App_Source 兜底 TOCTOU——两个并发 insert 第二个撞唯一冲突
        // → 写入门面 catch → 重查转 update（败者不抛、幂等收敛 1 行）
        // 文件模式 SQLite（多连接共享——两个独立 FreeSql 实例真实并发写；:memory: 单连接池会串行化掩盖竞态）
        var dbPath = Path.Combine(Path.GetTempPath(), "tkwf-auth-grant-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var (stub1, cmd1) = CreateFileService(dbPath);
            var (_, cmd2) = CreateFileService(dbPath);

            await Task.WhenAll(
                cmd1.RecordLoginGrantAsync("u-100", "app-1", CancellationToken.None),
                cmd2.RecordLoginGrantAsync("u-100", "app-1", CancellationToken.None));

            // 经任一连接查询断言仅 1 行（唯一索引兜底生效）
            var query = new AuthGrantQueryService(stub1, new FakeTokenVerificationContext("u-100"));
            var grants = await query.GetGrantsAsync(ct: TestContext.Current.CancellationToken);
            var grant = Assert.Single(grants.Grants);
            Assert.Equal(0, grant.Status);
            Assert.Equal(AuthGrantSources.Login, grant.Source);
            Assert.Equal("app-1", grant.AppId);
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

    [Fact]
    public async Task RecordLoginGrant_DifferentApp_IndependentRows()
    {
        var (command, query, _) = CreateServices();

        await command.RecordLoginGrantAsync("u-100", "app-1", CancellationToken.None);
        await command.RecordLoginGrantAsync("u-100", "app-2", CancellationToken.None);

        // 不同 AppId = 不同复合键 → 各自独立授权行
        var grants = await query.GetGrantsAsync(ct: TestContext.Current.CancellationToken);
        Assert.Equal(2, grants.Grants.Count);
        Assert.Equal(2, grants.Grants.Select(g => g.AppId).Distinct().Count());
    }

    [Fact]
    public async Task RecordGrantAsync_RedeemSource_CreatesWithValidUntil()
    {
        // T-7：泛化写路径——redeem 源创建行携带传入 validUntilUtc（login 源恒 null 之外的新语义）
        var (command, _, ds) = CreateServices();

        await command.RecordGrantAsync("u-100", "app-1", AuthGrantSources.Redeem, UtcA, CancellationToken.None);

        var all = await ds.GetGrantsAsync("u-100", null, CancellationToken.None);
        var grant = Assert.Single(all);
        Assert.Equal(AuthGrantSources.Redeem, grant.Source);
        AssertUtcMomentRoughEqual(UtcA, grant.ValidUntil);
        Assert.Equal(0, grant.Status);
    }

    [Fact]
    public async Task RecordGrantAsync_ExistingRow_OverwritesValidUntil()
    {
        // T-7：冲突（upsert 命中既有行）路径——以新 validUntil 覆写（RefreshActiveWithValidUntilAsync 生效实证，
        // 现 RefreshActiveAsync 不动 ValidUntil 缺口的回归护栏）
        var (command, _, ds) = CreateServices();

        await command.RecordGrantAsync("u-100", "app-1", AuthGrantSources.Redeem, UtcA, CancellationToken.None);
        await command.RecordGrantAsync("u-100", "app-1", AuthGrantSources.Redeem, UtcB, CancellationToken.None);

var all = await ds.GetGrantsAsync("u-100", null, CancellationToken.None);
        var grant = Assert.Single(all);
        AssertUtcMomentRoughEqual(UtcB, grant.ValidUntil);   // 第二次调用覆写（last-writer-wins）
        Assert.Equal(0, grant.Status);          // 覆写后仍 Active
    }

    [Fact]
    public async Task RecordLoginGrant_AfterDelegation_ValidUntilNull()
    {
        // T-7 向后兼容：RecordLoginGrantAsync 委托 RecordGrantAsync(Login, null)——login 行 ValidUntil 恒 null，
        // 覆写 null 无损（既有行 + 幂等重入双路径均断言）
        var (command, _, ds) = CreateServices();

        await command.RecordLoginGrantAsync("u-100", "app-1", CancellationToken.None);
        await command.RecordLoginGrantAsync("u-100", "app-1", CancellationToken.None);   // 幂等重入（既有行覆写 null）

        var all = await ds.GetGrantsAsync("u-100", null, CancellationToken.None);
        var grant = Assert.Single(all);
        Assert.Null(grant.ValidUntil);
        Assert.Equal(0, grant.Status);
    }

    [Fact]
    public async Task RecordGrantAsync_ConcurrentSameKey_OnlyOneRow_ValidUntilOverwritten()
    {
        // T-7 并发唯一冲突兜底转 update 且覆写：两个并发 redeem 写同一复合键不同 validUntil——
        // 败者撞唯一约束 → catch → 重查 → RefreshActiveWithValidUntilAsync 以各自 validUntil 覆写；
        // 终态 1 行 + ValidUntil = 后写者 B（先胜者落 A，败者 update 覆写 → B 恒为终值）
        var dbPath = Path.Combine(Path.GetTempPath(), "tkwf-auth-grant-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            var (stub1, cmd1) = CreateFileService(dbPath);
            var (_, cmd2) = CreateFileService(dbPath);

            await Task.WhenAll(
                cmd1.RecordGrantAsync("u-100", "app-1", AuthGrantSources.Redeem, UtcA, CancellationToken.None),
                cmd2.RecordGrantAsync("u-100", "app-1", AuthGrantSources.Redeem, UtcB, CancellationToken.None));

            // 经任一连接查询断言仅 1 行 + ValidUntil 已被后写者覆写（catch→update 路径生效实证）
            var query = new AuthGrantQueryService(stub1, new FakeTokenVerificationContext("u-100"));
            var grants = await query.GetGrantsAsync(ct: TestContext.Current.CancellationToken);
            var grant = Assert.Single(grants.Grants);
            Assert.Equal(0, grant.Status);
            AssertUtcMomentRoughEqual(UtcB, grant.ValidUntil);   // SQLite 往返丢 Kind——按 UTC 时刻比对
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
}
