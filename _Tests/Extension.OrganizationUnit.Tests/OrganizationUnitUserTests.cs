using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKWF.Ext.OrganizationUnit;

namespace TKWF.Ext.OrganizationUnit.Tests;

/// <summary>
/// OrganizationUnit 用户关联测试——D11-D13（分配/唯一防重/解除、子树用户列表、用户所属 OU 列表）+ D18（完整链路补漏）。
/// <para>每用例独立 SQLite 内存库（<see cref="OrganizationUnitTestHost.Create"/>）。</para>
/// </summary>
public class OrganizationUnitUserTests
{
    private static OrganizationUnitTestHost NewHost() => OrganizationUnitTestHost.Create();

    /// <summary>断言字符串集合与预期一致（顺序无关）。</summary>
    private static void AssertUserIds(IReadOnlyList<string> actual, params string[] expected)
    {
        var orderedActual = actual.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var orderedExpected = expected.OrderBy(x => x, StringComparer.Ordinal).ToArray();
        Assert.Equal(orderedExpected, orderedActual);
    }

    // ── D11 分配 / 唯一约束防重 / 解除（幂等） ──

    [Fact]
    public async Task AssignUser_Succeeds_AndListed()
    {
        using var host = NewHost();
        var ou = await host.Manager.CreateAsync("DE", "研发部", null, ct: CancellationToken.None);

        await host.Manager.AssignUserAsync(ou.Id, "u_001", CancellationToken.None);

        var userIds = await host.Manager.GetUserIdsInOrganizationUnitAsync(ou.Id, includeDescendants: false, ct: CancellationToken.None);
        AssertUserIds(userIds, "u_001");
    }

    [Fact]
    public async Task AssignUser_Duplicate_Throws()
    {
        using var host = NewHost();
        var ou = await host.Manager.CreateAsync("DE", "研发部", null, ct: CancellationToken.None);
        await host.Manager.AssignUserAsync(ou.Id, "u_001", CancellationToken.None);

        // 唯一约束 UX_OrganizationUnitUser_User_OU → 业务异常 InvalidOperationException
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.AssignUserAsync(ou.Id, "u_001", CancellationToken.None));
    }

    [Fact]
    public async Task UnassignUser_Removes_AndIdempotent()
    {
        using var host = NewHost();
        var ou = await host.Manager.CreateAsync("DE", "研发部", null, ct: CancellationToken.None);
        await host.Manager.AssignUserAsync(ou.Id, "u_001", CancellationToken.None);
        await host.Manager.AssignUserAsync(ou.Id, "u_002", CancellationToken.None);

        await host.Manager.UnassignUserAsync(ou.Id, "u_001", CancellationToken.None);

        var userIds = await host.Manager.GetUserIdsInOrganizationUnitAsync(ou.Id, includeDescendants: false, ct: CancellationToken.None);
        AssertUserIds(userIds, "u_002");

        // 幂等：再次解除不抛异常、无副作用
        await host.Manager.UnassignUserAsync(ou.Id, "u_001", CancellationToken.None);
        var afterIdempotent = await host.Manager.GetUserIdsInOrganizationUnitAsync(ou.Id, includeDescendants: false, ct: CancellationToken.None);
        AssertUserIds(afterIdempotent, "u_002");
    }

    // ── D12 子树用户列表（includeDescendants 含自身 / 仅直属） ──

    [Fact]
    public async Task GetUserIds_IncludeDescendants_IncludesSelfAndDescendantUsers()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync("R", "根", null, ct: CancellationToken.None);
        var child = await host.Manager.CreateAsync("C", "子", root.Id, ct: CancellationToken.None);
        await host.Manager.AssignUserAsync(root.Id, "u_root", CancellationToken.None);
        await host.Manager.AssignUserAsync(child.Id, "u_child", CancellationToken.None);

        var ids = await host.Manager.GetUserIdsInOrganizationUnitAsync(root.Id, includeDescendants: true, ct: CancellationToken.None);

        // 含自身 + 子孙用户（两步查询：OU 子树 → junction）
        AssertUserIds(ids, "u_root", "u_child");
    }

    [Fact]
    public async Task GetUserIds_ExcludeDescendants_OnlyDirectUsers()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync("R", "根", null, ct: CancellationToken.None);
        var child = await host.Manager.CreateAsync("C", "子", root.Id, ct: CancellationToken.None);
        await host.Manager.AssignUserAsync(root.Id, "u_root", CancellationToken.None);
        await host.Manager.AssignUserAsync(child.Id, "u_child", CancellationToken.None);

        var ids = await host.Manager.GetUserIdsInOrganizationUnitAsync(root.Id, includeDescendants: false, ct: CancellationToken.None);

        AssertUserIds(ids, "u_root");
    }

    // ── D13 用户所属 OU 列表（多 OU 归属） ──

    [Fact]
    public async Task GetOrganizationUnitIds_ForUser_ReturnsAllAssigned()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync("R", "根", null, ct: CancellationToken.None);
        var child = await host.Manager.CreateAsync("C", "子", root.Id, ct: CancellationToken.None);
        await host.Manager.AssignUserAsync(root.Id, "u_001", CancellationToken.None);
        await host.Manager.AssignUserAsync(child.Id, "u_001", CancellationToken.None);

        var ouIds = await host.Manager.GetOrganizationUnitIdsForUserAsync("u_001", CancellationToken.None);

        var ordered = ouIds.OrderBy(id => id).ToArray();
        Assert.Equal(new[] { root.Id, child.Id }.OrderBy(id => id).ToArray(), ordered);
    }

    // ── D18 补漏：解除全部用户 → 可删完整链路 / 同父重排序 / 根删除 ──

    [Fact]
    public async Task UnassignAllUsers_ThenDelete_Succeeds()
    {
        using var host = NewHost();
        var ou = await host.Manager.CreateAsync("DE", "研发部", null, ct: CancellationToken.None);
        await host.Manager.AssignUserAsync(ou.Id, "u_001", CancellationToken.None);
        await host.Manager.AssignUserAsync(ou.Id, "u_002", CancellationToken.None);

        // 完整链路：全部解除 → 删除成功
        await host.Manager.UnassignUserAsync(ou.Id, "u_001", CancellationToken.None);
        await host.Manager.UnassignUserAsync(ou.Id, "u_002", CancellationToken.None);
        await host.Manager.DeleteAsync(ou.Id, CancellationToken.None);

        Assert.Null(await host.Store.GetByIdAsync(ou.Id, CancellationToken.None));
        var remaining = await host.Manager.GetUserIdsInOrganizationUnitAsync(ou.Id, includeDescendants: false, ct: CancellationToken.None);
        Assert.Empty(remaining);
    }

    [Fact]
    public async Task SiblingCreate_DefaultSortOrder_AppendOrder()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync("R", "根", null, ct: CancellationToken.None);
        // 默认 SortOrder = 父下 max + 1（同级追加末尾）
        await host.Manager.CreateAsync("A", "A", root.Id, ct: CancellationToken.None);
        await host.Manager.CreateAsync("B", "B", root.Id, ct: CancellationToken.None);
        await host.Manager.CreateAsync("C", "C", root.Id, ct: CancellationToken.None);

        var tree = await host.Manager.GetTreeAsync(CancellationToken.None);
        Assert.NotNull(tree);

        var rootNode = FindNodeById(tree, root.Id);
        Assert.NotNull(rootNode);
        // 创建顺序即排序顺序 [A, B, C]
        Assert.Equal(new[] { "A", "B", "C" }, rootNode!.Children.Select(c => c.Code).ToArray());
    }

    [Fact]
    public async Task Update_SortOrder_ReordersSiblings()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync("R", "根", null, ct: CancellationToken.None);
        var a = await host.Manager.CreateAsync("A", "A", root.Id, sortOrder: 1, ct: CancellationToken.None);
        var b = await host.Manager.CreateAsync("B", "B", root.Id, sortOrder: 2, ct: CancellationToken.None);
        var c = await host.Manager.CreateAsync("C", "C", root.Id, sortOrder: 3, ct: CancellationToken.None);

        // C 置顶（sortOrder 0）→ 同级排序更新为 [C, A, B]
        await host.Manager.UpdateAsync(c.Id, sortOrder: 0, ct: CancellationToken.None);

        var tree = await host.Manager.GetTreeAsync(CancellationToken.None);
        var rootNode = FindNodeById(tree, root.Id);
        Assert.NotNull(rootNode);
        Assert.Equal(new[] { "C", "A", "B" }, rootNode!.Children.Select(x => x.Code).ToArray());

        // A 依然存在（更新只改排序，不动其他字段）
        var aReloaded = await host.Store.GetByIdAsync(a.Id, CancellationToken.None);
        Assert.NotNull(aReloaded);
        Assert.Equal("A", aReloaded!.Name);
    }

    [Fact]
    public async Task Delete_EmptyRoot_Succeeds()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync("R", "根", null, ct: CancellationToken.None);

        await host.Manager.DeleteAsync(root.Id, CancellationToken.None);

        var all = await host.Store.GetAllAsync(CancellationToken.None);
        Assert.Empty(all);
    }

    /// <summary>在树中按 Id 查找节点（BFS，容忍虚拟根）。</summary>
    private static OrganizationUnitTreeNode? FindNodeById(OrganizationUnitTreeNode node, long id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
        {
            var found = FindNodeById(child, id);
            if (found != null) return found;
        }
        return null;
    }
}