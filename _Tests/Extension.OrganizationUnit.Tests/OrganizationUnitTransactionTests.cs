using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.OrganizationUnit;

namespace TKWF.Ext.OrganizationUnit.Tests;

/// <summary>
/// D17 事务结构测试（P1 审核建议）——记录型 Fake ITransactionManager 机械验证
/// Create/Move/Delete 写路径确实进入 BeginAsync → CommitAsync / 失败 RollbackAsync。
/// <para>对齐方案 D17「代码审查 + 单测」：Noop TM 下行为测试无法证明事务存在，
/// 本文件为事务结构提供回归钉子——未来重构移除事务包裹立即可被捕获。</para>
/// </summary>
public class OrganizationUnitTransactionTests
{
    [Fact]
    public async Task CreateAsync_Success_CommitsExactlyOnce()
    {
        using var host = NewHostWithRecording(out var tm);

        await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(1, tm.CommitCount);
        Assert.Equal(0, tm.RollbackCount);
    }

    [Fact]
    public async Task MoveAsync_Success_CommitsExactlyOnce()
    {
        using var host = NewHostWithRecording(out var tm);
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        var b = await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None);
        tm.Reset();

        await host.Manager.MoveAsync(b.Id, null, CancellationToken.None);

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(1, tm.CommitCount);
        Assert.Equal(0, tm.RollbackCount);
    }

    [Fact]
    public async Task DeleteAsync_BlockedByChildren_RollsBack()
    {
        using var host = NewHostWithRecording(out var tm);
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None);
        tm.Reset();

        // 删除保护拒绝 → 事务回滚（业务异常经 Rollback 路径自然传播）
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.DeleteAsync(a.Id, CancellationToken.None));

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(0, tm.CommitCount);
        Assert.Equal(1, tm.RollbackCount);
    }

    [Fact]
    public async Task MoveAsync_CommitFailure_PropagatesAndRollsBack()
    {
        using var host = NewHostWithRecording(out var tm);
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        var b = await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None);
        tm.Reset();
        tm.FailCommit = true;

        // 提交阶段注入失败 → 异常自然传播 + Rollback 调用（子树无半更新残留由行为测试覆盖）
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.MoveAsync(b.Id, null, CancellationToken.None));

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(0, tm.CommitCount);
        Assert.Equal(1, tm.RollbackCount);
    }

    private static OrganizationUnitTestHost NewHostWithRecording(out RecordingTransactionManager tm)
    {
        var recording = new RecordingTransactionManager();
        tm = recording;
        return OrganizationUnitTestHost.Create(services =>
        {
            services.RemoveAll<ITransactionManager>();
            services.AddSingleton<ITransactionManager>(recording);
        });
    }
}

/// <summary>记录型 Fake 事务管理器——统计 Begin/Commit/Rollback 次数，可注入提交失败。</summary>
internal sealed class RecordingTransactionManager : ITransactionManager
{
    public int BeginCount;
    public int CommitCount;
    public int RollbackCount;

    /// <summary>为 true 时 CommitAsync 抛 InvalidOperationException（模拟提交失败）。</summary>
    public bool FailCommit;

    public bool IsActive => false;

    public void Reset()
    {
        BeginCount = 0;
        CommitCount = 0;
        RollbackCount = 0;
    }

    public ITransactionScope Begin(IsolationLevel isolationLevel = IsolationLevel.Serializable)
        => CreateScope();

    public Task<ITransactionScope> BeginAsync(
        IsolationLevel isolationLevel = IsolationLevel.Serializable,
        CancellationToken ct = default)
        => Task.FromResult(CreateScope());

    private ITransactionScope CreateScope()
    {
        BeginCount++;
        return new RecordingTransactionScope(this);
    }
}

/// <summary>记录型事务作用域——Commit 计数/可注入失败；Rollback 空操作（Manager catch 路径可重复调用）。</summary>
internal sealed class RecordingTransactionScope : ITransactionScope
{
    private readonly RecordingTransactionManager _tm;

    public RecordingTransactionScope(RecordingTransactionManager tm) => _tm = tm;

    public bool IsActive => true;

    public void Dispose() { }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    public Task CommitAsync(CancellationToken ct = default)
    {
        if (_tm.FailCommit)
            throw new InvalidOperationException("模拟提交失败");
        _tm.CommitCount++;
        return Task.CompletedTask;
    }

    public Task RollbackAsync(CancellationToken ct = default)
    {
        _tm.RollbackCount++;
        return Task.CompletedTask;
    }

    public void Commit() { }

    public void Rollback() { }
}
