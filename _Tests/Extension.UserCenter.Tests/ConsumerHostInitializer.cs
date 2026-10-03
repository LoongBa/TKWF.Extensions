using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.UserCenter;

namespace TKWF.Ext.UserCenter.Tests;

/// <summary>
/// V4.9.85 (B2)：消费方宿主初始化器——模拟真实消费方启用 UserCenter 扩展。
/// <para>UC-8：验证 <c>[TKWFEnabledExtension]</c> 白名单装配——声明后三钩子接线，
/// 门面 <see cref="IUserCenterQueryService"/> 自动注册。</para>
/// </summary>
[TKWFEnabledExtension(typeof(UserCenterExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}