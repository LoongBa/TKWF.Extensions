using System;
using System.Linq;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKWF.Ext.Navigation;

namespace TKWF.Ext.Navigation.Tests;

/// <summary>
/// V4.9.74 (W6): NavigationExtensionInitializer 三钩子测试。
/// 覆盖 ConfigureServices（贡献者收集 + DI 注册）、ConfigureFilters（空）、InitializeAsync（空）。
/// <para>静态隔离约束（镜像 Permissions L3）：FakeMetaContext 经 Install/Restore 操作进程级
/// <c>ProjectMetaContextBase.Instance</c>（try/finally 保证恢复）；xunit 每程序集独立 testhost 保证当前安全。</para>
/// </summary>
public class NavigationExtensionInitializerTests
{
    // ─── ConfigureServices：贡献者收集 + DI 注册 ───

    [Fact]
    public void ConfigureServices_CollectsContributors_RegistersServices()
    {
        var original = ProjectMetaContextBase.Instance;
        try
        {
            FakeMetaContext.Install(new FakeMetaContext());

            var services = new ServiceCollection();
            var initializer = new NavigationExtensionInitializer<SimpleUserInfo>();
            initializer.ConfigureServices(services);

            // 1. 两服务注册
            Assert.Contains(services, s => s.ServiceType == typeof(IMenuManager));
            Assert.Contains(services, s => s.ServiceType == typeof(IMenuDefinitionRepository));

            // 2. 收集到的菜单项注入仓库（经 DI 解析验证）
            var sp = services.BuildServiceProvider();
            var repo = sp.GetService<IMenuDefinitionRepository>();
            Assert.NotNull(repo);
            Assert.Contains(repo!.GetAll(), m => m.Name == "Orders");

            var manager = sp.GetService<IMenuManager>();
            Assert.NotNull(manager);
        }
        finally
        {
            FakeMetaContext.Restore(original);
        }
    }

    [Fact]
    public void ConfigureServices_NoContributors_RegistersEmptyRepo()
    {
        var original = ProjectMetaContextBase.Instance;
        try
        {
            FakeMetaContext.Install(new FakeMetaContext(empty: true));

            var services = new ServiceCollection();
            new NavigationExtensionInitializer<SimpleUserInfo>().ConfigureServices(services);

            var sp = services.BuildServiceProvider();
            var repo = sp.GetService<IMenuDefinitionRepository>();
            Assert.NotNull(repo);
            Assert.Empty(repo!.GetAll());
        }
        finally
        {
            FakeMetaContext.Restore(original);
        }
    }

    // ─── ConfigureFilters：空（菜单不注册 AOP 过滤器）───

    [Fact]
    public void ConfigureFilters_NoFilterRegistered()
    {
        var builder = new FilterBuilder<SimpleUserInfo>();
        builder.AddCoreDefaults();
        new NavigationExtensionInitializer<SimpleUserInfo>().ConfigureFilters(builder);

        // 仅 AddCoreDefaults 的 ValidateParameters/Authority，无 Navigation 过滤器
        Assert.Equal(2, builder.Filters.Count);
    }

    [Fact]
    public void InitializeAsync_CompletesImmediately()
    {
        var initializer = new NavigationExtensionInitializer<SimpleUserInfo>();
        var task = initializer.InitializeAsync();
        Assert.True(task.IsCompleted);
    }

    // ─── 测试基础设施 ───

    /// <summary>测试用 ProjectMetaContext——override MenuContributors 返回固定清单。</summary>
    private sealed class FakeMetaContext : ProjectMetaContextBase
    {
        private readonly bool _empty;

        public FakeMetaContext(bool empty = false) => _empty = empty;

        public static void Install(FakeMetaContext ctx) => Instance = ctx;

        public static void Restore(IProjectMetaContext? original) => Instance = original;

        public override IReadOnlyList<MenuContributorData> MenuContributors =>
            _empty
                ? Array.Empty<MenuContributorData>()
                : new[]
                {
                    new MenuContributorData(
                        "TKWF.Ext.Navigation.Tests.MainMenuContributor",
                        "MainMenuContributor",
                        typeof(MainMenuContributor))
                };

        public override ProjectConfiguration Configuration => null!;
        public override MetadataChangeLog ChangeLog => null!;
        public override string MetadataSchemaVersion => "test";
    }

    [MenuContributor]
    public sealed class MainMenuContributor : IMenuContributor
    {
        public void ConfigureMenu(MenuConfigurationContext context)
            => context.Add(new MenuItemDefinition { Name = "Orders", DisplayName = "订单", Url = "/orders" });
    }
}