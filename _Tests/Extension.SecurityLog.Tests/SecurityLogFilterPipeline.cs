using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Core;
using TKW.Framework.Core.AuthController;
using TKW.Framework.Domain;
using TKW.Framework.Domain.AuthController;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Interception;

namespace TKWF.Ext.SecurityLog.Tests;

/// <summary>
/// 过滤器测试设施——<b>真实 <see cref="StaticDomainInterceptor"/> AOP 管线</b> + 假 <see cref="IDomainHost"/>。
/// <para>验证采集路径（C1 实测结论）：全局注册的安全日志过滤器经 Domain AOP 拦截 AuthController 安全方法——
/// 与生产路径同构（<c>User.Use&lt;IAuthController&gt;()</c> → 装饰器 → <c>InterceptAsync</c> →
/// GlobalFilters 的 CanWeGo/PostProceed）。</para>
/// <para>差异仅在于：假 host 直接提供 <see cref="DomainContext{TUserInfo}"/>（免去完整 DomainHost 启动），
/// 过滤器与拦截器逻辑与生产完全一致（PreProceed → proceed → PostProceed + 异常路径 Bag["__Exception"]）。</para>
/// </summary>
internal static class SecurityLogFilterPipeline
{
    /// <summary>运行一次 AOP 拦截，返回捕获到的安全事件列表。</summary>
    public static async Task<List<SecurityLogEntry>> RunAsync(
        Action<PipelineContext> setup)
    {
        var ctx = new PipelineContext();
        setup(ctx);
        ArgumentNullException.ThrowIfNull(ctx.Target, nameof(ctx.Target));
        ArgumentNullException.ThrowIfNull(ctx.MethodName, nameof(ctx.MethodName));

        // ── DI：Store + Options + Ambient + CorrelationId ──
        var services = new ServiceCollection();
        var store = ctx.Store ?? new CapturingSecurityLogStore();
        services.AddSingleton<ISecurityLogStore>(store);
        services.AddSingleton<ICorrelationIdProvider>(ctx.CorrelationIdProvider ?? new FakeCorrelationIdProvider());
        services.AddSingleton<IOptions<SecurityLoggingOptions>>(
            new OptionsWrapper<SecurityLoggingOptions>(ctx.Options ?? new SecurityLoggingOptions()));
        services.AddScoped<IAmbientContext>(_ => ctx.Ambient ?? new FakeAmbientContext());
        using var provider = services.BuildServiceProvider();

        // ── DomainUser（初始游客；proceed 内可升级为登录态）──
        var user = new DomainUser<TestUserInfo>
        {
            SessionKey = "test-session",
            UserInfo = new TestUserInfo("guest", "Guest"),
        };

        // ── 假 IDomainHost：GlobalFilters 含安全日志过滤器 ──
        var filter = ctx.Filter ?? new SecurityLogFilterAttribute<TestUserInfo>();
        var host = new FilterTestDomainHost(user, filter);

        // ── 真实拦截器 ──
        var interceptor = new StaticDomainInterceptor(host, provider.GetRequiredService<IServiceScopeFactory>());

        var invocation = new InvocationContext(
            proxy: ctx.Target,
            target: ctx.Target,
            methodName: ctx.MethodName,
            arguments: ctx.Arguments);

        // ── 执行（proceed 模拟目标方法体）──
        await interceptor.InterceptAsync(invocation, () => ctx.Proceed?.Invoke(user, invocation) ?? Task.CompletedTask);

        return store is CapturingSecurityLogStore cap ? cap.Entries : [];
    }

    /// <summary>管线上下文——每个测试按需配置。</summary>
    public sealed class PipelineContext
    {
        /// <summary>拦截目标（IAuthController / IPasswordResetFlow 实现）。</summary>
        public object? Target { get; set; }

        /// <summary>拦截方法名。</summary>
        public string? MethodName { get; set; }

        /// <summary>方法参数。</summary>
        public object[] Arguments { get; set; } = [];

        /// <summary>目标方法体（成功 = 设置 UserInfo/ReturnValue；失败 = 抛 AuthenticationException）。</summary>
        public Func<DomainUser<TestUserInfo>, InvocationContext, Task>? Proceed { get; set; }

        /// <summary>捕获型 Store（默认自动创建）。</summary>
        public ISecurityLogStore? Store { get; set; }

        /// <summary>Options（默认 Enabled=true + 全部事件）。</summary>
        public SecurityLoggingOptions? Options { get; set; }

        /// <summary>Ambient（ClientIp/UserAgent）。</summary>
        public IAmbientContext? Ambient { get; set; }

        /// <summary>CorrelationId 提供者（默认无关联 ID）。</summary>
        public ICorrelationIdProvider? CorrelationIdProvider { get; set; }

        /// <summary>待测试过滤器（默认 <see cref="SecurityLogFilterAttribute{TUserInfo}"/>）。</summary>
        public SecurityLogFilterAttribute<TestUserInfo>? Filter { get; set; }
    }

    /// <summary>捕获型 Store：记录所有 SaveAsync 的条目。</summary>
    public sealed class CapturingSecurityLogStore : ISecurityLogStore
    {
        public List<SecurityLogEntry> Entries { get; } = [];

        /// <summary>设置后在 SaveAsync 抛出（验证异常静默）。</summary>
        public Exception? ThrowOnSave { get; set; }

        public Task SaveAsync(SecurityLogEntry entry, CancellationToken ct = default)
        {
            if (ThrowOnSave != null) throw ThrowOnSave;
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    /// <summary>假 ambient——预设 ClientIp/UserAgent。</summary>
    public sealed class FakeAmbientContext : IAmbientContext
    {
        private readonly Dictionary<string, object?> _values = new(StringComparer.Ordinal);

        public FakeAmbientContext With(string key, string value)
        {
            _values[key] = value;
            return this;
        }

        public void Set<T>(string key, T value) => _values[key] = value;

        public T? Get<T>(string key)
            => _values.TryGetValue(key, out var v) ? (T?)v : default;

        public bool TryGet<T>(string key, out T value)
        {
            if (_values.TryGetValue(key, out var v) && v is T t)
            {
                value = t;
                return true;
            }
            value = default!;
            return false;
        }
    }

    /// <summary>假关联 ID 提供者。</summary>
    public sealed class FakeCorrelationIdProvider : ICorrelationIdProvider
    {
        public string? CurrentId { get; set; }

        public IDisposable Change(string? correlationId)
        {
            var previous = CurrentId;
            CurrentId = correlationId;
            return new RestoreScope(() => CurrentId = previous);
        }

        private sealed class RestoreScope(Action restore) : IDisposable
        {
            private readonly Action _restore = restore;
            private bool _disposed;
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _restore();
            }
        }
    }

    /// <summary>假 IDomainHost——GlobalFilters 持有安全日志过滤器；NewDomainContext 绑定拦截器作用域。</summary>
    private sealed class FilterTestDomainHost : IDomainHost
    {
        private readonly DomainUser<TestUserInfo> _user;

        public FilterTestDomainHost(DomainUser<TestUserInfo> user, params DomainFilterAttribute[] filters)
        {
            _user = user;
            GlobalFilters = filters;
        }

        public IReadOnlyList<DomainFilterAttribute> GlobalFilters { get; }

        public ILoggerFactory LoggerFactory { get; } = NullLoggerFactory.Instance;

        public DefaultExceptionLoggerFactory? ExceptionLoggerFactory => null;

        public DomainContext NewDomainContext(InvocationContext invocation, IServiceProvider sp)
        {
            // 对齐真实 DomainHost.NewDomainContext：把拦截器作用域绑定到当前异步流（过滤器经
            // DomainUser.GetOptionalService 从该作用域解析 Store/Options/Ambient）
            DomainUser<TestUserInfo>.BindScope(sp);
            return new DomainContext<TestUserInfo>(_user, invocation, new DomainContracts<TestUserInfo>(), sp, LoggerFactory);
        }
    }
}

/// <summary>测试用 IAuthController 桩——实现全部 6 个安全方法（方法体不被真实调用，proceed 由测试控制）。</summary>
internal sealed class FakeAuthController : IAuthController
{
    public Task<LoginPayload> LoginByPasswordAsync(string userName, string password, CancellationToken ct = default)
        => Task.FromResult(new LoginPayload(false, null, null, null));

    public Task<LoginPayload> LoginByContextAsync(LoginContextInput input, CancellationToken ct = default)
        => Task.FromResult(new LoginPayload(false, null, null, null));

    public Task<LoginPayload> LogoutAsync(bool broadcast = false, CancellationToken ct = default)
        => Task.FromResult(new LoginPayload(false, null, null, null));

    public Task<ChallengeResponse> RequestChallengeAsync(string userName, CancellationToken ct = default)
        => Task.FromResult(new ChallengeResponse("token", "salt", 1000));

    public Task<RegisterResult> RegisterSecureAsync(RegisterSecureInput input, CancellationToken ct = default)
        => Task.FromResult(new RegisterResult(true));

    public Task<RegisterResult> ChangePasswordSecureAsync(ChangePasswordSecureInput input, CancellationToken ct = default)
        => Task.FromResult(new RegisterResult(true));
}

/// <summary>测试用 IPasswordResetFlow 桩。</summary>
internal sealed class FakePasswordResetFlow : IPasswordResetFlow
{
    public Task<bool> InitiateResetAsync(string userName, CancellationToken ct = default)
        => Task.FromResult(true);

    public Task<ResetResult> CompleteResetAsync(string userName, string resetCode, string newClientHash, string salt, CancellationToken ct = default)
        => Task.FromResult(new ResetResult(true));
}

/// <summary>测试用 ILogger 桩——捕获 Warning 日志。</summary>
internal sealed class FakeLogger<T> : ILogger<T>
{
    public List<string> Warnings { get; } = [];

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (logLevel == LogLevel.Warning)
            Warnings.Add(formatter(state, exception));
    }
}
