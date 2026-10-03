using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Emailing.Tests;

/// <summary>
/// SmtpEmailSender 测试——覆盖发送成功、异常静默、日志记录、Null 消息、配置读取。
/// <para>不真实连接 SMTP，使用 Mock/Fake 模式：SmtpEmailSender 依赖 IDomainUser（Use&lt;IEmailRecordStore&gt; 懒加载）
/// + IOptions&lt;EmailingOptions&gt;，发送失败时异常静默（catch + LogWarning）。</para>
/// </summary>
public class SmtpEmailSenderTests
{
    /// <summary>创建 SmtpEmailSender 实例（使用 Fake 依赖 + 能力容器桩）。</summary>
    private static (SmtpEmailSender Sender, FakeEmailRecordStore Store, FakeLogger<SmtpEmailSender> Logger) CreateSender(
        bool isEnabled = true,
        string smtpHost = "localhost",
        int smtpPort = 25)
    {
        var store = new FakeEmailRecordStore();
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
        // ADR88/DI004：SmtpEmailSender 不再注入 IEmailRecordStore——V4.10.53 改内部接线型
        // （ctor(IServiceProvider)，经 GetRequiredService 懒加载 Store——接口 key 精确匹配）。
        var stub = new StubDomainUser();
        var services = new ServiceCollection();
        services.AddSingleton<IEmailRecordStore>(store);
        stub.ServiceProvider = services.BuildServiceProvider();
        var sender = new SmtpEmailSender(stub.ServiceProvider!, options, logger);
        return (sender, store, logger);
    }

    [Fact]
    public async Task SendAsync_NullMessage_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var (sender, store, logger) = CreateSender();

        // Act — should not throw
        await sender.SendAsync(null!);

        // Assert
        Assert.Empty(store.SavedRecords);
        Assert.Single(logger.Warnings);
        Assert.Contains("message 为 null", logger.Warnings[0]);
    }

    [Fact]
    public async Task SendAsync_Disabled_LogsWarningAndDoesNotThrow()
    {
        // Arrange
        var (sender, store, logger) = CreateSender(isEnabled: false);
        var message = new EmailMessage { To = "test@example.com", Subject = "Hello" };

        // Act — should not throw
        await sender.SendAsync(message);

        // Assert
        Assert.Empty(store.SavedRecords);
        Assert.Single(logger.Warnings);
        Assert.Contains("已禁用", logger.Warnings[0]);
    }

    [Fact]
    public async Task SendAsync_SmtpConnectionFails_LogsWarningAndRecordsFailure()
    {
        // Arrange — SmtpHost = "localhost" 连接不上
        var (sender, store, logger) = CreateSender(smtpHost: "127.0.0.1", smtpPort: 19999);
        var message = new EmailMessage
        {
            To = "test@example.com",
            Subject = "Test",
            Body = "Hello"
        };

        // Act — should not throw
        await sender.SendAsync(message);

        // Assert — 记录了 Failed 状态
        Assert.Single(store.SavedRecords);
        Assert.Equal("Failed", store.SavedRecords[0].Status);
        Assert.NotNull(store.SavedRecords[0].ErrorMessage);
        Assert.Contains("邮件发送失败", logger.Warnings[0]);
    }

    [Fact]
    public async Task SendAsync_UsesDefaultFrom_WhenMessageFromIsNull()
    {
        // Arrange
        var (sender, store, logger) = CreateSender();
        var message = new EmailMessage
        {
            To = "test@example.com",
            From = null,
            Subject = "Test From Default"
        };

        // Act — 连接会失败，但能验证 From 被正确设置
        await sender.SendAsync(message);

        // Assert — 检查记录中的 From 使用了 DefaultFrom
        Assert.Single(store.SavedRecords);
        Assert.Equal("default@example.com", store.SavedRecords[0].From);
    }

    [Fact]
    public void Constructor_NullUser_Throws()
    {
        var logger = new FakeLogger<SmtpEmailSender>();
        var options = Options.Create(new EmailingOptions());
        Assert.Throws<ArgumentNullException>(() => new SmtpEmailSender(null!, options, logger));
    }

    // ── Test helpers ──

    /// <summary>最小 IDomainUser 桩——Use&lt;T&gt;()/GetService&lt;T&gt;() 从注入的 ServiceProvider 容器解析
    /// （测试工厂经 <see cref="ServiceProvider"/> 注入；未注入时抛 NotSupportedException）。</summary>
    private sealed class StubDomainUser : IDomainUser
    {
        private IServiceProvider? _provider;
        private readonly object _gate = new();
        private readonly Dictionary<Type, object?> _cache = new();

        /// <summary>ServiceProvider（测试工厂注册时注入——懒加载 Use&lt;T&gt; 解析源，线程安全）。</summary>
        public IServiceProvider ServiceProvider
        {
            set { lock (_gate) _provider = value; }
            get { lock (_gate) return _provider!; }
        }

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
            IServiceProvider provider;
            lock (_gate) provider = _provider ?? throw new NotSupportedException("Stub: Use<T> not supported in unit tests");
            if (_cache.TryGetValue(typeof(TDomainService), out var cached) && cached is TDomainService svc)
                return svc;
            lock (_gate)
            {
                if (_cache.TryGetValue(typeof(TDomainService), out cached) && cached is TDomainService svc2)
                    return svc2;
                var resolved = provider.GetRequiredService<TDomainService>();
                _cache[typeof(TDomainService)] = resolved;
                return resolved;
            }
        }

        public TService GetService<TService>() where TService : notnull
        {
            IServiceProvider provider;
            lock (_gate) provider = _provider ?? throw new NotSupportedException("Stub: GetService<T> not supported in unit tests");
            return provider.GetRequiredService<TService>();
        }

        public TService GetOptionalService<TService>() where TService : class => null!;
        public IEnumerable<TService> GetServices<TService>() where TService : notnull => [];
    }

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
