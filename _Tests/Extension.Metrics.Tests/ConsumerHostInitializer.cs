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

namespace TKWF.Ext.Metrics.Tests;

/// <summary>
/// V4.9.85 (B2)：消费方宿主初始化器——模拟真实消费方的 <see cref="DomainHostInitializerBase{TUserInfo}"/> 子类。
/// <para>V4.9.85 (ADR47)：消费方显式启用 Metrics 扩展——<see cref="TKWFEnabledExtensionAttribute"/>
/// 声明后，SG1b 将 Metrics 的能力清单聚合进本消费方领域权威注册（三钩子自动接线）。</para>
/// </summary>
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

/// <summary>消费方最小元数据上下文（无业务实体/服务——本测试仅验证扩展启用接线）。</summary>
public sealed class TestMetaContext : IProjectMetaContext
{
    public IReadOnlyList<ClassMetadata> AllMetadatas => [];
    public IReadOnlyList<ClassMetadata> Entities => [];
    public IReadOnlyList<ClassMetadata> Views => [];
    public IReadOnlyList<ClassMetadata> Services => [];
    public IReadOnlyList<ClassMetadata> DataServices => [];
    public IReadOnlyList<ClassMetadata> Controllers => [];
    public IReadOnlyList<ClassMetadata> Decorators => [];
    public IReadOnlyList<EnumMetadata> Enums => [];
    public ProjectConfiguration Configuration => null!;
    public MetadataChangeLog ChangeLog => null!;
    public string MetadataSchemaVersion => "1.0";

    public ClassMetadata FindByClassName(string className) => null!;
    public IEnumerable<ClassMetadata> FindByNamespace(string @namespace) => [];
    public IEnumerable<DomainServiceRegistration> GetServiceRegistrations() => [];
    public IEnumerable<EventHandlerRegistration> GetEventHandlerRegistrations() => [];
    public IEnumerable<string> GetTenantScopedEntityClassNames() => [];
    public void ValidateRuntimeGates(RuntimeGateOptions options) { }
    public MethodMetadata? GetMethodMeta(string classFullName, string methodName) => null;
    public IReadOnlyList<object> CreateExtensionInstances() => [];
    public IReadOnlyDictionary<string, PropertyMetadata> GetPropertyMap(string className)
        => new Dictionary<string, PropertyMetadata>();
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
