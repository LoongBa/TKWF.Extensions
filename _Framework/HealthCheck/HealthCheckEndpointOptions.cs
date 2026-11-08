using TKW.Framework.Domain;

namespace TKWF.Ext.HealthCheck
{
    /// <summary>
    /// 健康检查端点配置——经 <see cref="OptionsAttribute"/> 声明配置节，SG1 生成绑定（消费方启动期自动
    /// <c>services.Configure&lt;HealthCheckEndpointOptions&gt;(configuration.GetSection("TKWF:HealthCheck"))</c>）。
    /// <para>配置节：<c>TKWF:HealthCheck</c>。</para>
    /// <para>⚠️ 命名 <see cref="HealthCheckEndpointOptions"/>（非 HealthCheckOptions）——避免与 ASP.NET Core 内置
    /// <c>Microsoft.AspNetCore.Diagnostics.HealthChecks.HealthCheckOptions</c> 类型名冲突（Oracle P1-1 裁定）。</para>
    /// </summary>
    [Options("TKWF:HealthCheck")]
    public sealed class HealthCheckEndpointOptions
    {
        /// <summary>健康检查端点路径（默认 /health——D04 框架生命线已豁免认证）。</summary>
        public string Path { get; set; } = "/health";

        /// <summary>是否启用健康检查端点（默认 true；false 时不映射端点）。</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>（已删功能残留——v0.3.0 起无组件级输出）是否返回详细诊断。框架不读此键：Detailed 组件级输出已随方案 A（f8425d4）移除，MapHealthCheckCore 恒用摘要 writer，设置不再生效。</summary>
        public bool Detailed { get; set; } = false;

        /// <summary>端点是否匿名可访问（默认 true——健康探测无需认证；与 D04 /health /healthz 豁免一致；false 时遵循应用认证管线）。</summary>
        public bool AllowAnonymous { get; set; } = true;
    }
}
