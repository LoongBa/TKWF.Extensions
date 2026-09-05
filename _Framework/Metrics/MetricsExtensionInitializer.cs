using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.Metrics;

namespace TKWF.Ext.Metrics
{
    /// <summary>
    /// 业务指标计算引擎扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="IMetricsEngine"/>（Singleton，工厂 lambda 桥接 Options）+
    ///       <see cref="IMetricCalculatorFactory"/>（Singleton，静态注册表）+ <see cref="MetricsOptions"/> Options 注册 +
    ///       <see cref="MetricsSpecFileProvider"/>（规格存取）</item>
    /// <item>ConfigureFilters——不调用（无过滤器）</item>
    /// <item>InitializeAsync——不调用（无种子/无持久化）</item>
    /// </list>
    /// </summary>
    [TKWFExtension("Metrics")]
    public class MetricsExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "Metrics";

        /// <summary>扩展描述。</summary>
        public override string Description => "业务指标计算引擎扩展——规格文档驱动的复合业务指标计算（核心计算在 TKWF.Utility.Metrics）";

        /// <summary>
        /// 注册指标引擎服务。
        /// <para>C3+M4：<see cref="IMetricsEngine"/> 用工厂 lambda 注册——桥接 <see cref="MetricsOptions"/>（派生，[Options] 绑定）
        /// 到 <see cref="MetricsEngineOptions"/>（基类，Utility 纯 POCO），因 <c>IOptions&lt;T&gt;</c> 不变性不能直接注入基类；
        /// 注册为 Singleton：引擎无状态 + 定义校验/访问器缓存跨请求复用（对齐 D21 §5.7 "validate once per specKey"）。
        /// <see cref="IMetricCalculatorFactory"/> 亦为 Singleton（静态注册表，计算器无状态）。</para>
        /// <para>TryAdd 语义：消费方可自定义 <see cref="IMetricsEngine"/> / <see cref="IMetricCalculatorFactory"/> 实现，
        /// 扩展默认实现不覆盖消费方。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // Options 默认值注册（SG1 [Options] 特性已在消费方自动绑定 TKWF:Metrics 节；此处兜底默认值）
            services.AddOptions<MetricsOptions>();

            // C3：工厂 lambda 桥接 MetricsOptions → MetricsEngineOptions（IOptions<T> 不变性）
            services.TryAddSingleton<IMetricsEngine>(sp =>
                new MetricsEngine(
                    sp.GetRequiredService<IOptions<MetricsOptions>>().Value,
                    sp.GetRequiredService<IMetricCalculatorFactory>()));

            // 静态注册表（零反射）
            services.TryAddSingleton<IMetricCalculatorFactory, CalculatorFactory>();

            // 规格文件存取（无状态，Singleton）
            services.TryAddSingleton<MetricsSpecFileProvider>();
        }
    }
}
