using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Core.Hosting;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Hosting;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.Session;
using TKW.Framework.Enumerations;
using TKWF.Ext.WeChatOperations;
using TKWF.Ext.WeChatChannels;
using TKWF.Ext.FeatureManagement;

namespace TKWF.Ext.WeChatOperations.Consumer.Tests;

/// <summary>
/// V0.10.0（WARN009 契约形状纠正）消费方宿主初始化器——DLL 模式模拟消费方（对齐 Permissions.Consumer 先例 +
/// DMP-Lite 引用形态：raw Reference 非传递、引用集缺 MessageGateway.dll）。
/// <para>作用：SG1b 经 <c>ScanHostInitializerUserType</c> 从本类闭合泛型参数推断具体 TUser
/// （<see cref="ConsumerUserInfo"/>），扩展 <c>[GenerateController]</c> 门面在消费方生成控制器记录
/// （GeneratedControllerRegistrations.InterfaceNames）；本测试借类型存在供 SG1/SG2a 编译期识别。</para>
/// <para>V4.9.85 (ADR47)：白名单显式启用 WeChatOperations + WeChatChannels（V0.9.0 拆分——通道面依赖双白名单）。</para>
/// </summary>
[TKWFEnabledExtension(typeof(WeChatOperationsExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(WeChatChannelsExtensionInitializer<>))]
[TKWFEnabledExtension(typeof(FeatureManagementExtensionInitializer<>))]
public sealed class ConsumerHostInitializer : DomainHostInitializerBase<ConsumerUserInfo>
{
    protected override IProjectMetaContext OnRegisterInfrastructureServices(
        IServiceCollection services, IConfiguration? configuration, IDomainHostOptions options)
        => new ConsumerMetaContext();

    protected override DomainUserHelperBase<ConsumerUserInfo> OnRegisterDomainServices(
        IServiceCollection services, IConfiguration? configuration)
        => new ConsumerUserHelper();
}

/// <summary>消费方最小元数据上下文（无业务实体/服务——本测试仅验证扩展门面契约在消费方编译期的形状）。</summary>
public sealed class ConsumerMetaContext : IProjectMetaContext
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
public sealed class ConsumerUserHelper : DomainUserHelperBase<ConsumerUserInfo>
{
    protected override Task<ConsumerUserInfo> OnNewGuestSessionCreatedAsync(SessionInfo session)
        => Task.FromResult(new ConsumerUserInfo("guest", "Guest"));

    protected override Task<ConsumerUserInfo> OnLoginByPasswordAsync(
        DomainUser<ConsumerUserInfo> user, string userName, string credential, EnumLoginFrom loginFrom)
        => Task.FromResult(new ConsumerUserInfo(userName, userName));
}