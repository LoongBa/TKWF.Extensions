namespace TKWF.Ext.Account
{
    /// <summary>
    /// 账户管理配置选项。
    /// <para>通过 <c>services.Configure&lt;AccountOptions&gt;(config.GetSection("TKWF:Account"))</c> 绑定。</para>
    /// </summary>
    public class AccountOptions
    {
        /// <summary>锁定阈值——连续失败次数达到该值后锁定（默认 5）。</summary>
        public int MaxFailedAttempts { get; set; } = 5;

        /// <summary>锁定分钟数（默认 15）。</summary>
        public int DefaultLockoutMinutes { get; set; } = 15;

        /// <summary>重置码有效期分钟数（默认 30）。</summary>
        public int ResetCodeValidityMinutes { get; set; } = 30;

        /// <summary>是否启用账户管理（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;
    }
}