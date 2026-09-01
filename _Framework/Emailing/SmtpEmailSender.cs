using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MimeKit;

namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// SMTP 邮件发送器实现——使用 MailKit 的 <see cref="SmtpClient"/> 发送邮件。
    /// <para>异常静默处理：发送失败时记录 Warning 日志，不抛出异常（不阻塞业务调用）。
    /// 发送成功后调用 <see cref="IEmailRecordStore.SaveAsync"/> 记录。</para>
    /// </summary>
    internal sealed class SmtpEmailSender : IEmailSender
    {
        private readonly IEmailRecordStore _recordStore;
        private readonly IOptions<EmailingOptions> _options;
        private readonly ILogger<SmtpEmailSender> _logger;

        public SmtpEmailSender(
            IEmailRecordStore recordStore,
            IOptions<EmailingOptions> options,
            ILogger<SmtpEmailSender> logger)
        {
            _recordStore = recordStore ?? throw new ArgumentNullException(nameof(recordStore));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <inheritdoc />
        public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
        {
            if (message == null)
            {
                _logger.LogWarning("邮件发送跳过：message 为 null");
                return;
            }

            var opts = _options.Value;
            if (!opts.IsEnabled)
            {
                _logger.LogWarning("邮件发送跳过：邮件发送已禁用");
                return;
            }

            // 创建邮件记录（初始状态 Pending）
            var record = new EmailRecordEntity
            {
                To = message.To,
                From = message.From ?? opts.DefaultFrom,
                Subject = message.Subject,
                Body = message.Body,
                IsHtml = message.IsHtml,
                Status = "Pending",
                CreateTime = DateTime.Now
            };

            try
            {
                var mimeMessage = BuildMimeMessage(message, opts);

                using var client = new SmtpClient();
                // V0.1.1（评审修复）：SSL 模式可配置——UseSsl=true 用 SslOnConnect（465 隐式），否则 StartTls（587 标准）
                var socketOptions = opts.UseSsl
                    ? MailKit.Security.SecureSocketOptions.SslOnConnect
                    : MailKit.Security.SecureSocketOptions.StartTls;
                await client.ConnectAsync(opts.SmtpHost, opts.SmtpPort, socketOptions, ct);
                await client.AuthenticateAsync(opts.SmtpUser, opts.SmtpPassword, ct);
                await client.SendAsync(mimeMessage, ct);
                await client.DisconnectAsync(true, ct);

                // 记录发送成功
                record.Status = "Sent";
                record.SendTime = DateTime.Now;
                await _recordStore.SaveAsync(record, ct);

                _logger.LogInformation("邮件发送成功: To={To}, Subject={Subject}", message.To, message.Subject);
            }
            catch (Exception ex)
            {
                // 异常静默：记录错误但不抛出
                record.Status = "Failed";
                record.ErrorMessage = ex.Message;
                await _recordStore.SaveAsync(record, ct);

                _logger.LogWarning(ex, "邮件发送失败: To={To}, Subject={Subject}", message.To, message.Subject);
            }
        }

        /// <summary>
        /// 构建 MimeKit 的 <see cref="MimeMessage"/>。
        /// </summary>
        private static MimeMessage BuildMimeMessage(EmailMessage message, EmailingOptions opts)
        {
            var mimeMessage = new MimeMessage();

            // 发件人
            var fromAddress = message.From ?? opts.DefaultFrom;
            mimeMessage.From.Add(MailboxAddress.Parse(fromAddress));

            // 收件人（支持逗号分隔多个）
            var toAddresses = message.To.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            foreach (var addr in toAddresses)
            {
                mimeMessage.To.Add(MailboxAddress.Parse(addr));
            }

            mimeMessage.Subject = message.Subject;

            // 正文
            var bodyBuilder = new BodyBuilder();
            if (message.IsHtml)
            {
                bodyBuilder.HtmlBody = message.Body;
            }
            else
            {
                bodyBuilder.TextBody = message.Body;
            }
            mimeMessage.Body = bodyBuilder.ToMessageBody();

            return mimeMessage;
        }
    }
}
