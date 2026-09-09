using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.Calendar;

namespace TKWF.Ext.Calendar.Tests;

/// <summary>
/// D20 事务结构测试（对齐 OrganizationUnitTransactionTests 模式）——记录型 Fake ITransactionManager
/// 机械验证 Calendar 写路径确实进入 BeginAsync → CommitAsync / 失败 RollbackAsync。
/// <para>Noop TM 下行为测试无法证明事务存在——本文件为事务包裹提供回归钉子。</para>
/// </summary>
public class CalendarTransactionTests
{
    private static DateTime Utc(int year, int month, int day, int hour = 0, int minute = 0)
        => new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);

    [Fact]
    public async Task CreateCalendar_Success_CommitsExactlyOnce()
    {
        using var host = NewHostWithRecording(out var tm);

        await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(1, tm.CommitCount);
        Assert.Equal(0, tm.RollbackCount);
    }

    [Fact]
    public async Task CreateEvent_Success_CommitsExactlyOnce()
    {
        using var host = NewHostWithRecording(out var tm);
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        tm.Reset();

        await host.Manager.CreateEventAsync(cal.Id, "E", Utc(2026, 9, 9, 9), ct: CancellationToken.None);

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(1, tm.CommitCount);
        Assert.Equal(0, tm.RollbackCount);
    }

    [Fact]
    public async Task DeleteCalendar_BlockedByEvents_RollsBack()
    {
        using var host = NewHostWithRecording(out var tm);
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        await host.Manager.CreateEventAsync(cal.Id, "E", Utc(2026, 9, 9, 9), ct: CancellationToken.None);
        tm.Reset();

        // 删除保护拒绝 → 业务异常经 Rollback 路径自然传播
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.DeleteCalendarAsync(cal.Id, CancellationToken.None));

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(0, tm.CommitCount);
        Assert.Equal(1, tm.RollbackCount);
    }

    [Fact]
    public async Task CreateEvent_CommitFailure_PropagatesAndRollsBack()
    {
        using var host = NewHostWithRecording(out var tm);
        var cal = await host.Manager.CreateCalendarAsync("C", "Cal", ct: CancellationToken.None);
        tm.Reset();
        tm.FailCommit = true;

        // 提交阶段注入失败 → 异常自然传播 + Rollback 调用
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.CreateEventAsync(cal.Id, "E", Utc(2026, 9, 9, 9), ct: CancellationToken.None));

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(0, tm.CommitCount);
        Assert.Equal(1, tm.RollbackCount);
    }

    private static CalendarTestHost NewHostWithRecording(out RecordingTransactionManager tm)
    {
        var recording = new RecordingTransactionManager();
        tm = recording;
        return CalendarTestHost.Create(services =>
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
