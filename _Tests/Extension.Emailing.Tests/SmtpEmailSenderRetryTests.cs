using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.Emailing.Tests;

/// <summary>
/// SmtpEmailSender 发送重试测试（V0.2.0）——覆盖配置化指数退避重试 + 取消语义。
/// <para>不真实连接 SMTP，使用 Fake 模式（同 <see cref="SmtpEmailSenderTests"/>）：
/// 连接 127.0.0.1:19999（未监听端口）必然失败，验证重试分支与最终失败记录。</para>
/// </summary>
public class SmtpEmailSenderRetryTests
{
    /// <summary>创建 SmtpEmailSender 实例（使用 Fake 依赖 + 重试配置）。</summary>
    private static (SmtpEmailSender Sender, FakeEmailRecordStore Store, FakeLogger<SmtpEmailSender> Logger) CreateSender(
        int retryCount,
        int retryBaseDelayMilliseconds = 1)
    {
        var store = new FakeEmailRecordStore();
        var logger = new FakeLogger<SmtpEmailSender>();
        var options = Options.Create(new EmailingOptions
        {
            SmtpHost = "127.0.0.1",
            SmtpPort = 19999,
            SmtpUser = "user",
            SmtpPassword = "pass",
            DefaultFrom = "default@example.com",
            IsEnabled = true,
            RetryCount = retryCount,
            RetryBaseDelayMilliseconds = retryBaseDelayMilliseconds
        });
        var sender = new SmtpEmailSender(store, options, logger);
        return (sender, store, logger);
    }

    [Fact]
    public async Task SendAsync_RetryCount2_AllAttemptsFail_RecordsFailedWithRetryCount2()
    {
        // Arrange — RetryCount=2 → 总尝试 3 次，全部连接失败
        var (sender, store, logger) = CreateSender(retryCount: 2);
        var message = new EmailMessage { To = "test@example.com", Subject = "Test", Body = "Hello" };

        // Act — should not throw
        await sender.SendAsync(message);

        // Assert — 最终失败记录 + RetryCount 反映实际重试次数（= RetryCount 配置 = 2）
        Assert.Single(store.SavedRecords);
        Assert.Equal("Failed", store.SavedRecords[0].Status);
        Assert.Equal(2, store.SavedRecords[0].RetryCount);
        Assert.NotNull(store.SavedRecords[0].ErrorMessage);
        // 2 次重试警告 + 1 次最终失败警告
        Assert.Equal(2, logger.Warnings.Count(w => w.Contains("重试")));
        Assert.Single(logger.Warnings, w => w.Contains("邮件发送失败") && !w.Contains("重试"));
    }

    [Fact]
    public async Task SendAsync_RetryCount0_Default_NoRetry_RecordsFailedOnce()
    {
        // Arrange — RetryCount=0（默认）→ 总尝试 1 次，行为与 V0.1.x 一致
        var (sender, store, logger) = CreateSender(retryCount: 0);
        var message = new EmailMessage { To = "test@example.com", Subject = "Test", Body = "Hello" };

        // Act — should not throw
        await sender.SendAsync(message);

        // Assert — 单次失败记录，RetryCount 保持 0，无重试警告
        Assert.Single(store.SavedRecords);
        Assert.Equal("Failed", store.SavedRecords[0].Status);
        Assert.Equal(0, store.SavedRecords[0].RetryCount);
        Assert.DoesNotContain(logger.Warnings, w => w.Contains("重试"));
        Assert.Single(logger.Warnings, w => w.Contains("邮件发送失败"));
    }

    [Fact]
    public async Task SendAsync_RetryBaseDelayZero_FastExecution_AllAttemptsFail()
    {
        // Arrange — RetryBaseDelayMilliseconds=0 → 无等待快速执行（避免拖慢测试）
        var (sender, store, logger) = CreateSender(retryCount: 2, retryBaseDelayMilliseconds: 0);
        var message = new EmailMessage { To = "test@example.com", Subject = "Test", Body = "Hello" };

        // Act — should not throw，且不应因退避等待而拖慢（base=0 无等待；本机每次连接尝试约 2s）
        var sw = System.Diagnostics.Stopwatch.StartNew();
        await sender.SendAsync(message);
        sw.Stop();

        // Assert — 防挂起软断言（配置错误导致无限重试/长退避时会远超该值）+ 最终失败 + RetryCount=2
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"重试执行过慢: {sw.Elapsed}");
        Assert.Single(store.SavedRecords);
        Assert.Equal("Failed", store.SavedRecords[0].Status);
        Assert.Equal(2, store.SavedRecords[0].RetryCount);
    }

    [Fact]
    public async Task SendAsync_CancelledDuringRetry_NoFurtherRetry_SilentlySavesFailed()
    {
        // Arrange — RetryCount=2 + 大退避基数，便于在第一次重试的退避等待中取消
        var (sender, store, logger) = CreateSender(retryCount: 2, retryBaseDelayMilliseconds: 5000);
        var message = new EmailMessage { To = "test@example.com", Subject = "Test", Body = "Hello" };
        using var cts = new CancellationTokenSource();

        // Act — 启动发送，等待进入第一次重试退避后取消
        var sendTask = sender.SendAsync(message, cts.Token);
        await WaitUntilAsync(
            () => logger.Warnings.Any(w => w.Contains("重试")),
            TimeSpan.FromSeconds(10));
        cts.Cancel();

        // should not throw（取消走最终失败分支，异常静默）
        await sendTask;

        // Assert — 取消后不再重试：仅 1 次重试已计数（被中断），静默保存 Failed
        Assert.Single(store.SavedRecords);
        Assert.Equal("Failed", store.SavedRecords[0].Status);
        Assert.Equal(1, store.SavedRecords[0].RetryCount);
    }

    /// <summary>轮询等待条件成立（带超时防挂起）。</summary>
    private static async Task WaitUntilAsync(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException("等待条件超时");
            await Task.Delay(10);
        }
    }

    // ── Test helpers（与 SmtpEmailSenderTests 相同的 Fake 桩） ──

    /// <summary>简化 ILogger 桩：捕获 Warning 和 Information 日志。</summary>
    private sealed class FakeLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];
        public List<string> Informations { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
            else if (logLevel == LogLevel.Information)
                Informations.Add(formatter(state, exception));
        }
    }

    /// <summary>简化 IEmailRecordStore 桩：捕获保存的记录。</summary>
    private sealed class FakeEmailRecordStore : IEmailRecordStore
    {
        public List<EmailRecordEntity> SavedRecords { get; } = [];

        public Task<EmailRecordEntity?> GetAsync(long id, CancellationToken ct = default)
            => Task.FromResult<EmailRecordEntity?>(null);

        public Task<IReadOnlyList<EmailRecordEntity>> GetListAsync(string? status = null, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<EmailRecordEntity>>(Array.Empty<EmailRecordEntity>());

        public Task SaveAsync(EmailRecordEntity entity, CancellationToken ct = default)
        {
            if (entity != null)
                SavedRecords.Add(entity);
            return Task.CompletedTask;
        }
    }
}
