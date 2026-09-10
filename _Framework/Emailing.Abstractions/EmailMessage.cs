// 契约定义于 TKWF.Ext.Emailing.Abstractions（ADR48 D7 依赖倒置）——Emailing 实现与消费方共用。
// 命名空间保持 TKWF.Ext.Emailing：既有消费方 using 不变，零破坏（对齐 Account.Abstractions 迁移先例）。
namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// 邮件消息模型——定义发送邮件所需的信息（收件人、发件人、主题、正文、是否 HTML）。
    /// <para>契约定义于 <c>TKWF.Ext.Emailing.Abstractions</c>（ADR48 D7 依赖倒置）。</para>
    /// </summary>
    public class EmailMessage
    {
        /// <summary>收件人（多个以逗号分隔）。</summary>
        public string To { get; set; } = "";

        /// <summary>发件人地址（为空时使用配置中的 DefaultFrom）。</summary>
        public string? From { get; set; }

        /// <summary>邮件主题。</summary>
        public string Subject { get; set; } = "";

        /// <summary>邮件正文。</summary>
        public string? Body { get; set; }

        /// <summary>是否为 HTML 格式正文（默认 false）。</summary>
        public bool IsHtml { get; set; }
    }
}
