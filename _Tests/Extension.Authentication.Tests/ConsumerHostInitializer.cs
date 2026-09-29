using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;
using TKWF.Ext.Authentication;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>
/// V4.9.85 (ADR47)：消费方宿主初始化器——模拟真实消费方的 <see cref="DomainHostInitializerBase{TUserInfo}"/> 子类。
/// <para>白名单声明 <see cref="TKWFEnabledExtensionAttribute"/> 启用认证中心扩展——三钩子接线（SG1b 聚合能力清单）。</para>
/// <para>元数据上下文：测试项目无 [DomainGenerateCode] 实体 → SG1 不生成 ProjectMetaContext——用最小空桩
/// <see cref="TestMetaContext"/>（对齐 Identity 老先例；D14b 消费方生产一致路径验证在消费方装配测试/后续迭代——
/// 认证中心测试以服务单测为主，不经完整 DomainHost DI 全图）。</para>
/// </summary>
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

/// <summary>消费方最小元数据上下文（测试项目无实体——SG1 不生成 ProjectMetaContext；空桩仅满足宿主契约）。</summary>
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
