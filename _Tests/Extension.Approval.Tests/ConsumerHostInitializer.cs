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

/// <summary>消费方最小元数据上下文。</summary>
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

/// <summary>消费方最小用户助手。</summary>
public sealed class TestUserHelper : DomainUserHelperBase<TestUserInfo>
{
    protected override Task<TestUserInfo> OnNewGuestSessionCreatedAsync(SessionInfo session)
        => Task.FromResult(new TestUserInfo("guest", "Guest"));

    protected override Task<TestUserInfo> OnLoginByPasswordAsync(
        DomainUser<TestUserInfo> user, string userName, string credential, EnumLoginFrom loginFrom)
        => Task.FromResult(new TestUserInfo(userName, userName));
}

/// <summary>消费方最小用户类型。</summary>
public class TestUserInfo : SimpleUserInfo
{
    public TestUserInfo() : base() { }
    public TestUserInfo(string userIdString, string userName, params string[] roles)
        : base(userIdString, userName)
    {
        Roles = roles.ToList();
    }
}
