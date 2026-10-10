using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKWF.Ext.Federation;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// V4.9.85 (ADR47)：消费方宿主初始化器——模拟真实消费方的 <see cref="DomainHostInitializerBase{TUserInfo}"/> 子类。
/// <para>白名单声明 <see cref="TKWFEnabledExtensionAttribute"/> 启用 Federation 连接层扩展——三钩子接线
/// （SG1b 聚合能力清单；FederationExtensionInitializer.ConfigureServices 注册 ISsoLogin/ISsoChannelFactory 等
/// 守卫工厂门面，端点冒烟经 RegisterServices 替换 Fake）。</para>
/// <para>元数据上下文：测试项目无 [DomainGenerateCode] 实体 → SG1 不生成 ProjectMetaContext——用最小空桩
/// <see cref="TestMetaContext"/>（对齐 AuthCenter 先例；T3 装配面冒烟以端点行为断言为主，不经完整 DomainHost DI 全图）。</para>
/// </summary>
[TKWFEnabledExtension(typeof(FederationExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}