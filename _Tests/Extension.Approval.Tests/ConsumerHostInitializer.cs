using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;
using TKWF.Ext.Approval;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.Approval.Tests;

/// <summary>
/// 消费方宿主初始化器——模拟真实消费方的 DomainHostInitializerBase 子类。
/// <para>V4.9.85 (ADR47)：消费方显式启用 Approval 扩展——TKWFEnabledExtensionAttribute 声明后三钩子自动接线。</para>
/// </summary>
[TKWFEnabledExtension(typeof(ApprovalExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}
