using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;
using TKWF.Ext.Metrics;
using TKWF.Ext.Metrics.Tests.Generated;

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// V4.9.85 (B2)：消费方宿主初始化器——模拟真实消费方的 <see cref="DomainHostInitializerBase{TUserInfo}"/> 子类。
/// <para>V4.9.85 (ADR47)：消费方显式启用 Metrics 扩展——<see cref="TKWFEnabledExtensionAttribute"/>
/// 声明后，SG1b 将 Metrics 的能力清单聚合进本消费方领域权威注册（三钩子自动接线）。</para>
/// <para>2026-09-14：元数据上下文改用 SG1 生成的 <see cref="ProjectMetaContext"/>（消费方真实形态——
/// 装载测试实体/DataService 元数据 + ADR61 自动注册；废弃手写 TestMetaContext 空桩）。</para>
/// </summary>
[TKWFEnabledExtension(typeof(MetricsExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => ProjectMetaContext.GetOrCreateInstance();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}

/// <summary>消费方最小用户助手（测试不实际登录，仅满足抽象方法）。</summary>
public sealed class TestUserHelper : DomainUserHelperBase<TestUserInfo>
{
    protected override Task<TestUserInfo> OnNewGuestSessionCreatedAsync(SessionInfo session)
        => Task.FromResult(new TestUserInfo("guest", "Guest"));

    protected override Task<TestUserInfo> OnLoginByPasswordAsync(
        DomainUser<TestUserInfo> user, string userName, string credential, EnumLoginFrom loginFrom)
        => Task.FromResult(new TestUserInfo(userName, userName));
}

/// <summary>消费方最小用户类型——模拟真实消费方定义自己的 UserInfo。</summary>
public class TestUserInfo : SimpleUserInfo
{
    public TestUserInfo() : base() { }

    public TestUserInfo(string userIdString, string userName, params string[] roles)
        : base(userIdString, userName)
    {
        Roles = roles.ToList();
    }
}
