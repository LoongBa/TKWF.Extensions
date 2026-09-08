using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.OrganizationUnit;

namespace TKWF.Ext.OrganizationUnit.Tests;

/// <summary>
/// OrganizationUnitManager 树形能力测试——D1-D10（创建/子树/祖先/移动/删除防护）+ D15-D17（白名单/长度守卫/事务行为）。
/// <para>每用例独立 SQLite 内存库（<see cref="OrganizationUnitTestHost.Create"/>）。</para>
/// </summary>
public class OrganizationUnitManagerTests
{
    private static OrganizationUnitTestHost NewHost() => OrganizationUnitTestHost.Create();

    /// <summary>断言实体集合的 Code 集合与预期一致（顺序无关）。</summary>
    private static void AssertCodes(IReadOnlyList<OrganizationUnitEntity> entities, params string[] expectedCodes)
    {
        var actual = entities.Select(e => e.Code).OrderBy(c => c, StringComparer.Ordinal).ToArray();
        var expected = expectedCodes.OrderBy(c => c, StringComparer.Ordinal).ToArray();
        Assert.Equal(expected, actual);
    }

    /// <summary>在树中按 Id 查找节点（BFS，容忍 GetTreeAsync 返回真实根或虚拟根）。</summary>
    private static OrganizationUnitTreeNode? FindNode(OrganizationUnitTreeNode node, long id)
    {
        if (node.Id == id) return node;
        foreach (var child in node.Children)
        {
            var found = FindNode(child, id);
            if (found != null) return found;
        }
        return null;
    }

    // ── D1 创建根 ──

    [Fact]
    public async Task CreateRoot_LevelZero_PathPrefixed()
    {
        using var host = NewHost();

        var root = await host.Manager.CreateAsync("HQ", "总部", null, ct: CancellationToken.None);

        Assert.True(root.Id > 0);
        Assert.Null(root.ParentId);
        Assert.Equal(0, root.Level);
        Assert.Equal("/HQ/", root.Path);

        // 持久化复验
        var persisted = await host.Store.GetByCodeAsync("HQ", CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Equal(0, persisted!.Level);
        Assert.Equal("/HQ/", persisted.Path);
    }

    // ── D2 创建子 ──

    [Fact]
    public async Task CreateChild_TwoLevels_LevelAndPathInherit()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync("HQ", "总部", null, ct: CancellationToken.None);

        var child = await host.Manager.CreateAsync("DE", "研发部", root.Id, ct: CancellationToken.None);

        Assert.Equal(root.Id, child.ParentId);
        Assert.Equal(1, child.Level);
        Assert.Equal("/HQ/DE/", child.Path);

        var persisted = await host.Store.GetByCodeAsync("DE", CancellationToken.None);
        Assert.Equal(1, persisted!.Level);
        Assert.Equal("/HQ/DE/", persisted.Path);
    }

    [Fact]
    public async Task CreateChild_ThreeLevels_LevelAndPathInherit()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync("HQ", "总部", null, ct: CancellationToken.None);
        var dept = await host.Manager.CreateAsync("DE", "研发部", root.Id, ct: CancellationToken.None);

        var emp = await host.Manager.CreateAsync("EMP", "研发小组", dept.Id, ct: CancellationToken.None);

        Assert.Equal(dept.Id, emp.ParentId);
        Assert.Equal(2, emp.Level);
        Assert.Equal("/HQ/DE/EMP/", emp.Path);
    }

    // ── D3 重复 Code 拒绝（库级唯一约束） ──

    [Fact]
    public async Task Create_DuplicateCode_Throws()
    {
        using var host = NewHost();
        await host.Manager.CreateAsync("HQ", "总部", null, ct: CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(
            () => host.Manager.CreateAsync("HQ", "重复编码", null, ct: CancellationToken.None));
    }

    // ── D4 全树组装：层级正确 + 同级 SortOrder 排序 ──

    [Fact]
    public async Task GetTree_AssemblesHierarchy_ChildrenSortedBySortOrder()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync("R", "根", null, sortOrder: 1, ct: CancellationToken.None);
        // 显式 SortOrder 打乱创建顺序，验证按 SortOrder 升序组装
        await host.Manager.CreateAsync("B", "B组", root.Id, sortOrder: 20, ct: CancellationToken.None);
        await host.Manager.CreateAsync("A", "A组", root.Id, sortOrder: 10, ct: CancellationToken.None);
        await host.Manager.CreateAsync("G", "孙节点", (await host.Store.GetByCodeAsync("A", CancellationToken.None))!.Id, sortOrder: 1, ct: CancellationToken.None);

        var tree = await host.Manager.GetTreeAsync(CancellationToken.None);

        Assert.NotNull(tree);
        var rootNode = FindNode(tree, root.Id);
        Assert.NotNull(rootNode);
        Assert.Equal(root.Id, rootNode!.Id);
        Assert.Equal("R", rootNode.Code);
        // 同级按 SortOrder 升序 = [A(10), B(20)]
        Assert.Equal(new[] { "A", "B" }, rootNode.Children.Select(c => c.Code).ToArray());
        // 层级正确：孙节点挂在 A 之下
        var aNode = rootNode.Children.First(c => c.Code == "A");
        Assert.Equal(new[] { "G" }, aNode.Children.Select(c => c.Code).ToArray());
    }

    // ── D5 子树查询（含自身 + 子孙） ──

    [Fact]
    public async Task GetSubTree_IncludesSelfAndDescendants()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync("R", "根", null, ct: CancellationToken.None);
        var child = await host.Manager.CreateAsync("C", "子", root.Id, ct: CancellationToken.None);
        await host.Manager.CreateAsync("G", "孙", child.Id, ct: CancellationToken.None);

        var fullSubTree = await host.Manager.GetSubTreeAsync(root.Id, CancellationToken.None);
        AssertCodes(fullSubTree, "R", "C", "G");

        var childSubTree = await host.Manager.GetSubTreeAsync(child.Id, CancellationToken.None);
        AssertCodes(childSubTree, "C", "G");
    }

    // ── D6 祖先链（根 → 直接父，不含自身） ──

    [Fact]
    public async Task GetAncestors_RootToDirectParent_ExcludesSelf()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync("R", "根", null, ct: CancellationToken.None);
        var child = await host.Manager.CreateAsync("C", "子", root.Id, ct: CancellationToken.None);
        var grand = await host.Manager.CreateAsync("G", "孙", child.Id, ct: CancellationToken.None);

        var ancestors = await host.Manager.GetAncestorsAsync(grand.Id, CancellationToken.None);
        Assert.Equal(new[] { "R", "C" }, ancestors.Select(e => e.Code).ToArray());
        Assert.DoesNotContain(ancestors, e => e.Id == grand.Id);

        var childAncestors = await host.Manager.GetAncestorsAsync(child.Id, CancellationToken.None);
        Assert.Equal(new[] { "R" }, childAncestors.Select(e => e.Code).ToArray());

        var rootAncestors = await host.Manager.GetAncestorsAsync(root.Id, CancellationToken.None);
        Assert.Empty(rootAncestors);
    }

    // ── D7 移动：子树 Path/Level 全重算（含孙代）+ 查询反映新父 ──

    [Fact]
    public async Task Move_RecalculatesSubtree_PathsAndLevels_WithGrandchild()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        var b = await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None);
        var c = await host.Manager.CreateAsync("C", "C", b.Id, ct: CancellationToken.None);
        await host.Manager.CreateAsync("E", "E", c.Id, ct: CancellationToken.None); // C 下孙代
        var d = await host.Manager.CreateAsync("D", "D", a.Id, ct: CancellationToken.None);

        await host.Manager.MoveAsync(c.Id, d.Id, CancellationToken.None);

        // C 自身重算（D 为 A 子节点 Level=1 → C 移入后 Level=2）
        var cMoved = await host.Store.GetByIdAsync(c.Id, CancellationToken.None);
        Assert.Equal(d.Id, cMoved!.ParentId);
        Assert.Equal(2, cMoved.Level);
        Assert.Equal("/A/D/C/", cMoved.Path);
        // 孙代 E 重算（含孙代断言：C Level=2 → E Level=3）
        var eMoved = await host.Store.GetByCodeAsync("E", CancellationToken.None);
        Assert.Equal(c.Id, eMoved!.ParentId);
        Assert.Equal(3, eMoved.Level);
        Assert.Equal("/A/D/C/E/", eMoved.Path);
        // 未移动节点不受影响
        var bStays = await host.Store.GetByCodeAsync("B", CancellationToken.None);
        Assert.Equal("/A/B/", bStays!.Path);
        Assert.Equal(1, bStays.Level);
    }

    [Fact]
    public async Task Move_SubTreeAndAncestorsReflectNewParent()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        var b = await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None);
        var c = await host.Manager.CreateAsync("C", "C", b.Id, ct: CancellationToken.None);
        await host.Manager.CreateAsync("E", "E", c.Id, ct: CancellationToken.None);
        var d = await host.Manager.CreateAsync("D", "D", a.Id, ct: CancellationToken.None);

        await host.Manager.MoveAsync(c.Id, d.Id, CancellationToken.None);

        // 移动后子树查询反映新父（D 子树含 C + 孙代 E）
        var dSubTree = await host.Manager.GetSubTreeAsync(d.Id, CancellationToken.None);
        AssertCodes(dSubTree, "D", "C", "E");
        // 旧父 B 的子树不再含 C
        var bSubTree = await host.Manager.GetSubTreeAsync(b.Id, CancellationToken.None);
        AssertCodes(bSubTree, "B");
        // 移动后祖先链反映新父（根 → 直接父 = A, D）
        var cAncestors = await host.Manager.GetAncestorsAsync(c.Id, CancellationToken.None);
        Assert.Equal(new[] { "A", "D" }, cAncestors.Select(e => e.Code).ToArray());
    }

    // ── D8 移动为根（newParentId = null） ──

    [Fact]
    public async Task Move_ToRoot_ResetsLevelAndPath()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        var b = await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None);

        await host.Manager.MoveAsync(b.Id, null, CancellationToken.None);

        var bMoved = await host.Store.GetByIdAsync(b.Id, CancellationToken.None);
        Assert.Null(bMoved!.ParentId);
        Assert.Equal(0, bMoved.Level);
        Assert.Equal("/B/", bMoved.Path);
    }

    // ── D9 循环防护 ──

    [Fact]
    public async Task Move_ToItself_Throws()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.MoveAsync(a.Id, a.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Move_IntoOwnDescendant_Throws()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        var b = await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None);
        var c = await host.Manager.CreateAsync("C", "C", b.Id, ct: CancellationToken.None);

        // C 是 A 的后代——A 移入 C 之下成环
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.MoveAsync(a.Id, c.Id, CancellationToken.None));
    }

    // ── D10 删除防护 ──

    [Fact]
    public async Task Delete_HasChildren_Throws()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.DeleteAsync(a.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_HasAssignedUsers_Throws()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        await host.Manager.AssignUserAsync(a.Id, "u_001", CancellationToken.None);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.DeleteAsync(a.Id, CancellationToken.None));
    }

    [Fact]
    public async Task Delete_EmptyOu_Succeeds_AndGoneFromAllQueries()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);

        await host.Manager.DeleteAsync(a.Id, CancellationToken.None);

        // 全量查询不含已删 OU
        var all = await host.Store.GetAllAsync(CancellationToken.None);
        Assert.DoesNotContain(all, e => e.Id == a.Id);
        // 子树查询引用已删 OU → 引用守卫抛异常（对齐 Create/AssignUser 引用守卫语义）
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.GetSubTreeAsync(a.Id, CancellationToken.None));
        // 按编码查无
        Assert.Null(await host.Store.GetByCodeAsync("A", CancellationToken.None));
    }

    [Fact]
    public async Task Delete_DeletedCode_BecomesReusable()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("HQ", "总部", null, ct: CancellationToken.None);
        await host.Manager.DeleteAsync(a.Id, CancellationToken.None);

        // 物理删除后 Code 可复用（唯一索引释放）
        var recreated = await host.Manager.CreateAsync("HQ", "总部（重建）", null, ct: CancellationToken.None);

        Assert.True(recreated.Id > 0);
        Assert.NotEqual(a.Id, recreated.Id);
        Assert.Equal("/HQ/", recreated.Path);
    }

    [Fact]
    public async Task Delete_CreateChildUnderDeletedOu_Throws()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        await host.Manager.DeleteAsync(a.Id, CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(
            () => host.Manager.CreateAsync("B", "孤儿子节点", a.Id, ct: CancellationToken.None));
    }

    [Fact]
    public async Task Delete_AssignUserUnderDeletedOu_Throws()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        await host.Manager.DeleteAsync(a.Id, CancellationToken.None);

        await Assert.ThrowsAnyAsync<Exception>(
            () => host.Manager.AssignUserAsync(a.Id, "u_001", CancellationToken.None));
    }

    // ── D15 Code 白名单（[A-Za-z0-9_.-]；% 与空白拒绝；_/-/. 合法） ──

    [Theory]
    [InlineData("HR%Dept")]
    [InlineData("HR_Dept%")]
    [InlineData("HR Dept")]
    [InlineData(" HR")]
    [InlineData("HR ")]
    public async Task CreateCode_Whitelist_RejectsInvalidCodes(string invalidCode)
    {
        using var host = NewHost();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => host.Manager.CreateAsync(invalidCode, "非法编码", null, ct: CancellationToken.None));
    }

    [Theory]
    [InlineData("HR_Dept", "/HR_Dept/")]
    [InlineData("HR-Dept", "/HR-Dept/")]
    [InlineData("HR.Dept", "/HR.Dept/")]
    [InlineData("Dept1", "/Dept1/")]
    public async Task CreateCode_Whitelist_AcceptsUnderscoreDashDot(string code, string expectedPath)
    {
        using var host = NewHost();

        var ou = await host.Manager.CreateAsync(code, "合法编码", null, ct: CancellationToken.None);

        Assert.True(ou.Id > 0);
        Assert.Equal(expectedPath, ou.Path);
    }

    // ── D16 Path 长度守卫（>1024 → 友好业务异常，而非 DB OverflowError） ──

    [Fact]
    public async Task Create_DeepTree_PathLengthGuard_ThrowsFriendlyException()
    {
        using var host = NewHost();
        var root = await host.Manager.CreateAsync(new string('A', 180), "超长根", null, ct: CancellationToken.None);
        var parentId = root.Id;
        var thrown = (Exception?)null;

        for (var i = 1; i <= 10 && thrown == null; i++)
        {
            try
            {
                var node = await host.Manager.CreateAsync(
                    new string((char)('B' + i), 180), $"超长层级{i}", parentId, ct: CancellationToken.None);
                parentId = node.Id;
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }

        // 深度 6（Path ≈ 1087 字符）即应触发 1024 长度守卫——友好业务异常而非 DB 溢出
        Assert.NotNull(thrown);
        Assert.IsAssignableFrom<InvalidOperationException>(thrown);
    }

    // ── D17 移动事务原子性（行为验证：完成后子树 Path 全部新值，无半更新） ──

    [Fact]
    public async Task Move_Completes_AllSubtreePathsAreNewValues()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        var b = await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None);
        var c = await host.Manager.CreateAsync("C", "C", b.Id, ct: CancellationToken.None);
        var e = await host.Manager.CreateAsync("E", "E", c.Id, ct: CancellationToken.None);
        var d = await host.Manager.CreateAsync("D", "D", a.Id, ct: CancellationToken.None);

        await host.Manager.MoveAsync(c.Id, d.Id, CancellationToken.None);

        // 事务提交后：移动子树整体换前缀——全部节点路径要么保持旧值（A/B/D），要么是新值（C/E 换到 /A/D/ 下），
        // 绝无"半更新"中间态（旧 /A/B/C/、旧 /A/B/C/E/ 残留即断言失败）
        var all = await host.Store.GetAllAsync(CancellationToken.None);
        var actualPaths = all.Select(x => x.Path).OrderBy(p => p, StringComparer.Ordinal).ToArray();
        var expected = new[] { "/A/", "/A/B/", "/A/D/", "/A/D/C/", "/A/D/C/E/" }
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        Assert.Equal(expected, actualPaths);
        // 移动后的 C/E 绝无残留于旧父 B 之下（B 自身 /A/B/ 合法存在——B 未移动）
        Assert.DoesNotContain(actualPaths, p => p.StartsWith("/A/B/C", StringComparison.Ordinal));
    }

    // ── P3 移动 SortOrder 追加新父末尾 ──

    [Fact]
    public async Task Move_AppendsToNewParentEnd_SortOrder()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        var b = await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None);
        var c = await host.Manager.CreateAsync("C", "C", a.Id, ct: CancellationToken.None);
        var b1 = await host.Manager.CreateAsync("B1", "B1", b.Id, ct: CancellationToken.None); // B 下首个 → SortOrder 0

        // B1 移入 C（C 无子）→ 追加到 C 子级末尾 = max+1 = 0
        await host.Manager.MoveAsync(b1.Id, c.Id, CancellationToken.None);
        var b1Moved = await host.Store.GetByIdAsync(b1.Id, CancellationToken.None);
        Assert.Equal(c.Id, b1Moved!.ParentId);
        Assert.Equal(0, b1Moved.SortOrder);

        // C 下再建 C1 → 紧随 B1 之后（SortOrder 1）
        var c1 = await host.Manager.CreateAsync("C1", "C1", c.Id, ct: CancellationToken.None);
        Assert.Equal(1, c1.SortOrder);

        // 旧父 B 不再含 B1（B 子树仅自身）
        var bSubTree = await host.Manager.GetSubTreeAsync(b.Id, CancellationToken.None);
        AssertCodes(bSubTree, "B");
    }

    // ── P2 树组装防御（孤儿——数据库被直接篡改的场景） ──
    // 注：环检测不适用——ParentId 单父约束下从根链数学上不可成环（审核 P4 分析），孤儿防护即足够

    [Fact]
    public async Task GetTree_OrphanNode_Throws()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);

        // Store 直插孤儿行（ParentId 指向不存在的节点）——绕过 Manager 引用守卫
        await host.Store.CreateAsync(new OrganizationUnitEntity
        {
            Code = "X",
            Name = "孤儿",
            ParentId = 9999,
            Level = 1,
            Path = "/A/X/"
        }, CancellationToken.None);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.GetTreeAsync(CancellationToken.None));
    }

    [Fact]
    public async Task GetAncestors_MissingAncestor_Throws()
    {
        using var host = NewHost();
        var a = await host.Manager.CreateAsync("A", "A", null, ct: CancellationToken.None);
        var b = await host.Manager.CreateAsync("B", "B", a.Id, ct: CancellationToken.None); // Path=/A/B/

        // Store 直改：Path 指向不存在的祖先段 X
        b.Path = "/X/B/";
        await host.Store.UpdateAsync(b, CancellationToken.None);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.GetAncestorsAsync(b.Id, CancellationToken.None));
    }
}