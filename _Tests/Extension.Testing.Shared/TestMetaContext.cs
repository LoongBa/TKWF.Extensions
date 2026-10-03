using TKW.Framework.CodeGeneration;

namespace TKWF.Ext.Testing.Shared;

/// <summary>
/// 消费方最小元数据上下文（无业务实体/服务——测试宿主无实体元数据时的空桩）。
/// C 基座公共类（2026-10-04）：统一 25+ 个测试项目的标准 <c>TestMetaContext</c> 空桩定义。
/// ⚠️ Metrics 例外：测试宿主经 SG1 生成的 <c>ProjectMetaContext</c>（消费方真实形态，含 ADR61 DataService
/// 自动注册）——该测试项目**不用**本空桩（2026-09-14 生成物健康清单 #6：禁手写 IProjectMetaContext 空桩）。
/// </summary>
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