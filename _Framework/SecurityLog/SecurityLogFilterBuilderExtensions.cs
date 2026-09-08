using System;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.SecurityLog
{
    /// <summary>
    /// <see cref="FilterBuilder{TUserInfo}"/> 扩展——安全日志过滤器注册入口（消费方 opt-in，对齐 <c>AddAuditLog</c> 先例）。
    /// <para>用法（消费方 <c>DomainHostInitializerBase&lt;TUserInfo&gt;</c> 派生类的 <c>ConfigureGlobalFilters</c>）：</para>
    /// <code>
    /// protected override void ConfigureGlobalFilters(FilterBuilder&lt;MyUserInfo&gt; builder)
    /// {
    ///     builder.AddSecurityLog();   // 全局注册安全日志采集过滤器（CanWeGo 白名单只拦安全方法）
    /// }
    /// </code>
    /// </summary>
    public static class SecurityLogFilterBuilderExtensions
    {
        /// <summary>
        /// 全局注册安全日志采集过滤器——经 CanWeGo 正向白名单仅拦截 AuthController/IPasswordResetFlow 安全方法，
        /// 其余调用零开销跳过（对齐 <c>FilterBuilder.AddAuditLog()</c> 的 opt-in 形态）。
        /// </summary>
        /// <param name="builder">全局过滤器构建器。</param>
        /// <param name="configure">可选：配置过滤器实例（当前无公开可调项，预留扩展）。</param>
        public static FilterBuilder<TUserInfo> AddSecurityLog<TUserInfo>(
            this FilterBuilder<TUserInfo> builder,
            Action<SecurityLogFilterAttribute<TUserInfo>>? configure = null)
            where TUserInfo : class, IUserInfo, new()
        {
            ArgumentNullException.ThrowIfNull(builder);

            var filter = new SecurityLogFilterAttribute<TUserInfo>();
            configure?.Invoke(filter);
            builder.Add(filter);
            return builder;
        }
    }
}
