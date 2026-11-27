using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuditLogging.Web
{
    /// <summary>
    /// 审计业务读面 Web 端点领域初始化器——注册业务审计 Web 门面（域服务，经 <c>User.Use&lt;IBusinessAuditWebApiService&gt;()</c> 解析）。
    /// <para>消费方须与主扩展并行启用：<c>[TKWFEnabledExtension(typeof(AuditLoggingExtensionInitializer&lt;&gt;))]</c>
    /// + <c>[TKWFEnabledExtension(typeof(AuditLoggingWebExtensionInitializer&lt;&gt;))]</c>，并经
    /// <c>UseWebExtensions(e =&gt; e.Add&lt;AuditLoggingWebExtension&gt;())</c> 装配端点。</para>
    /// </summary>
    [TKWFExtension("AuditLogging.Web")]
    public class AuditLoggingWebExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        public override string Name => "AuditLogging.Web";

        public override string Description => "审计业务读面 Web 端点（业务审计受控 REST）";

        public override void ConfigureServices(IServiceCollection services)
            => services.AddConstructibleService<IBusinessAuditWebApiService, BusinessAuditWebApiService>();
    }
}
