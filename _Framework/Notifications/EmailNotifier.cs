using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKWF.Ext.Emailing;

namespace TKWF.Ext.Notifications
{
    /// <summary>
    /// 邮件通知器——Email 通道（best-effort，M1 修订：外部通道投递失败自行处理，不重抛阻塞发布流程）。
    /// <para>构造注入 <see cref="IServiceProvider"/> + <see cref="ILogger{TCategoryName}"/>，内部<b>延迟可空解析</b>
    /// <see cref="IEmailSender"/>（Emailing 扩展未启用 → 未注册）与 <see cref="IUserEmailProvider"/>（消费方未实现）
    /// ——对齐 <see cref="NotificationPublisher"/> 的 IServiceProvider 可空解析先例（C1 模式），前置缺失时构造不失败。</para>
    /// <para>投递语义：任一前置缺失（IEmailSender null / IUserEmailProvider null / 邮箱 null 或空白）→ LogWarning 返回不抛异常；
    /// 组装 <see cref="EmailMessage"/>（To=邮箱, Subject=通知名, Body=DataJson）经 <see cref="IEmailSender.SendAsync"/> 发送；
    /// 发送异常 → catch + LogWarning 不重抛。外部通道不参与发布事务（C4）——失败不回滚 inbox 写入。</para>
    /// </summary>
    internal sealed class EmailNotifier : INotificationNotifier
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<EmailNotifier> _logger;

        public EmailNotifier(IServiceProvider serviceProvider, ILogger<EmailNotifier> logger)
        {
            _serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>通道名。</summary>
        public string Name => "Email";

        public async Task DeliverAsync(NotificationDeliveryRequest request, CancellationToken ct = default)
        {
            var emailSender = _serviceProvider.GetService<IEmailSender>();
            if (emailSender == null)
            {
                _logger.LogWarning(
                    "Email 通道投递跳过：IEmailSender 未注册（须启用 Emailing 扩展）。通知 {NotificationName} → 用户 {UserId}",
                    request.NotificationName, request.UserId);
                return;
            }

            var emailProvider = _serviceProvider.GetService<IUserEmailProvider>();
            if (emailProvider == null)
            {
                _logger.LogWarning(
                    "Email 通道投递跳过：IUserEmailProvider 未实现（消费方须注册邮箱提供者）。通知 {NotificationName} → 用户 {UserId}",
                    request.NotificationName, request.UserId);
                return;
            }

            // 投递段整体 best-effort（M1）：邮箱解析 / 组装 / 发送任一失败 → LogWarning，不重抛阻塞发布流程
            try
            {
                var email = await emailProvider.GetEmailAsync(request.UserId, ct);
                if (string.IsNullOrWhiteSpace(email))
                {
                    _logger.LogWarning(
                        "Email 通道投递跳过：用户 {UserId} 无邮箱。通知 {NotificationName}",
                        request.UserId, request.NotificationName);
                    return;
                }

                var message = new EmailMessage
                {
                    To = email,
                    Subject = request.NotificationName,
                    Body = request.DataJson
                };

                await emailSender.SendAsync(message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Email 通道投递失败（best-effort 不重抛）：通知 {NotificationName} → 用户 {UserId}",
                    request.NotificationName, request.UserId);
            }
        }
    }
}