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
    /// <para>V0.2.0：配置化发送重试（指数退避）——<see cref="EmailingOptions.RetryCount"/> 次额外重试，
    /// 每次失败按 <see cref="EmailingOptions.RetryBaseDelayMilliseconds"/> 指数退避；取消时不再重试，
    /// 最终失败仍异常静默（记录 Failed + ErrorMessage + 保存 + Warning，不抛给调用方）。</para>
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

            // V0.2.0：配置化重试（指数退避）——总尝试次数 = RetryCount + 1（首次发送 + 失败后额外重试）
            var attempts = Math.Max(1, opts.RetryCount + 1);

            try
            {
                for (var i = 0; i < attempts; i++)
                {
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
                        return;
                    }
                    catch (Exception ex) when (!ct.IsCancellationRequested && i < attempts - 1)
                    {
                        // 失败但还有剩余重试机会（且未取消）——指数退避后重试：
                        // 第 i 次重试前等待 RetryBaseDelayMilliseconds * 2^(i-1) 毫秒（i 从 1 起）
                        record.RetryCount++;
                        var backoffMs = opts.RetryBaseDelayMilliseconds * (1 << i);
                        _logger.LogWarning(ex,
                            "邮件发送失败，准备第 {RetryIndex} 次重试（退避 {BackoffMs}ms）: To={To}, Subject={Subject}",
                            record.RetryCount, backoffMs, message.To, message.Subject);
                        await Task.Delay(backoffMs, ct);
                    }
                }
            }
            catch (Exception ex)
            {
                // 最终失败（含重试期间取消导致的 OperationCanceledException）——异常静默：记录错误但不抛出。
                // 终态保存用 CancellationToken.None：即使调用方已取消，也须保证 Failed 记录落库（对齐"取消时静默保存"语义）；
                // EmailRecordStore 本身异常静默（Upsert 失败仅 LogWarning），此处不会向调用方抛出。
                record.Status = "Failed";
                record.ErrorMessage = ex.Message;
                await _recordStore.SaveAsync(record, CancellationToken.None);

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
