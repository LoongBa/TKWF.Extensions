using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 特权删除能力注册（C3：能力注册即存在，无配置开关）。
    /// <para>消费方在 <c>ConfigureServices</c> 显式调用 <c>services.AddAuditPrivilegedDelete()</c> 才启用；
    /// 未注册则 <see cref="IAuditPrivilegedDeleteService"/> 不存在（DI 解析失败）。</para>
    /// </summary>
    public static class AuditPrivilegedDeleteExtensions
    {
        /// <summary>注册 <see cref="IAuditPrivilegedDeleteService"/>（域服务门面，经 <c>User.Use&lt;IAuditPrivilegedDeleteService&gt;()</c> 解析）。</summary>
        public static IServiceCollection AddAuditPrivilegedDelete(this IServiceCollection services)
        {
            services.AddConstructibleService<IAuditPrivilegedDeleteService, AuditPrivilegedDeleteService>();
            return services;
        }
    }
}
