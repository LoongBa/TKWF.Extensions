using System;
using System.Linq;
using TKWF.Ext.Navigation;

namespace TKWF.Ext.Navigation.Tests;

/// <summary>
/// V4.9.74 (W6): 菜单定义 + 上下文 + 仓库行为测试。
/// </summary>
public class MenuDefinitionTests
{
    // ─── MenuConfigurationContext.Add ───

    [Fact]
    public void Context_Add_CollectsMenuItem()
    {
        var ctx = new MenuConfigurationContext();
        ctx.Add(new MenuItemDefinition { Name = "Orders", DisplayName = "订单", Url = "/orders" });
        ctx.Add(new MenuItemDefinition { Name = "Settings", DisplayName = "设置" });

        Assert.Equal(2, ctx.MenuItems.Count);
        Assert.Equal("订单", ctx.MenuItems[0].DisplayName);
        Assert.Equal("/orders", ctx.MenuItems[0].Url);
    }

    [Fact]
    public void Context_Add_NullItem_Throws()
    {
        var ctx = new MenuConfigurationContext();
        Assert.Throws<ArgumentNullException>(() => ctx.Add(null!));
    }

    [Fact]
    public void Context_Add_EmptyName_Throws()
    {
        var ctx = new MenuConfigurationContext();
        Assert.Throws<ArgumentNullException>(() => ctx.Add(new MenuItemDefinition()));
    }

    [Fact]
    public void Context_Add_DuplicateName_Throws()
    {
        var ctx = new MenuConfigurationContext();
        ctx.Add(new MenuItemDefinition { Name = "Orders" });

        Assert.Throws<InvalidOperationException>(() => ctx.Add(new MenuItemDefinition { Name = "Orders" }));
    }

    // ─── MenuDefinitionRepository（经 IMenuDefinitionRepository 接口验证）───

    [Fact]
    public void Repository_AddRange_Deduplicates()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new[]
        {
            new MenuItemDefinition { Name = "A" },
            new MenuItemDefinition { Name = "B" },
            new MenuItemDefinition { Name = "A" }, // 重复
            new MenuItemDefinition { Name = "C" }
        });

        Assert.Equal(3, repo.GetAll().Count);
    }

    [Fact]
    public void Repository_AddRange_NullEntries_Skipped()
    {
        var repo = new MenuDefinitionRepository();
        repo.AddRange(new MenuItemDefinition[] { null!, new MenuItemDefinition { Name = "A" } });

        Assert.Single(repo.GetAll());
    }
}