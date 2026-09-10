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

        /// <summary>
        /// 是否使用隐式 SSL（SslOnConnect，465 端口）。默认 false = StartTls（587 端口标准）。
        /// <para>V0.1.1（评审修复）：原先硬编码 StartTls，无法对接仅支持隐式 SSL 的服务器。</para>
        /// </summary>
        public bool UseSsl { get; set; }

        /// <summary>SMTP 认证用户名。</summary>
        public string SmtpUser { get; set; } = "";

        /// <summary>SMTP 认证密码。</summary>
        public string SmtpPassword { get; set; } = "";

        /// <summary>默认发件人地址（EmailMessage.From 为空时使用）。</summary>
        public string DefaultFrom { get; set; } = "";

        /// <summary>是否启用邮件发送（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;

        /// <summary>
        /// 失败后额外重试次数（默认 0 = 不重试）。总发送尝试次数 = RetryCount + 1（首次发送 + 失败后额外重试）。
        /// <para>V0.2.0：发送重试（指数退避）——每次失败若还有剩余尝试且未取消，递增记录 RetryCount 并按
        /// <see cref="RetryBaseDelayMilliseconds"/> 退避后重试；最终失败仍异常静默（记录 Failed，不抛给调用方）。</para>
        /// </summary>
        public int RetryCount { get; set; } = 0;

        /// <summary>
        /// 指数退避基数（毫秒，默认 1000）：第 i 次重试前等待 <c>RetryBaseDelayMilliseconds * 2^(i-1)</c> 毫秒。
        /// <para>V0.2.0：测试或低延迟场景可设 0/1 以跳过等待。</para>
        /// </summary>
        public int RetryBaseDelayMilliseconds { get; set; } = 1000;
    }
}
