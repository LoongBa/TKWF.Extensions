using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;
using TKWF.Ext.BackgroundJobs;
using TKWF.Ext.Testing.Shared;

namespace TKWF.Ext.BackgroundJobs.Tests;

/// <summary>
/// V4.9.85 (B2)：消费方宿主初始化器——模拟真实消费方的 <see cref="DomainHostInitializerBase{TUserInfo}"/> 子类。
/// <para>作用：
/// ① SG1b 经 <c>ScanHostInitializerUserType</c> 从此类闭合泛型参数推断具体 TUser（<see cref="TestUserInfo"/>）；
/// ② 消费方侧启动接线（本测试不真正跑宿主，仅借其类型存在供 SG1 编译期识别）。</para>
/// <para>V4.9.85 (ADR47)：消费方显式启用 BackgroundJobs 扩展——<see cref="TKWFEnabledExtensionAttribute"/>
/// 声明后，SG1b 将 BackgroundJobs 的能力清单聚合进本消费方的领域权威注册。</para>
/// </summary>
[TKWFEnabledExtension(typeof(BackgroundJobsExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}
