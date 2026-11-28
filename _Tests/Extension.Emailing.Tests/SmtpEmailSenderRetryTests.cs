using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Emailing.Tests;

/// <summary>
/// SmtpEmailSender 发送重试测试（A5 Store-debt cleanup 后）——记录写入经真实 EmailRecordEntityDataService + SQLite。
/// </summary>
public class SmtpEmailSenderRetryTests
{
    private static (SmtpEmailSender Sender, IFreeSql Fsql, FakeLogger<SmtpEmailSender> Logger) CreateSender(
        int retryCount,
        int retryBaseDelayMilliseconds = 1)
    {
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

        var fsql = new FreeSql.FreeSqlBuilder()
            .UseConnectionString(FreeSql.DataType.Sqlite, "Data Source=:memory:")
            .UseAutoSyncStructure(true)
            .Build();
        fsql.CodeFirst.SyncStructure<EmailRecordEntity>();

        var services = new ServiceCollection();
        services.AddSingleton<IFreeSql>(fsql);
        services.AddSingleton<UnitOfWorkManager>();
        services.AddSingleton<IEntityDAC<EmailRecordEntity>, FreeSqlEntityDAC<EmailRecordEntity>>();
        services.AddSingleton(sp =>
        {
            var user = new StubDomainUser { ServiceProvider = sp };
            return (EmailRecordEntityDataService)ActivatorUtilities.CreateInstance(
                sp, typeof(EmailRecordEntityDataService), user);
        });
        var provider = services.BuildServiceProvider();
        var sender = new SmtpEmailSender(provider, options, logger);
        return (sender, fsql, logger);
    }

    private static List<EmailRecordEntity> SavedRecords(IFreeSql fsql)
        => fsql.Select<EmailRecordEntity>().ToList();

    [Fact]
    public async Task SendAsync_RetryCount2_AllAttemptsFail_RecordsFailedWithRetryCount2()
    {
        var (sender, fsql, logger) = CreateSender(retryCount: 2);
        var message = new EmailMessage { To = "test@example.com", Subject = "Test", Body = "Hello" };

        await sender.SendAsync(message, ct: TestContext.Current.CancellationToken);

        var records = SavedRecords(fsql);
        Assert.Single(records);
        Assert.Equal("Failed", records[0].Status);
        Assert.Equal(2, records[0].RetryCount);
        Assert.NotNull(records[0].ErrorMessage);
        Assert.Equal(2, logger.Warnings.Count(w => w.Contains("重试")));
        Assert.Single(logger.Warnings, w => w.Contains("邮件发送失败") && !w.Contains("重试"));
    }

    [Fact]
    public async Task SendAsync_RetryCount0_Default_NoRetry_RecordsFailedOnce()
    {
        var (sender, fsql, logger) = CreateSender(retryCount: 0);
        var message = new EmailMessage { To = "test@example.com", Subject = "Test", Body = "Hello" };

        await sender.SendAsync(message, ct: TestContext.Current.CancellationToken);

        var records = SavedRecords(fsql);
        Assert.Single(records);
        Assert.Equal("Failed", records[0].Status);
        Assert.Equal(0, records[0].RetryCount);
        Assert.DoesNotContain(logger.Warnings, w => w.Contains("重试"));
        Assert.Single(logger.Warnings, w => w.Contains("邮件发送失败"));
    }

    [Fact]
    public async Task SendAsync_RetryBaseDelayZero_FastExecution_AllAttemptsFail()
    {
        var (sender, fsql, _) = CreateSender(retryCount: 2, retryBaseDelayMilliseconds: 0);
        var message = new EmailMessage { To = "test@example.com", Subject = "Test", Body = "Hello" };

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await sender.SendAsync(message, ct: TestContext.Current.CancellationToken);
        sw.Stop();

        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"重试执行过慢: {sw.Elapsed}");
        var records = SavedRecords(fsql);
        Assert.Single(records);
        Assert.Equal("Failed", records[0].Status);
        Assert.Equal(2, records[0].RetryCount);
    }

    [Fact]
    public async Task SendAsync_CancelledDuringRetry_NoFurtherRetry_SilentlySavesFailed()
    {
        var (sender, fsql, logger) = CreateSender(retryCount: 2, retryBaseDelayMilliseconds: 5000);
        var message = new EmailMessage { To = "test@example.com", Subject = "Test", Body = "Hello" };
        using var cts = new CancellationTokenSource();

        var sendTask = sender.SendAsync(message, cts.Token);
        await WaitUntilAsync(
            () => logger.Warnings.Any(w => w.Contains("重试")),
            TimeSpan.FromSeconds(10));
        cts.Cancel();

        await sendTask;

        var records = SavedRecords(fsql);
        Assert.Single(records);
        Assert.Equal("Failed", records[0].Status);
        Assert.Equal(1, records[0].RetryCount);
    }

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

    private sealed class StubDomainUser : IDomainUser
    {
        public IServiceProvider? ServiceProvider { get; set; }
        public string SessionKey => "test-session";
        public bool IsAuthenticated => false;
        public bool IsSystemActor => false;
        public IUserInfo? UserInfo => null;
        public long? TenantId => null;
        public bool IsNoAuditActive => false;
        public string? UserId => null;
        public string? UserName => null;
        public bool IsInRole(string role) => false;

        public TDomainService Use<TDomainService>() where TDomainService : IDomainService
        {
            if (ServiceProvider is null) throw new NotSupportedException("Stub: Use<T> 未注入 ServiceProvider");
            return (TDomainService)ActivatorUtilities.CreateInstance(ServiceProvider, typeof(TDomainService), this);
        }

        public TService GetService<TService>() where TService : notnull
        {
            if (ServiceProvider is null) throw new NotSupportedException("Stub: GetService<T> 未注入 ServiceProvider");
            return ServiceProvider.GetRequiredService<TService>();
        }

        public TService GetOptionalService<TService>() where TService : class => null!;
        public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
    }

    private sealed class FakeLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];
        public List<string> Informations { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) Warnings.Add(formatter(state, exception));
            else if (logLevel == LogLevel.Information) Informations.Add(formatter(state, exception));
        }
    }
}
