using System.Security.Authentication;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.AuthSurface.Tests;

/// <summary>
/// 批次管理门面测试（v0.3.0 T-0~T-3）——生产路径（真实 DI + FreeSql SQLite + FreeSqlTransactionManager 物理事务 +
/// BindScope + user.Use&lt;ICodeBatchService&gt;() AOP 守卫工厂）。
/// <para>覆盖：创建校验 / 生成回调（明文一次性 + 失败回滚 DRAFT + CAS 抢占）/ 删除 / 列表统计时间派生口径 /
/// 明细分页 / 整批与单码吊销（状态机联动 + 幂等）。</para>
/// </summary>
public class CodeBatchServiceTests
{
    private static ServiceProvider CreateProvider(IFreeSql fsql, Action<IServiceCollection>? configure = null)
        => AuthSurfaceTestHost.CreateProvider(fsql, configure);

    private static DomainUser<TestUserInfo> BindUser(ServiceProvider sp, string userId = "u-1001")
    {
        DomainUser<TestUserInfo>.BindScope(sp);
        return new DomainUser<TestUserInfo> { UserInfo = new TestUserInfo(userId, "测试用户") };
    }

    private static CreateCodeBatchRequest NewRequest(int quantity = 5, string? channel = null, bool test = false)
        => new(
            Name: "暑期30天卡",
            ProductName: "英语陪练",
            TargetAppId: "peilian",
            ValidityDays: 30,
            ExpireAtUtc: DateTime.UtcNow.AddDays(30),
            Channel: channel,
            Quantity: quantity,
            Test: test,
            CreatedBy: "admin-1");

    // ── T-0 创建 ──

    [Fact]
    public async Task CreateBatch_Valid_SavesDraft_WithBatchNoFormat()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);

        var dto = await user.Use<ICodeBatchService>().CreateBatchAsync(NewRequest(5), CancellationToken.None);

        Assert.StartsWith("B", dto.BatchNo);
        Assert.Equal(13, dto.BatchNo.Length);                 // B + yyyyMMdd(8) + 4 随机
        Assert.Equal(0, dto.Status);                          // DRAFT
        Assert.Equal(0, dto.GeneratedCount);
        Assert.Equal(5, dto.Quantity);
        Assert.Equal("admin-1", dto.CreatedBy);
        Assert.True(dto.Id > 0);
    }

    [Fact]
    public async Task CreateBatch_QuantityExceedsOption_ThrowsBatchQuantityExceeded()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql, s => s.AddOptions<AuthSurfaceOptions>().Configure(o => o.MaxBatchQuantity = 10));
        var user = BindUser(sp);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => user.Use<ICodeBatchService>().CreateBatchAsync(NewRequest(11), CancellationToken.None));
        Assert.Equal(BatchErrorCodes.BatchQuantityExceeded, ex.Message);
    }

    [Fact]
    public async Task CreateBatch_PastExpireAt_Throws()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);

        var request = NewRequest() with { ExpireAtUtc = DateTime.UtcNow.AddDays(-1) };
        await Assert.ThrowsAsync<ArgumentException>(
            () => user.Use<ICodeBatchService>().CreateBatchAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task CreateBatch_NonPositiveValidityDays_Throws()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);

        var request = NewRequest() with { ValidityDays = 0 };
        await Assert.ThrowsAsync<ArgumentException>(
            () => user.Use<ICodeBatchService>().CreateBatchAsync(request, CancellationToken.None));
    }

    // ── T-0 生成（回调主方案 + 事务回滚） ──

    [Fact]
    public async Task GenerateBatch_StreamsPlainCodes_AndMarksGenerated()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var facade = user.Use<ICodeBatchService>();
        var batch = await facade.CreateBatchAsync(NewRequest(5), CancellationToken.None);

        var plains = new List<string>();
        await facade.GenerateBatchAsync(batch.Id, (plain, _) => { plains.Add(plain); return Task.CompletedTask; }, CancellationToken.None);

        Assert.Equal(5, plains.Count);
        Assert.All(plains, p => Assert.StartsWith("EDU-", p));
        Assert.Equal(5, plains.Distinct().Count());

        var head = await fsql.Select<CodeBatchEntity>().Where(e => e.Id == batch.Id).ToOneAsync();
        Assert.Equal(2, head!.Status);                        // GENERATED
        Assert.Equal(5, head.GeneratedCount);
        Assert.NotNull(head.ExportedAtUtc);

        // 码行回填 BatchId + 继承头字段；库中仅哈希/脱敏
        var rows = await fsql.Select<RedemptionCodeEntity>().Where(e => e.BatchId == batch.Id).ToListAsync();
        Assert.Equal(5, rows.Count);
        Assert.All(rows, r =>
        {
            Assert.Equal("英语陪练", r.ProductName);
            Assert.Equal("peilian", r.TargetAppId);
            Assert.Equal(0, r.Status);
            Assert.NotNull(r.ExpireAtUtc);
            Assert.Contains('*', r.CodeMasked);
            Assert.DoesNotContain(plains, p => p == r.CodeHash);
        });
    }

    [Fact]
    public async Task GenerateBatch_CallbackThrows_RollsBackStructure()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var tm = sp.GetRequiredService<ITransactionManager>();
        var recording = Assert.IsType<RecordingTransactionManager>(tm);
        var facade = user.Use<ICodeBatchService>();
        var batch = await facade.CreateBatchAsync(NewRequest(5), CancellationToken.None);

        var delivered = new List<string>();
        await Assert.ThrowsAsync<InvalidOperationException>(() => facade.GenerateBatchAsync(
            batch.Id,
            (plain, _) =>
            {
                delivered.Add(plain);
                if (delivered.Count == 3) throw new InvalidOperationException("消费方写入失败");
                return Task.CompletedTask;
            },
            CancellationToken.None));

        // 事务结构钉子：进入事务但不提交 → 未 Commit 释放计入 Rollback（真实物理原子性归 PostgreSQL——
        // docs/测试约定-SQLite物理事务与Noop限制.md §2.2；SQLite 上不断言码行是否真回滚）
        Assert.True(recording.BeginCount >= 1);
        Assert.Equal(0, recording.CommitCount);
        Assert.True(recording.RollbackCount >= 1);
        Assert.Equal(3, delivered.Count);                     // 已流出明文 = 作废（B1 §4.4 失败边语义）
    }

    [Fact]
    public async Task GenerateBatch_Cancelled_RollsBackStructure()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var recording = Assert.IsType<RecordingTransactionManager>(sp.GetRequiredService<ITransactionManager>());
        var facade = user.Use<ICodeBatchService>();
        var batch = await facade.CreateBatchAsync(NewRequest(5), CancellationToken.None);

        using var cts = new CancellationTokenSource();
        var seen = 0;
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => facade.GenerateBatchAsync(
            batch.Id,
            (_, _) => { if (++seen == 2) cts.Cancel(); return Task.CompletedTask; },
            cts.Token));

        Assert.Equal(0, recording.CommitCount);
        Assert.True(recording.RollbackCount >= 1);
    }

    [Fact]
    public async Task GenerateBatch_SecondCall_CasLoser_ThrowsBatchNotEditable()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var facade = user.Use<ICodeBatchService>();
        var batch = await facade.CreateBatchAsync(NewRequest(2), CancellationToken.None);

        await facade.GenerateBatchAsync(batch.Id, (_, _) => Task.CompletedTask, CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => facade.GenerateBatchAsync(batch.Id, (_, _) => Task.CompletedTask, CancellationToken.None));
        Assert.Equal(BatchErrorCodes.BatchNotEditable, ex.Message);
    }

    [Fact]
    public async Task GenerateBatch_BatchNotFound_Throws()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => user.Use<ICodeBatchService>().GenerateBatchAsync(999_999, (_, _) => Task.CompletedTask, CancellationToken.None));
        Assert.Equal(BatchErrorCodes.BatchNotFound, ex.Message);
    }

    // ── T-0 删除 ──

    [Fact]
    public async Task DeleteBatch_Draft_Succeeds_GeneratedRejects()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var facade = user.Use<ICodeBatchService>();

        var draft = await facade.CreateBatchAsync(NewRequest(1), CancellationToken.None);
        await facade.DeleteBatchAsync(draft.Id, CancellationToken.None);
        Assert.Null(await fsql.Select<CodeBatchEntity>().Where(e => e.Id == draft.Id).ToOneAsync());

        var generated = await facade.CreateBatchAsync(NewRequest(1), CancellationToken.None);
        await facade.GenerateBatchAsync(generated.Id, (_, _) => Task.CompletedTask, CancellationToken.None);
        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => facade.DeleteBatchAsync(generated.Id, CancellationToken.None));
        Assert.Equal(BatchErrorCodes.BatchNotEditable, ex.Message);
    }

    // ── T-2 统计口径 / 分页 ──

    [Fact]
    public async Task GetBatches_Stats_TimeDerivedWithInvariant()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var facade = user.Use<ICodeBatchService>();

        var drafts = await SeedBatchWithCodesAsync(fsql, facade, "official", test: false);
        // 5 码：2 已兑 / 2 过期未翻转（status=0 + 过去 expire）/ 1 吊销
        var dto = (await facade.GetBatchAsync(drafts, CancellationToken.None)).Batch;

        Assert.Equal(2, dto.RedeemedCount);
        Assert.Equal(0, dto.PendingCount);
        Assert.Equal(2, dto.ExpiredCount);
        Assert.Equal(1, dto.RevokedCount);
        Assert.Equal(dto.GeneratedCount, dto.RedeemedCount + dto.PendingCount + dto.ExpiredCount + dto.RevokedCount);

        var page = await facade.GetBatchesAsync(new CodeBatchFilter(Channel: "official"), 1, 20, CancellationToken.None);
        Assert.Equal(1, page.Total);
        Assert.Single(page.Items);

        var filtered = await facade.GetBatchesAsync(new CodeBatchFilter(NameKeyword: "不存在"), 1, 20, CancellationToken.None);
        Assert.Equal(0, filtered.Total);
        Assert.Empty(filtered.Items);
    }

    [Fact]
    public async Task GetBatches_Stats_FlippedExpiredCountsAsExpired_InvariantHolds()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var facade = user.Use<ICodeBatchService>();

        var batch = await facade.CreateBatchAsync(NewRequest(3), CancellationToken.None);
        await facade.GenerateBatchAsync(batch.Id, (_, _) => Task.CompletedTask, CancellationToken.None);
        var rows = await fsql.Select<RedemptionCodeEntity>().Where(e => e.BatchId == batch.Id).OrderBy(e => e.Id).ToListAsync();

        // 1 已翻转过期态（status=2，MarkExpiredAsync 惰性翻转路径）+ 1 时间派生过期（status=0 且过去）+ 1 待兑
        await fsql.Update<RedemptionCodeEntity>().Where(e => e.Id == rows[0].Id)
            .Set(e => e.Status, 2).Set(e => e.ExpireAtUtc, DateTime.UtcNow.AddDays(-1)).ExecuteAffrowsAsync();
        await fsql.Update<RedemptionCodeEntity>().Where(e => e.Id == rows[1].Id)
            .Set(e => e.ExpireAtUtc, DateTime.UtcNow.AddDays(-1)).ExecuteAffrowsAsync();

        var dto = (await facade.GetBatchAsync(batch.Id, CancellationToken.None)).Batch;

        Assert.Equal(2, dto.ExpiredCount);                    // status=2 已翻转 + status=0 时间派生 同桶
        Assert.Equal(1, dto.PendingCount);
        Assert.Equal(0, dto.RedeemedCount);
        Assert.Equal(0, dto.RevokedCount);
        Assert.Equal(dto.GeneratedCount, dto.RedeemedCount + dto.PendingCount + dto.ExpiredCount + dto.RevokedCount);
    }

    [Fact]
    public async Task GetBatchCodes_PagingAndStatusFilter_NoPlaintext()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var facade = user.Use<ICodeBatchService>();
        var batchId = await SeedBatchWithCodesAsync(fsql, facade, channel: null, test: false);

        var page1 = await facade.GetBatchCodesAsync(batchId, 1, 2, null, CancellationToken.None);
        Assert.Equal(5, page1.Total);
        Assert.Equal(2, page1.Items.Count);
        Assert.All(page1.Items, r => Assert.Contains('*', r.CodeMasked));

        var pending = await facade.GetBatchCodesAsync(batchId, 1, 20, 0, CancellationToken.None);
        Assert.Equal(2, pending.Total);                        // 2 过期未翻转（status=0）

        var beyond = await facade.GetBatchCodesAsync(batchId, 99, 20, null, CancellationToken.None);
        Assert.Empty(beyond.Items);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => facade.GetBatchCodesAsync(999_999, 1, 20, null, CancellationToken.None));
        Assert.Equal(BatchErrorCodes.BatchNotFound, ex.Message);
    }

    // ── T-3 吊销（状态机联动 + 幂等） ──

    [Fact]
    public async Task RevokeBatch_Generated_FlipsPendingOnly_AndMarksRevoked()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var facade = user.Use<ICodeBatchService>();
        var batchId = await SeedBatchWithCodesAsync(fsql, facade, channel: null, test: false);

        await facade.RevokeBatchAsync(batchId, CancellationToken.None);

        var head = await fsql.Select<CodeBatchEntity>().Where(e => e.Id == batchId).ToOneAsync();
        Assert.Equal(3, head!.Status);                          // REVOKED

        var byStatus = await fsql.Select<RedemptionCodeEntity>().Where(e => e.BatchId == batchId).ToListAsync();
        Assert.Equal(2, byStatus.Count(r => r.Status == 1));    // 已兑不动
        Assert.Equal(3, byStatus.Count(r => r.Status == 3));    // 原 2 过期未翻转 + 原 1 吊销 → 全 3（吞没语义）
        Assert.Equal(0, byStatus.Count(r => r.Status == 0));

        // REVOKED 幂等（不抛）
        await facade.RevokeBatchAsync(batchId, CancellationToken.None);
    }

    [Fact]
    public async Task RevokeBatch_Draft_ThrowsBatchNotEditable()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var facade = user.Use<ICodeBatchService>();
        var batch = await facade.CreateBatchAsync(NewRequest(2), CancellationToken.None);

        var ex = await Assert.ThrowsAsync<AuthenticationException>(
            () => facade.RevokeBatchAsync(batch.Id, CancellationToken.None));
        Assert.Equal(BatchErrorCodes.BatchNotEditable, ex.Message);
    }

    [Fact]
    public async Task RevokeCode_Single_IdempotentAndAutoRevokesBatchOnLastPending()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var facade = user.Use<ICodeBatchService>();

        var batch = await facade.CreateBatchAsync(NewRequest(2), CancellationToken.None);
        await facade.GenerateBatchAsync(batch.Id, (_, _) => Task.CompletedTask, CancellationToken.None);
        var rows = await fsql.Select<RedemptionCodeEntity>().Where(e => e.BatchId == batch.Id).OrderBy(e => e.Id).ToListAsync();

        // 第一张：翻成功 true；剩余 1 张未兑 → 批次不升 REVOKED
        Assert.True(await facade.RevokeCodeAsync(rows[0].Id, CancellationToken.None));
        var head = await fsql.Select<CodeBatchEntity>().Where(e => e.Id == batch.Id).ToOneAsync();
        Assert.Equal(2, head!.Status);                          // 仍 GENERATED（派生 PARTIAL）
        var detail = await facade.GetBatchAsync(batch.Id, CancellationToken.None);
        Assert.True(detail.PartialRevoked);

        // 重复吊销同一码 → false 幂等不抛
        Assert.False(await facade.RevokeCodeAsync(rows[0].Id, CancellationToken.None));

        // 第二张（最后一张未兑）→ 批次升 REVOKED
        Assert.True(await facade.RevokeCodeAsync(rows[1].Id, CancellationToken.None));
        head = await fsql.Select<CodeBatchEntity>().Where(e => e.Id == batch.Id).ToOneAsync();
        Assert.Equal(3, head!.Status);

        // 已兑码吊销 → false（0 行更新）
        await AuthSurfaceTestHost.SeedAccountAsync(fsql, "u-2002");
        var plain = await user.Use<IRedemptionCommandService>().CreateCodeAsync("手工码", "peilian", ct: CancellationToken.None);
        var manualId = (await fsql.Select<RedemptionCodeEntity>()
            .Where(e => e.CodeHash == RedemptionCodeGenerator.Hash(plain)).ToOneAsync())!.Id;
        var redeemed = await user.Use<IRedemptionCommandService>().RedeemAsync("u-2002", plain, CancellationToken.None);
        Assert.NotNull(redeemed);
        Assert.False(await facade.RevokeCodeAsync(manualId, CancellationToken.None));
    }

    [Fact]
    public async Task GetBatches_Stats_SingleGroupBySql_NotNPlusOne()
    {
        using var fsql = AuthSurfaceTestHost.CreateInMemoryFreeSql();
        using var sp = CreateProvider(fsql);
        var user = BindUser(sp);
        var facade = user.Use<ICodeBatchService>();
        _ = await SeedBatchWithCodesAsync(fsql, facade, channel: "official", test: false);

        var sqlLog = new List<string>();
        fsql.Aop.CommandBefore += (_, e) => sqlLog.Add(e.Command.CommandText);

        var page = await facade.GetBatchesAsync(new CodeBatchFilter(Channel: "official"), 1, 20, TestContext.Current.CancellationToken);
        Assert.Single(page.Items);
        Assert.Equal(5, page.Items[0].GeneratedCount);

        var statSql = sqlLog.Where(s => s.Contains("GROUP BY", StringComparison.OrdinalIgnoreCase)).ToList();
        Assert.Single(statSql);                                  // 单查询分组（非 N+1 逐批拉全量）

        var dto = page.Items[0];
        Assert.Equal(dto.GeneratedCount, dto.RedeemedCount + dto.PendingCount + dto.ExpiredCount + dto.RevokedCount);
    }

    /// <summary>种子批次 + 5 码（2 已兑 / 2 过期未翻转 / 1 吊销）——经门面生成后直改码行状态（造数）。</summary>
    private static async Task<long> SeedBatchWithCodesAsync(IFreeSql fsql, ICodeBatchService facade, string? channel, bool test)
    {
        var batch = await facade.CreateBatchAsync(NewRequest(5, channel, test), CancellationToken.None);
        await facade.GenerateBatchAsync(batch.Id, (_, _) => Task.CompletedTask, CancellationToken.None);

        var rows = await fsql.Select<RedemptionCodeEntity>().Where(e => e.BatchId == batch.Id).OrderBy(e => e.Id).ToListAsync();
        var past = DateTime.UtcNow.AddDays(-1);
        await fsql.Update<RedemptionCodeEntity>()
            .Where(e => e.Id == rows[0].Id || e.Id == rows[1].Id)
            .Set(e => e.Status, 1).Set(e => e.RedeemedAtUtc, DateTime.UtcNow).Set(e => e.RedeemedByUId, "u-1001")
            .ExecuteAffrowsAsync();                                    // 2 已兑
        await fsql.Update<RedemptionCodeEntity>()
            .Where(e => e.Id == rows[2].Id || e.Id == rows[3].Id)
            .Set(e => e.ExpireAtUtc, past).ExecuteAffrowsAsync();       // 2 过期未翻转（status 仍 0）
        await fsql.Update<RedemptionCodeEntity>()
            .Where(e => e.Id == rows[4].Id)
            .Set(e => e.Status, 3).ExecuteAffrowsAsync();               // 1 吊销
        return batch.Id;
    }
}
