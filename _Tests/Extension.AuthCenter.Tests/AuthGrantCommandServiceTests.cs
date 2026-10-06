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
/// DataService 经 User.Use&lt;具体类&gt;() NoAop 直建——零 IFreeSql/IEntityDAC 直注入。</para></summary>
public class AuthGrantCommandServiceTests
{
    private static (AuthGrantCommandService Command, AuthGrantQueryService Query, AuthGrantEntityDataService Ds) CreateServices()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var command = new AuthGrantCommandService(stub);
        var query = new AuthGrantQueryService(stub);
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

        var grants = await query.GetGrantsAsync("u-100");
        var grant = Assert.Single(grants);
        Assert.Equal("u-100", grant.UserId);
        Assert.Equal("app-1", grant.AppId);
        Assert.Equal(0, grant.Status);                    // Active
        Assert.Equal(AuthGrantSources.Login, grant.Source);
        Assert.Equal("", grant.Scopes);                   // login 源 v0.8.0 空串
        Assert.Null(grant.ValidUntil);                    // 应用授权有效期 null = 持续至吊销
    }

    [Fact]
    public async Task RecordLoginGrant_SameKey_UpsertsSingle()
    {
        var (command, query, ds) = CreateServices();

        await command.RecordLoginGrantAsync("u-100", "app-1", CancellationToken.None);
        await command.RecordLoginGrantAsync("u-100", "app-1", CancellationToken.None);

        // upsert 幂等：同一复合键（UserId+AppId+Source=Login）两次调用 → 仅 1 行（存在刷新 Active 态）
        var grants = await query.GetGrantsAsync("u-100");
        Assert.Single(grants);
        Assert.Equal(0, grants[0].Status);

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
            var query = new AuthGrantQueryService(stub1);
            var grants = await query.GetGrantsAsync("u-100");
            var grant = Assert.Single(grants);
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
        var grants = await query.GetGrantsAsync("u-100");
        Assert.Equal(2, grants.Count);
        Assert.Equal(2, grants.Select(g => g.AppId).Distinct().Count());
    }
}
