#nullable enable
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.Analytics;

namespace TKWF.Ext.Analytics;

/// <summary>
/// 分析服务扩展初始化器——经 [TKWFExtension] 被 SG1 发现，三钩子接线：
/// <list type="bullet">
/// <item><see cref="ConfigureServices"/>——注册 <see cref="AnalyticsSpecLoader"/>（Singleton，工厂 lambda 桥接 Options）+ 
///       <see cref="IAnalyticsQueryService"/>（Singleton，无状态门面）+ <see cref="AnalyticsOptions"/> Options 注册</item>
/// <item>ConfigureFilters——不调用（无过滤器）</item>
/// <item>InitializeAsync——不调用（无种子/无持久化）</item>
/// </list>
/// </summary>
[TKWFExtension("Analytics")]
public class AnalyticsExtensionInitializer<TUserInfo> : ExtensionInitializer<TUserInfo>
    where TUserInfo : class, IUserInfo, new()
{
    /// <summary>扩展名称。</summary>
    public override string Name => "Analytics";

    /// <summary>扩展描述。</summary>
    public override string Description => "分析服务扩展——AnalysisSpec 读取/校验/透传（语义层设计期静态产物对齐 flint ChartAssemblyInput；核心加载校验在 TKWF.Utility.Analytics）";

    /// <summary>
    /// 注册分析服务。
    /// <para><see cref="AnalyticsSpecLoader"/> 用工厂 lambda 注册——桥接 <see cref="AnalyticsOptions"/>（派生，[Options] 绑定）
    /// 到 <see cref="AnalyticsSpecOptions"/>（基类，Utility 纯 POCO），因 <c>IOptions&lt;T&gt;</c> 不变性不能直接注入基类（对齐 Metrics C3）。
    /// 注册为 Singleton：Loader 无状态（只读文件系统）+ spec 文件设计期固化，跨请求复用。</para>
    /// <para>TryAdd 语义：消费方可自定义 <see cref="IAnalyticsQueryService"/> 实现，扩展默认实现不覆盖消费方。</para>
    /// </summary>
    public override void ConfigureServices(IServiceCollection services)
    {
        // Options 默认值注册（SG1 [Options] 特性已在消费方自动绑定 TKWF:Analytics 节；此处兜底默认值）
        services.AddOptions<AnalyticsOptions>();

        // C3：工厂 lambda 桥接 AnalyticsOptions → AnalyticsSpecOptions（IOptions<T> 不变性）
        services.TryAddSingleton(sp =>
            new AnalyticsSpecLoader(sp.GetRequiredService<IOptions<AnalyticsOptions>>().Value));

        // 门面（无状态，Singleton）
        services.TryAddSingleton<IAnalyticsQueryService, AnalyticsQueryService>();
    }
}
