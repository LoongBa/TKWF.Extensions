using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Dashboard
{
    /// <summary>
    /// 仪表盘扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
    /// <list type="bullet">
    /// <item><see cref="ConfigureServices"/>——注册 <see cref="DashboardSpecFileProvider"/>（Singleton，双路径规格加载）+
    ///       <see cref="IDashboardDataService"/>（Scoped，定义查询 + Widget 数据查询）+
    ///       <see cref="DashboardOptions"/> Options 注册（TKWF:Dashboard 节）</item>
    /// <item>ConfigureFilters——不调用（无过滤器）</item>
    /// <item>InitializeAsync——不调用（无种子/无持久化）</item>
    /// </list>
    /// <para>运行期依赖（Oracle C9）：metricRef Widget 需 <see cref="TKW.Framework.Utility.Metrics.IMetricCalculatorFactory"/>
    /// （由 Metrics 扩展注册）——消费方须同时启用 <c>MetricsExtensionInitializer&lt;&gt;</c>；未启用时
    /// <see cref="DashboardDataService"/> 首次 metricRef 计算抛 <see cref="DashboardDefinitionException"/>（fail-fast）。</para>
    /// </summary>
    [TKWFExtension("Dashboard")]
    public class DashboardExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
        where TUserInfo : class, IUserInfo, new()
    {
        /// <summary>扩展名称。</summary>
        public override string Name => "Dashboard";

        /// <summary>扩展描述。</summary>
        public override string Description => "仪表盘数据服务（Metrics 展示层）——JSON 描述符定义 + Widget 数据查询 + 消费 TKWF.Utility.Metrics 复合指标；不引入图表库，UI 渲染归消费方";

        /// <summary>
        /// 注册仪表盘数据服务。
        /// <para>TryAdd 语义：消费方可自定义 <see cref="IDashboardDataService"/> / <see cref="DashboardSpecFileProvider"/> 实现，
        /// 扩展默认实现不覆盖消费方。<see cref="IDashboardDataProvider"/> 由消费方实现并注入（本扩展不注册默认 provider）。</para>
        /// </summary>
        public override void ConfigureServices(IServiceCollection services)
        {
            // Options 默认值注册（SG1 [Options] 特性已在消费方自动绑定 TKWF:Dashboard 节；此处兜底默认值）
            services.AddOptions<DashboardOptions>();

            // 规格文件提供者（双路径加载：Dashboard 定义 + Metrics 指标规格，无状态 → Singleton）
            services.TryAddSingleton<DashboardSpecFileProvider>();

            // 门面（Scoped：按请求组装 Widget 数据）
            services.TryAddScoped<IDashboardDataService, DashboardDataService>();
        }
    }
}
