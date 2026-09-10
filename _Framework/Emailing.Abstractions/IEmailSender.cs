using System.Threading;
using System.Threading.Tasks;

// 契约定义于 TKWF.Ext.Emailing.Abstractions（ADR48 D7 依赖倒置）——Emailing 实现与消费方共用。
// 命名空间保持 TKWF.Ext.Emailing：既有消费方 using 不变，零破坏（对齐 Account.Abstractions 迁移先例）。
namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// 邮件发送器抽象——定义邮件发送操作。
    /// <para>契约定义于 <c>TKWF.Ext.Emailing.Abstractions</c>（ADR48 D7 依赖倒置）——
    /// Emailing 实现项目与消费方共用；默认实现为 SMTP（<c>SmtpEmailSender</c>，MailKit），
    /// 后续可扩展 SendGrid / Amazon SES 等。</para>
    /// </summary>
    public interface IEmailSender
    {
        /// <summary>发送邮件。</summary>
        Task SendAsync(EmailMessage message, CancellationToken ct = default);
    }
}
