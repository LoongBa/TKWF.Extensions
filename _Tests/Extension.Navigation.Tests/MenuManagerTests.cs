using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using TKWF.Ext.Navigation;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.Navigation.Tests;

/// <summary>
/// V4.9.74 (W6): MenuManager 行为测试——树形组装/排序/权限过滤 All-Any/循环检测/checker 降级。
/// </summary>
public class MenuManagerTests
{
    // ─── 树形组装 + 排序 ───

    [Fact]
    public async Task GetMenu_Flattened_DepthThenOrder()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "Child", Parent = "Root", Order = 1 },
            new MenuItemDefinition { Name = "Root", Order = 0 },
            new MenuItemDefinition { Name = "OtherRoot", Order = 1 }
        });
        var manager = CreateManager(repo, checker: null);

        var result = await manager.GetMainMenuAsync();

        // 深度优先：Root(0) → OtherRoot(0) → Child(1)
        Assert.Equal(new[] { "Root", "OtherRoot", "Child" }, result.Select(i => i.Name));
    }

    [Fact]
    public async Task GetMenu_OrphanParent_FlattenedToTop()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "Orphan", Parent = "NotExist" }
        });
        var manager = CreateManager(repo, checker: null);

        var result = await manager.GetMainMenuAsync();

        Assert.Single(result);
        Assert.Equal("Orphan", result[0].Name);
    }

    // ─── 权限过滤 All / Any ───

    [Fact]
    public async Task GetMenu_AllLogic_AllGranted_Visible()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "A", RequiredPermissions = new[] { "P1", "P2" } } // Logic 默认 All
        });
        var checker = new FakePermissionChecker(new Dictionary<string, bool> { ["P1"] = true, ["P2"] = true });
        var manager = CreateManager(repo, checker);

        var result = await manager.GetMainMenuAsync();

        Assert.Single(result);
    }

    [Fact]
    public async Task GetMenu_AllLogic_OneDenied_Hidden()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "A", RequiredPermissions = new[] { "P1", "P2" } }
        });
        var checker = new FakePermissionChecker(new Dictionary<string, bool> { ["P1"] = true, ["P2"] = false });
        var manager = CreateManager(repo, checker);

        var result = await manager.GetMainMenuAsync();

        Assert.Empty(result);
    }

    [Fact]
    public async Task GetMenu_AnyLogic_OneGranted_Visible()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition
            {
                Name = "A",
                RequiredPermissions = new[] { "P1", "P2" },
                Logic = PermissionLogic.Any
            }
        });
        var checker = new FakePermissionChecker(new Dictionary<string, bool> { ["P1"] = false, ["P2"] = true });
        var manager = CreateManager(repo, checker);

        var result = await manager.GetMainMenuAsync();

        Assert.Single(result);
    }

    [Fact]
    public async Task GetMenu_AnyLogic_NoneGranted_Hidden()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition
            {
                Name = "A",
                RequiredPermissions = new[] { "P1", "P2" },
                Logic = PermissionLogic.Any
            }
        });
        var checker = new FakePermissionChecker(new Dictionary<string, bool> { ["P1"] = false, ["P2"] = false });
        var manager = CreateManager(repo, checker);

        var result = await manager.GetMainMenuAsync();

        Assert.Empty(result);
    }

    // ─── 无 RequiredPermissions / 禁用 / 不可见 ───

    [Fact]
    public async Task GetMenu_NoRequiredPermissions_AlwaysVisible()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[] { new MenuItemDefinition { Name = "A" } });
        var manager = CreateManager(repo, checker: null);

        var result = await manager.GetMainMenuAsync();

        Assert.Single(result);
    }

    [Fact]
    public async Task GetMenu_Disabled_NotVisible()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "A", IsEnabled = false },
            new MenuItemDefinition { Name = "B", IsVisible = false },
            new MenuItemDefinition { Name = "C" }
        });
        var manager = CreateManager(repo, checker: null);

        var result = await manager.GetMainMenuAsync();

        Assert.Single(result);
        Assert.Equal("C", result[0].Name);
    }

    // ─── checker 缺失降级 ───

    [Fact]
    public async Task GetMenu_CheckerNotRegistered_NoFiltering()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "A", RequiredPermissions = new[] { "P1" } }
        });
        // checker: null → 降级不过滤
        var manager = CreateManager(repo, checker: null);

        var result = await manager.GetMainMenuAsync();

        Assert.Single(result);
    }

    // ─── 循环检测 ───

    [Fact]
    public async Task GetMenu_CyclicParent_Throws()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "A", Parent = "B" },
            new MenuItemDefinition { Name = "B", Parent = "A" }
        });
        var manager = CreateManager(repo, checker: null);

        await Assert.ThrowsAsync<InvalidOperationException>(() => manager.GetMainMenuAsync());
    }

    // ─── V0.2.0 多菜单分区 ───

    [Fact]
    public async Task GetMenu_MultiMenu_FiltersByMenuName()
    {
        // Main + Admin 双菜单：GetMenuAsync("Admin") 只含 Admin 项，GetMainMenuAsync 只含 Main 项
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "Orders", MenuName = "Main" },          // 主菜单
            new MenuItemDefinition { Name = "Admin.Users", MenuName = "Admin" },    // 管理菜单
            new MenuItemDefinition { Name = "System", MenuName = "Admin", Order = 1 }
        });
        var manager = CreateManager(repo, checker: null);

        var admin = await manager.GetMenuAsync("Admin");
        var main = await manager.GetMainMenuAsync();

        Assert.Equal(new[] { "Admin.Users", "System" }, admin.Select(i => i.Name));
        Assert.Equal(new[] { "Orders" }, main.Select(i => i.Name));
    }

    [Fact]
    public async Task GetMenu_DefaultMenuName_IsMain()
    {
        // 既有单菜单（未设 MenuName）→ 默认 "Main"，GetMainMenuAsync 返回全部项（零迁移）
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "A" },
            new MenuItemDefinition { Name = "B" }
        });
        var manager = CreateManager(repo, checker: null);

        var result = await manager.GetMainMenuAsync();

        Assert.Equal(2, result.Length);
        Assert.All(result, i => Assert.Equal("Main", i.MenuName));
    }

    [Fact]
    public async Task GetMenu_DefaultMenuName_Configured_ReturnsConfiguredMenu()
    {
        // 配置 DefaultMenuName = "Admin" → GetMainMenuAsync 返回 Admin 菜单
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "Orders", MenuName = "Main" },
            new MenuItemDefinition { Name = "Admin.Users", MenuName = "Admin" }
        });
        var manager = CreateManager(repo, checker: null, defaultMenuName: "Admin");

        var result = await manager.GetMainMenuAsync();

        Assert.Equal(new[] { "Admin.Users" }, result.Select(i => i.Name));
    }

    [Fact]
    public async Task GetMenu_CrossMenuParent_TreatedAsTopLevel()
    {
        // Oracle 条件 3：Admin 项 Parent 指向 Main 项 → scoped 过滤后 byName 不含跨菜单项 →
        // 深度计算视为顶层（depth 0，不抛异常）。Parent 字符串保留（前端建树跨菜单解析不到即拉平）。
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "Root", MenuName = "Main" },
            new MenuItemDefinition { Name = "Admin.Child", MenuName = "Admin", Parent = "Root" }
        });
        var manager = CreateManager(repo, checker: null);

        var admin = await manager.GetMenuAsync("Admin");
        var main = await manager.GetMainMenuAsync();

        // 不抛异常 + 正常返回：Admin.Child 在 Admin 菜单（Parent 字符串保留，深度按顶层处理）
        Assert.Single(admin);
        Assert.Equal("Admin.Child", admin[0].Name);
        Assert.Equal("Root", admin[0].Parent);          // Parent 保留（前端解析不到即拉平）
        Assert.Single(main);                            // Main 不受影响
        Assert.Equal("Root", main[0].Name);
    }

    [Fact]
    public async Task GetMenu_PermissionFilter_IndependentPerMenu()
    {
        // 权限过滤在多菜单内独立生效：Admin 项权限过滤不影响 Main（checker 存在时）
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "Orders", MenuName = "Main" },
            new MenuItemDefinition { Name = "Admin.Users", MenuName = "Admin", RequiredPermissions = new[] { "Admin.Manage" } },
            new MenuItemDefinition { Name = "Admin.Audit", MenuName = "Admin", RequiredPermissions = new[] { "Audit.View" } }
        });
        var checker = new FakePermissionChecker(new Dictionary<string, bool> { ["Admin.Manage"] = true });   // 仅 Admin.Manage 授予
        var manager = CreateManager(repo, checker);

        var admin = await manager.GetMenuAsync("Admin");
        var main = await manager.GetMainMenuAsync();

        Assert.Equal(new[] { "Admin.Users" }, admin.Select(i => i.Name));   // Audit.View 未授予 → 隐藏
        Assert.Equal(new[] { "Orders" }, main.Select(i => i.Name));         // Main 项无权限要求 → 始终显示
    }

    [Fact]
    public async Task GetMenu_NoContributorsForMenu_ReturnsEmpty()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "Orders", MenuName = "Main" }
        });
        var manager = CreateManager(repo, checker: null);

        var mobile = await manager.GetMenuAsync("Mobile");

        Assert.Empty(mobile);
    }

    [Fact]
    public async Task GetMenu_NullOrEmptyMenuName_Throws()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[] { new MenuItemDefinition { Name = "Orders", MenuName = "Main" } });
        var manager = CreateManager(repo, checker: null);

        await Assert.ThrowsAsync<ArgumentException>(() => manager.GetMenuAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => manager.GetMenuAsync(""));
    }

    // ─── 基础设施 ───

    private static MenuManager<SimpleUserInfo> CreateManager(IMenuDefinitionRepository repo, IPermissionChecker? checker,
        string? defaultMenuName = null)   // V0.2.0：可注入 DefaultMenuName（多菜单测试用）
    {
        var services = new ServiceCollection();
        if (checker != null)
            services.AddSingleton(checker);
        var sp = services.BuildServiceProvider();
        var options = Options.Create(new NavigationOptions
        {
            DefaultMenuName = defaultMenuName ?? "Main"
        });
        return new MenuManager<SimpleUserInfo>(repo, sp, options);
    }

    private sealed class FakePermissionChecker : IPermissionChecker
    {
        private readonly Dictionary<string, bool> _grants;
        public FakePermissionChecker(Dictionary<string, bool> grants) => _grants = grants;

        public Task<bool> IsGrantedAsync(string permissionName)
            => Task.FromResult(_grants.GetValueOrDefault(permissionName, false));

        public Task<Dictionary<string, bool>> IsGrantedAsync(params string[] permissionNames)
        {
            var result = new Dictionary<string, bool>();
            foreach (var n in permissionNames)
                result[n] = _grants.GetValueOrDefault(n, false);
            return Task.FromResult(result);
        }
    }
}