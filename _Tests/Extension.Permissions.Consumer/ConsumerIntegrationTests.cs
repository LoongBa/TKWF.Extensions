using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Permissions.Consumer.Tests;

/// <summary>
/// V0.7.0 (W4)：消费方集成验证——验证扩展在真实消费方形态下工作。
/// <para>覆盖 README §六.4「消费方集成验证」缺口（编译期 InterfaceNames 记录 → 消费方端到端验证）：
/// ① SG1b 在消费方记录扩展 DataService 控制器接口名（<see cref="TKW.Framework.SourceGen.GeneratedControllerRegistrations"/>）；
/// ② 消费方 <see cref="ConsumerPermissionContributor"/> 经扩展 ConfigureServices 被收集（FakeMetaContext 模式，
///    镜像主框架 Navigation 测试 L3 静态隔离）；
/// ③ 三钩子接线：ConfigureServices 注册服务 + ConfigureFilters 注册过滤器。</para>
/// <para>注：完整 REST 控制器生成需消费方 ApiService.SG (SG#2) + Web 宿主管线，超出本仓库扩展测试范围
/// （README §五.4 注明控制器由消费方 SG#2 生成）。本测试验证扩展侧在消费方编译期的接线契约。</para>
/// </summary>
public class ConsumerIntegrationTests
{
    // ─── 1. SG1b 编译期发现：扩展 DataService 控制器接口名在消费方记录 ───

    /// <summary>
    /// 消费方项目激活 SG1b（csproj Analyzer 引用）→ 扩展 <c>[GenerateController(FromDataService=true)]</c>
    /// 的 DataService 接口名被记录进 <see cref="TKW.Framework.SourceGen.GeneratedControllerRegistrations"/>。
    /// 证明扩展控制器生成契约在消费方编译期生效（SG#2 依此生成 REST API）。
    /// </summary>
    [Fact]
    public void Sg1b_InterfaceNames_IncludesPermissionDataService()
    {
        const string expectedIface = "TKWF.Ext.Permissions.Controllers.IPermissionGrantEntityDataService";
        Assert.Contains(expectedIface, TKW.Framework.SourceGen.GeneratedControllerRegistrations.InterfaceNames);
    }

    // ─── 2. 三钩子接线：ConfigureServices 收集消费方贡献者定义 + 注册服务 ───

    [Fact]
    public void ConfigureServices_CollectsConsumerContributorDefinitions()
    {
        var original = ProjectMetaContextBase.Instance;
        try
        {
            // 安装含消费方贡献者的元数据上下文（模拟 SG1 生成的 ProjectMetaContext）→ 扩展初始化器读取
            FakeConsumerMetaContext.Install();

            var services = new ServiceCollection();
            new PermissionExtensionInitializer<ConsumerUserInfo>().ConfigureServices(services);

            var sp = services.BuildServiceProvider();
            var repository = sp.GetService<IPermissionDefinitionRepository>();
            Assert.NotNull(repository);

            var names = repository!.GetAll().Select(d => d.Name).ToArray();
            Assert.Contains("Order.Create", names);
            Assert.Contains("Order.Delete", names);

            // 服务已注册（默认 NoOp store + PermissionChecker + RoleProvider）
            Assert.NotNull(sp.GetService<IPermissionChecker>());
            Assert.NotNull(sp.GetService<IRoleProvider<ConsumerUserInfo>>());
        }
        finally
        {
            FakeConsumerMetaContext.Restore(original);
        }
    }

    [Fact]
    public void ConfigureServices_TryAddScoped_DoesNotOverrideConsumerChecker()
    {
        var original = ProjectMetaContextBase.Instance;
        try
        {
            FakeConsumerMetaContext.Install();

            var services = new ServiceCollection();
            // 消费方先注册自定义 IPermissionChecker → TryAddScoped 不应覆盖
            services.AddScoped<IPermissionChecker, ConsumerGrantAllChecker>();
            new PermissionExtensionInitializer<ConsumerUserInfo>().ConfigureServices(services);

            var sp = services.BuildServiceProvider();
            Assert.IsType<ConsumerGrantAllChecker>(sp.GetRequiredService<IPermissionChecker>());
        }
        finally
        {
            FakeConsumerMetaContext.Restore(original);
        }
    }

    // ─── 3. ConfigureFilters：注册 PermissionFilter 到 Tier-S ───

    [Fact]
    public void ConfigureFilters_RegistersPermissionFilter()
    {
        var builder = new FilterBuilder<ConsumerUserInfo>();
        new PermissionExtensionInitializer<ConsumerUserInfo>().ConfigureFilters(builder);

        Assert.Contains(builder.Filters, f => f.GetType() == typeof(PermissionFilterAttribute<ConsumerUserInfo>));
    }

    // ─── 测试基础设施 ───

    /// <summary>
    /// 测试用 ProjectMetaContext——override PermissionContributors 返回消费方贡献者清单。
    /// <c>ProjectMetaContextBase.Instance</c> setter 是 protected，经子类公开 Install/Restore。
    /// 静态隔离约束（镜像 Permissions/Navigation 测试）：进程级单例，try/finally 恢复。
    /// </summary>
    private sealed class FakeConsumerMetaContext : ProjectMetaContextBase
    {
        public static void Install() => Instance = new FakeConsumerMetaContext();

        public static void Restore(IProjectMetaContext? original) => Instance = original;

        public override IReadOnlyList<PermissionContributorData> PermissionContributors =>
            new[]
            {
                new PermissionContributorData(
                    "TKWF.Ext.Permissions.Consumer.Tests.ConsumerPermissionContributor",
                    "ConsumerPermissionContributor",
                    typeof(ConsumerPermissionContributor))
            };

        public override ProjectConfiguration Configuration => null!;
        public override MetadataChangeLog ChangeLog => null!;
        public override string MetadataSchemaVersion => "test";
    }

    /// <summary>测试专用 IPermissionChecker：标记消费方自定义实现（TryAdd 不被覆盖）。</summary>
    private sealed class ConsumerGrantAllChecker : IPermissionChecker
    {
        public Task<bool> IsGrantedAsync(string permissionName) => Task.FromResult(true);

        public Task<Dictionary<string, bool>> IsGrantedAsync(params string[] permissionNames)
            => Task.FromResult(permissionNames.ToDictionary(n => n, _ => true));
    }
}

