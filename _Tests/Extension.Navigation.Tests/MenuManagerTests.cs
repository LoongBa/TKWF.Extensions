using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
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

    // ─── 基础设施 ───

    private static MenuManager<SimpleUserInfo> CreateManager(IMenuDefinitionRepository repo, IPermissionChecker? checker)
    {
        var services = new ServiceCollection();
        if (checker != null)
            services.AddSingleton(checker);
        var sp = services.BuildServiceProvider();
        return new MenuManager<SimpleUserInfo>(repo, sp);
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