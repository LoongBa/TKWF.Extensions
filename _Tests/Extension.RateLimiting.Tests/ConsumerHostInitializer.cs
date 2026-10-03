using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;
using TKWF.Ext.RateLimiting;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.RateLimiting.Tests;

/// <summary>
/// V4.9.85 (B2)：消费方宿主初始化器——模拟真实消费方启用 RateLimiting 扩展。
/// <para>限流中间件接线由 Web 装配钩子完成（v4.10.45 收敛迁移：消费方 <c>UseWebExtensions</c> 装配
/// <c>RateLimitingWebExtension</c>——旧 AddTkfwRateLimiting 显式调用已删除）；本消费方仅验证
/// 扩展发现 + Options 绑定（TKWF:RateLimiting 节）三钩子接线。</para>
/// </summary>
[TKWFEnabledExtension(typeof(RateLimitingExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}