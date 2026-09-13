using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace TKWF.Ext.Notifications.SignalR
{
    /// <summary>
    /// SignalR 通知器——"SignalR" 通道（best-effort，M1 修订：外部通道投递失败自行处理，不重抛阻塞发布流程）。
    /// <para>构造注入 <see cref="IServiceProvider"/> + <see cref="ILogger{TCategoryName}"/>，内部<b>延迟可空解析</b>
    /// <see cref="IHubContext{T}"/>（消费方未调 <c>AddSignalR()</c> → 未注册 → 构造不失败，投递 LogWarning 跳过）——
    /// 对齐 <see cref="EmailNotifier"/> 的 IServiceProvider 可空解析先例（C1 模式）。</para>
    /// <para>DI 语义（Oracle P2 评审确认）：<see cref="IHubContext{THub}"/> 是 <b>Singleton</b> 注册，本通知器是
    /// <b>Scoped</b>——Scoped 解析 Singleton 合法（无 captive dependency 问题，反向才违规）。</para>
    /// <para>用户标识契约（P2-2）：<c>Clients.User(userId.ToString(CultureInfo.InvariantCulture))</c>——
    /// 消费方认证管线的 <c>DefaultUserIdProvider</c>（读 <c>ClaimTypes.NameIdentifier</c>）须保证该 claim 值为
    /// userId 的 InvariantCulture 字符串形式；非标准 claim 时消费方注册自定义 <see cref="IUserIdProvider"/>。</para>
    /// <para>投递语义：任一前置缺失（<see cref="IHubContext{NotificationsHub}"/> null）→ LogWarning 返回不抛异常；
    /// 无在线连接 → SignalR <c>Clients.User</c> 空操作自然跳过；<c>SendAsync</c> 异常 → catch + LogWarning 不重抛。
    /// 外部通道不参与发布事务（C4）——失败不回滚 inbox 写入。</para>
    /// </summary>
    internal sealed class SignalRNotifier : INotificationNotifier
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SignalRNotifier> _logger;

        public SignalRNotifier(IServiceProvider serviceProvider, ILogger<SignalRNotifier> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>通道名（通道路由匹配——"SignalR" 声明在定义 UseChannels 或用户偏好中）。</summary>
        public string Name => "SignalR";

        public async Task DeliverAsync(NotificationDeliveryRequest request, CancellationToken ct = default)
        {
            // 延迟可空解析（C1 模式）：消费方未 AddSignalR() → IHubContext 未注册 → 前置缺失跳过
            var hubContext = _serviceProvider.GetService<IHubContext<NotificationsHub>>();
            if (hubContext == null)
            {
                _logger.LogWarning(
                    "SignalR 通道投递跳过：IHubContext<NotificationsHub> 未注册（消费方须 services.AddSignalR()）。通知 {NotificationName} → 用户 {UserId}",
                    request.NotificationName, request.UserId);
                return;
            }

            // 投递段整体 best-effort（M1）：SendAsync 失败 → LogWarning，不重抛阻塞发布流程
            try
            {
                var payload = new SignalRNotificationPayload(
                    request.NotificationId,
                    request.NotificationName,
                    request.Severity.ToString(),
                    request.DataJson,
                    DateTime.UtcNow);

                // 方法名经 Options 解析（未注册 IOptions 时兜底 Options 默认值——可靠路径对齐 HealthCheck ResolveOptions）
                var methodName = _serviceProvider.GetService<Microsoft.Extensions.Options.IOptions<NotificationsSignalROptions>>()?.Value.MethodName
                    ?? new NotificationsSignalROptions().MethodName;

                await hubContext.Clients
                    .User(request.UserId.ToString(CultureInfo.InvariantCulture))
                    .SendAsync(methodName, payload, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "SignalR 通道投递失败（best-effort 不重抛）：通知 {NotificationName} → 用户 {UserId}",
                    request.NotificationName, request.UserId);
            }
        }
    }
}