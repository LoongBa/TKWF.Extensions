using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.HealthCheck
{
    /// <summary>
    /// 系统健康探测扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="HealthCheckEndpointOptions"/> Options 绑定（TKWF:HealthCheck 节）</item>
    /// <item>ConfigureFilters——不调用（无过滤器）</item>
    /// <item>InitializeAsync——不调用（无种子/无持久化）</item>
    /// </list>
    /// <para><b>无内置探针</b>（Oracle P2-2 裁定）：v0.1.0 不注册任何 <c>IHealthCheck</c>——消费方经
    /// <c>services.AddHealthChecks().AddCheck&lt;T&gt;("name")</c> 注册（数据访问红线规避，见开发方案 §二不包含）；
    /// 端点映射由消费方在 Program.cs 调 <c>app.MapTkfwHealthChecks()</c> 完成。</para>
    /// </summary>
    [TKWFExtension("HealthCheck")]
    public class HealthCheckExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "HealthCheck";

        /// <summary>扩展描述。</summary>
        public override string Description => "系统健康探测扩展——net10 内置 HealthChecks 聚合 + /health 端点映射（接线型，无内置探针，消费方经 AddCheck 注册）";

        /// <summary>
        /// 注册健康检查 Options（<c>TKWF:HealthCheck</c> 节绑定）。
        /// <para>SG1 [Options] 特性已在消费方自动绑定配置节；此处 AddOptions + BindConfiguration 兜底默认值
        /// （对齐 Metrics/Dashboard 初始器模式）。v0.1.0 无 IHealthCheck 注册。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            services.AddOptions<HealthCheckEndpointOptions>().BindConfiguration("TKWF:HealthCheck");
        }
    }
}
