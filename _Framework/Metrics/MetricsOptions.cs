using TKW.Framework.Domain;
using TKW.Framework.Utility.Metrics;

namespace TKWF.Ext.Metrics
{
    /// <summary>
    /// 指标扩展配置——经 <see cref="OptionsAttribute"/> 声明配置节，SG1 生成绑定（消费方启动期自动
    /// <c>services.Configure&lt;MetricsOptions&gt;(configuration.GetSection("TKWF:Metrics"))</c>）。
    /// <para>配置节：<c>TKWF:Metrics</c>。执行参数（超时/失败行为/对齐）继承 Utility 的
    /// <see cref="MetricsEngineOptions"/>（C3：扩展层 Options 派生，工厂 lambda 桥接为基类传入引擎）。</para>
    /// </summary>
    [Options("TKWF:Metrics")]
    public class MetricsOptions : MetricsEngineOptions
    {
        /// <summary>规格文件根目录（默认 "docs/analytics-specs"，相对消费方仓库根；规格存取接入）</summary>
        public string SpecRoot { get; set; } = "docs/analytics-specs";
    }
}
