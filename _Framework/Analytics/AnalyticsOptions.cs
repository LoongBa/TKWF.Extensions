#nullable enable
using TKW.Framework.Domain;
using TKW.Framework.Utility.Analytics;

namespace TKWF.Ext.Analytics;

/// <summary>
/// 分析扩展配置——经 <see cref="OptionsAttribute"/> 声明配置节，SG1 生成绑定（消费方启动期自动
/// <c>services.Configure&lt;AnalyticsOptions&gt;(configuration.GetSection("TKWF:Analytics"))</c>）。
/// <para>配置节：<c>TKWF:Analytics</c>。继承 Utility 的 <see cref="AnalyticsSpecOptions"/>（扩展层 Options 派生，
/// 工厂 lambda 桥接为基类传入 Loader——对齐 Metrics C3：IOptions&lt;T&gt; 不变性不能直接注入基类）。</para>
/// </summary>
[Options("TKWF:Analytics")]
public class AnalyticsOptions : AnalyticsSpecOptions
{
    // 继承 SpecRoot（默认 "docs/analytics-specs"）；扩展层可在此追加消费方特定配置
}
