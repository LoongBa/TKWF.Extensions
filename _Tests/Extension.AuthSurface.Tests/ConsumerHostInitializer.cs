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
using TKWF.Ext.AuthCenter;

namespace TKWF.Ext.AuthSurface.Tests;

/// <summary>
/// V4.9.85 (B2)：消费方宿主初始化器——模拟真实消费方**同时启用 AuthSurface + AuthCenter** 扩展
/// （跨扩展双白名单——对齐 Account.Tests Account+SecurityLog 先例）。
/// <para>作用：
/// ① SG1b 经 <c>ScanHostInitializerUserType</c> 从此类闭合泛型参数推断具体 TUser（<see cref="TestUserInfo"/>）；
/// ② 白名单声明后三钩子接线（AuthSurface 4 门面 + AuthCenter 实体/VEntity DataService 自动注册）。</para>
/// <para>⚠️ L3 门控（TKWF0032）：本测试项目直接引用 AuthCenter（vm 视图 JOIN 基表数据造数）——
/// 须对每个直接引用的扩展声明白名单，故双特性堆叠（Account.Tests L25-26 先例）。</para>
/// </summary>
[TKWFEnabledExtension(typeof(AuthSurfaceExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(AuthCenterExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}
