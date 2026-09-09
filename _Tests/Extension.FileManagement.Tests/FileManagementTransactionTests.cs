using System;
using System.Data;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain.Transactions;
using TKWF.Ext.FileManagement;

namespace TKWF.Ext.FileManagement.Tests;

/// <summary>
/// D19 事务记录型测试（对齐 CalendarTransactionTests/OrganizationUnitTransactionTests 模式）——
/// 记录型 Fake ITransactionManager 机械验证 FileManagement 写路径确实进入 BeginAsync → CommitAsync /
/// 失败 RollbackAsync。
/// <para>Noop TM 下行为测试无法证明事务存在——本文件为事务包裹提供回归钉子。</para>
/// </summary>
public class FileManagementTransactionTests
{
    private static readonly byte[] SampleBytes = Encoding.UTF8.GetBytes("transaction-test-content");

    [Fact]
    public async Task CreateFolder_Success_CommitsExactlyOnce()
    {
        using var host = NewHostWithRecording(out var tm);

        await host.CreateFolderAsync("docs", "文档");

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(1, tm.CommitCount);
        Assert.Equal(0, tm.RollbackCount);
    }

    [Fact]
    public async Task UploadFile_Success_CommitsExactlyOnce()
    {
        using var host = NewHostWithRecording(out var tm);
        var folder = await host.CreateFolderAsync("docs", "文档");
        tm.Reset();

        await host.UploadFileAsync(folder.Id, "a.txt", SampleBytes);

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(1, tm.CommitCount);
        Assert.Equal(0, tm.RollbackCount);
    }

    [Fact]
    public async Task DeleteFolder_BlockedByFiles_RollsBack()
    {
        using var host = NewHostWithRecording(out var tm);
        var folder = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(folder.Id, "a.txt", SampleBytes);
        tm.Reset();

        // 删除保护拒绝（事务内二次确认）→ 业务异常经 Rollback 路径自然传播
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Manager.DeleteFolderAsync(folder.Id, CancellationToken.None));

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(0, tm.CommitCount);
        Assert.Equal(1, tm.RollbackCount);
    }

    [Fact]
    public async Task DeleteFolder_Empty_Succeeds_CommitsOnce()
    {
        using var host = NewHostWithRecording(out var tm);
        var folder = await host.CreateFolderAsync("docs", "文档");
        tm.Reset();

        await host.Manager.DeleteFolderAsync(folder.Id, CancellationToken.None);

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(1, tm.CommitCount);
        Assert.Equal(0, tm.RollbackCount);
    }

    [Fact]
    public async Task UploadFile_CommitFailure_PropagatesAndRollsBack()
    {
        using var host = NewHostWithRecording(out var tm);
        var folder = await host.CreateFolderAsync("docs", "文档");
        tm.Reset();
        tm.FailCommit = true;

        // 提交阶段注入失败 → 异常自然传播 + Rollback 调用（Blob 补偿同步生效，不额外断言）
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.UploadFileAsync(folder.Id, "a.txt", SampleBytes));

        Assert.Equal(1, tm.BeginCount);
        Assert.Equal(0, tm.CommitCount);
        Assert.Equal(1, tm.RollbackCount);
    }

    private static FileManagementTestHost NewHostWithRecording(out RecordingTransactionManager tm)
    {
        var recording = new RecordingTransactionManager();
        tm = recording;
        return FileManagementTestHost.Create(configure: services =>
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