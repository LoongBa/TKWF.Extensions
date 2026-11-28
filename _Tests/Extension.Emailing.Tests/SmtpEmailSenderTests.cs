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
/// SmtpEmailSender 测试（A5 Store-debt cleanup 后）——记录写入经真实 EmailRecordEntityDataService + SQLite 内存库。
/// </summary>
public class SmtpEmailSenderTests
{
    private static (SmtpEmailSender Sender, IFreeSql Fsql, FakeLogger<SmtpEmailSender> Logger) CreateSender(
        bool isEnabled = true,
        string smtpHost = "localhost",
        int smtpPort = 25)
    {
        var logger = new FakeLogger<SmtpEmailSender>();
        var options = Options.Create(new EmailingOptions
        {
            SmtpHost = smtpHost,
            SmtpPort = smtpPort,
            SmtpUser = "user",
            SmtpPassword = "pass",
            DefaultFrom = "default@example.com",
            IsEnabled = isEnabled
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
    public async Task SendAsync_NullMessage_LogsWarningAndDoesNotThrow()
    {
        var (sender, fsql, logger) = CreateSender();

        await sender.SendAsync(null!, ct: TestContext.Current.CancellationToken);

        Assert.Empty(SavedRecords(fsql));
        Assert.Single(logger.Warnings);
        Assert.Contains("message 为 null", logger.Warnings[0]);
    }

    [Fact]
    public async Task SendAsync_Disabled_LogsWarningAndDoesNotThrow()
    {
        var (sender, fsql, logger) = CreateSender(isEnabled: false);
        var message = new EmailMessage { To = "test@example.com", Subject = "Hello" };

        await sender.SendAsync(message, ct: TestContext.Current.CancellationToken);

        Assert.Empty(SavedRecords(fsql));
        Assert.Single(logger.Warnings);
        Assert.Contains("已禁用", logger.Warnings[0]);
    }

    [Fact]
    public async Task SendAsync_SmtpConnectionFails_LogsWarningAndRecordsFailure()
    {
        var (sender, fsql, logger) = CreateSender(smtpHost: "127.0.0.1", smtpPort: 19999);
        var message = new EmailMessage
        {
            To = "test@example.com",
            Subject = "Test",
            Body = "Hello"
        };

        await sender.SendAsync(message, ct: TestContext.Current.CancellationToken);

        var records = SavedRecords(fsql);
        Assert.Single(records);
        Assert.Equal("Failed", records[0].Status);
        Assert.NotNull(records[0].ErrorMessage);
        Assert.Contains("邮件发送失败", logger.Warnings[0]);
    }

    [Fact]
    public async Task SendAsync_UsesDefaultFrom_WhenMessageFromIsNull()
    {
        var (sender, fsql, _) = CreateSender();
        var message = new EmailMessage
        {
            To = "test@example.com",
            From = null,
            Subject = "Test From Default"
        };

        await sender.SendAsync(message, ct: TestContext.Current.CancellationToken);

        var records = SavedRecords(fsql);
        Assert.Single(records);
        Assert.Equal("default@example.com", records[0].From);
    }

    [Fact]
    public void Constructor_NullProvider_Throws()
    {
        var logger = new FakeLogger<SmtpEmailSender>();
        var options = Options.Create(new EmailingOptions());
        Assert.Throws<ArgumentNullException>(() => new SmtpEmailSender(null!, options, logger));
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
