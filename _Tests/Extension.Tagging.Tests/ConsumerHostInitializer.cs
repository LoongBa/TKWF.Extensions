using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;
using TKWF.Ext.Tagging;

namespace TKWF.Ext.Tagging.Tests;

/// <summary>
/// V4.9.85 (B2)：消费方宿主初始化器——模拟真实消费方的 <see cref="DomainHostInitializerBase{TUserInfo}"/> 子类。
/// <para>作用：
/// ① SG1b 经 <c>ScanHostInitializerUserType</c> 从此类闭合泛型参数推断具体 TUser（<see cref="TestUserInfo"/>），
///    从而为扩展的 <c>[GenerateController(FromDataService=true)]</c> DataService 在消费方生成控制器；
/// ② 消费方侧启动接线（本测试不真正跑宿主，仅借其类型存在供 SG1 编译期识别）。</para>
/// <para>V4.9.85 (ADR47)：消费方显式启用 Tagging 扩展——<see cref="TKWFEnabledExtensionAttribute"/>
/// 声明后，SG1b 将 Tagging 的能力清单（GeneratedControllerCatalog）聚合进本消费方的
/// 领域权威注册（GeneratedControllerRegistrations.InterfaceNames），扩展服务接口才能在消费方暴露。</para>
/// </summary>
[TKWFEnabledExtension(typeof(TaggingExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<TestUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new TestMetaContext();

    protected override DomainUserHelperBase<TestUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new TestUserHelper();
}

/// <summary>消费方最小元数据上下文（无业务实体/服务——本测试仅验证扩展控制器生成）。</summary>
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
    /// <summary>V4.9.85 (ADR48 D4): 消费方测试无扩展初始化器实例——返回空。</summary>
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
