using TKW.Framework.Domain.Web.Extensions;

namespace TKWF.Ext.AuditLogging
{
    /// <summary>
    /// 审计业务读面 Web 装配扩展——<see cref="DeclareEndpoints"/> 挂载 <see cref="IBusinessAuditWebApiService"/>
    /// 全部 <c>[ApiExpose]</c> 端点（组前缀 = <c>TKWF:Api:RestBasePath</c> 缺省 /api + 模块前缀 audit-logging）。
    /// <para>默认不装配：消费方须显式 <c>UseWebExtensions(e =&gt; e.Add&lt;AuditLoggingWebExtension&gt;())</c>；
    /// 端点均经 <c>.RequireAuthorization()</c>。</para>
    /// </summary>
    public sealed class AuditLoggingWebExtension : IWebExtension
    {
        public void DeclareEndpoints(IExtensionEndpointRegistry registry)
            => registry.MapGenerated<IBusinessAuditWebApiService>();
    }
}
