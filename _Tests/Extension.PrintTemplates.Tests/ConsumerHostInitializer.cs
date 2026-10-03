using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;
using TKWF.Ext.PrintTemplates;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.PrintTemplates.Tests;

/// <summary>
/// V4.9.85 (B2)：消费方宿主初始化器——模拟真实消费方的 <see cref="DomainHostInitializerBase{TUserInfo}"/> 子类。
/// <para>V4.9.85 (ADR47)：消费方显式启用 PrintTemplates 扩展——<see cref="TKWFEnabledExtensionAttribute"/>
/// 声明后，SG1b 将 PrintTemplates 的能力清单聚合进消费方的领域权威注册。</para>
/// </summary>
[TKWFEnabledExtension(typeof(PrintTemplatesExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}
