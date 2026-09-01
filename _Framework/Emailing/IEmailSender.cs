using System.Threading;
using System.Threading.Tasks;

namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// 邮件发送器抽象——定义邮件发送操作。
    /// <para>V0.1.0 SMTP 默认实现；后续可扩展 SendGrid / Amazon SES 等。</para>
    /// </summary>
    public interface IEmailSender
    {
        /// <summary>发送邮件。</summary>
        Task SendAsync(EmailMessage message, CancellationToken ct = default);
    }
}
