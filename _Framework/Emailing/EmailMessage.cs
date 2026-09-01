namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// 邮件消息模型——定义发送邮件所需的信息（收件人、发件人、主题、正文、是否 HTML）。
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
