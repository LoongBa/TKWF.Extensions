namespace TKWF.Ext.Emailing
{
    /// <summary>
    /// 邮件发送配置选项。
    /// <para>通过 <c>services.Configure&lt;EmailingOptions&gt;(config.GetSection("TKWF:Emailing"))</c> 绑定。</para>
    /// </summary>
    public class EmailingOptions
    {
        /// <summary>SMTP 服务器主机名。</summary>
        public string SmtpHost { get; set; } = "";

        /// <summary>SMTP 服务器端口（默认 587）。</summary>
        public int SmtpPort { get; set; } = 587;

        /// <summary>SMTP 认证用户名。</summary>
        public string SmtpUser { get; set; } = "";

        /// <summary>SMTP 认证密码。</summary>
        public string SmtpPassword { get; set; } = "";

        /// <summary>默认发件人地址（EmailMessage.From 为空时使用）。</summary>
        public string DefaultFrom { get; set; } = "";

        /// <summary>是否启用邮件发送（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;
    }
}
