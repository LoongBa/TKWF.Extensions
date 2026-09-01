namespace TKWF.Ext.Identity
{
    /// <summary>
    /// 身份管理配置选项。
    /// <para>通过 <c>services.Configure&lt;IdentityOptions&gt;(config.GetSection("TKWF:Identity"))</c> 绑定。</para>
    /// </summary>
    public class IdentityOptions
    {
        /// <summary>密码最小长度（默认 6）。</summary>
        public int PasswordMinLength { get; set; } = 6;

        /// <summary>是否启用身份管理（默认 true）。</summary>
        public bool IsEnabled { get; set; } = true;
    }
}