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
using TKWF.Ext.AuthSurface;

namespace TKWF.Ext.AuthSurface.Tests;

/// <summary>
/// V4.9.85 (B2)：消费方宿主初始化器——模拟真实消费方启用 AuthSurface 扩展。
/// <para>作用：
/// ① SG1b 经 <c>ScanHostInitializerUserType</c> 从此类闭合泛型参数推断具体 TUser（<see cref="TestUserInfo"/>）；
/// ② 白名单声明后三钩子接线（AuthSurface 4 门面 + 实体/VEntity DataService 自动注册）。</para>
/// <para>⚠️ AuthCenter 暂不双白名单——其源码处 V0.9.0 身份域重构进行中（半成品）；vm 视图 JOIN 基表由
/// 测试宿主原生 SQL 建表模拟（AuthSurfaceTestHost.SyncSchema）。AuthCenter V0.9.0 完成后补
/// <c>[TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer&lt;&gt;))]</c> 双扩展集成（Oracle4 P1-2 契约）。</para>
/// </summary>
[TKWFEnabledExtension(typeof(AuthSurfaceExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}
