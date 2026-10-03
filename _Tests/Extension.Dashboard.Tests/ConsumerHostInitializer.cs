using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;
using TKWF.Ext.Dashboard;
using TKWF.Ext.Metrics;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Dashboard.Tests;

/// <summary>
/// V4.9.85 (B2)：消费方宿主初始化器——模拟真实消费方同时启用 Dashboard + Metrics 扩展。
/// <para>Dashboard 的 metricRef Widget 依赖 Metrics 扩展注册 <see cref="IMetricCalculatorFactory"/>
/// （Oracle C9：运行期依赖）——本消费方声明两个扩展验证接线。</para>
/// </summary>
[TKWFEnabledExtension(typeof(DashboardExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(MetricsExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}
