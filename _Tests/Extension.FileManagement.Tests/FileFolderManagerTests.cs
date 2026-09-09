using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.FileManagement;

namespace TKWF.Ext.FileManagement.Tests;

/// <summary>
/// D4-D8 目录树测试——创建（Code 唯一 + Level/Path 物化路径）、循环/引用防护、删除保护、重命名
/// （只改 Name、Path/子树不变）、全树组装层级 + SortOrder 排序。
/// </summary>
public class FileFolderManagerTests
{
    private static readonly byte[] SampleBytes = Encoding.UTF8.GetBytes("folder-test-content");

    // ── D4 创建根目录：Level=0、Path=/code/ ──

    [Fact]
    public async Task CreateRoot_LevelZero_PathSlashCodeSlash()
    {
        using var host = FileManagementTestHost.Create();

        var root = await host.CreateFolderAsync("docs", "文档");

        Assert.Equal(0, root.Level);
        Assert.Equal("/docs/", root.Path);
        Assert.Null(root.ParentId);
        Assert.Equal("docs", root.Code);
        Assert.Equal("文档", root.Name);
    }

    // ── D5 创建子目录：Level 递增 + Path 继承 ──

    [Fact]
    public async Task CreateChild_LevelIncrements_PathInherits()
    {
        using var host = FileManagementTestHost.Create();
        var parent = await host.CreateFolderAsync("docs", "文档");

        var child = await host.CreateFolderAsync("attach", "附件", parent.Id);

        Assert.Equal(1, child.Level);
        Assert.Equal("/docs/attach/", child.Path);
        Assert.Equal(parent.Id, child.ParentId);
    }

    // ── D6 重复 Code 拒绝（库级唯一）+ 引用/循环防护 ──

    [Fact]
    public async Task CreateFolder_DuplicateCode_Throws()
    {
        using var host = FileManagementTestHost.Create();
        await host.CreateFolderAsync("docs", "文档");

        // Code 库级唯一约束 → 原始库异常自然传播
        await Assert.ThrowsAnyAsync<Exception>(
            () => host.CreateFolderAsync("docs", "另一文档", ct: CancellationToken.None));
    }

    [Fact]
    public async Task CreateFolder_ParentNotExists_ThrowsReferenceGuard()
    {
        using var host = FileManagementTestHost.Create();

        // 父目录不存在 → 引用守卫拒绝（InvalidOperationException）。
        // 注：v0.1.0 无目录移动（改挂），"parentId == 自身/后代"的循环在公开 API 边界内不可达——
        // 以引用守卫 + 路径守卫代理覆盖防护语义。
        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.CreateFolderAsync("orphan", "孤儿", parentId: 999_999, ct: CancellationToken.None));
    }

    [Fact]
    public async Task CreateFolder_PathLengthGuard_ThrowsFriendlyException()
    {
        using var host = FileManagementTestHost.Create();
        // Path 列 MaxLength=1024（对齐 OrganizationUnit C3 路径守卫）——深链超限拒绝而非 DB 溢出。
        var parent = await host.CreateFolderAsync(new string('r', 100), "root");
        Exception? thrown = null;

        for (var i = 0; i < 30 && thrown == null; i++)
        {
            var code = new string((char)('a' + (i % 26)), 100);
            try
            {
                parent = await host.CreateFolderAsync(code, $"f{i}", parent.Id, ct: CancellationToken.None);
            }
            catch (Exception ex)
            {
                thrown = ex;
            }
        }

        Assert.NotNull(thrown);
        Assert.IsAssignableFrom<InvalidOperationException>(thrown);
    }

    // ── 更新：只改 Name/SortOrder，Code/Path 不变量保持 ──

    [Fact]
    public async Task UpdateFolder_NameAndSortOrder_CodeAndPathUnchanged()
    {
        using var host = FileManagementTestHost.Create();
        var root = await host.CreateFolderAsync("docs", "文档");

        await host.Manager.UpdateFolderAsync(root.Id, name: "改名", sortOrder: 7, ct: CancellationToken.None);

        var updated = (await host.Manager.GetSubFoldersAsync(null, CancellationToken.None)).Single(f => f.Id == root.Id);
        Assert.Equal("改名", updated.Name);
        Assert.Equal(7, updated.SortOrder);
        Assert.Equal("docs", updated.Code);      // Code 不可变（C3——Path 由 Code 构建）
        Assert.Equal("/docs/", updated.Path);    // Path 由 Code 构建 → 不变
    }

    // ── D7 删除保护：有子目录/文件目录禁删；空目录可删 ──

    [Fact]
    public async Task DeleteFolder_HasSubfolder_Throws()
    {
        using var host = FileManagementTestHost.Create();
        var root = await host.CreateFolderAsync("docs", "文档");
        await host.CreateFolderAsync("sub", "子目录", root.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Manager.DeleteFolderAsync(root.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteFolder_HasFiles_Throws()
    {
        using var host = FileManagementTestHost.Create();
        var root = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(root.Id, "a.txt", SampleBytes);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Manager.DeleteFolderAsync(root.Id, CancellationToken.None));
    }

    [Fact]
    public async Task DeleteFolder_Empty_Succeeds()
    {
        using var host = FileManagementTestHost.Create();
        var root = await host.CreateFolderAsync("docs", "文档");

        await host.Manager.DeleteFolderAsync(root.Id, CancellationToken.None);

        Assert.Empty(await host.Manager.GetSubFoldersAsync(null, CancellationToken.None));
    }

    // ── D8 目录重命名：只改 Name（Path 由 Code 构建，Code 不可变——C3），子树 Path 不变 + 文件查询不受影响（C6） ──

    [Fact]
    public async Task RenameFolder_ChangesNameOnly_SubtreePathAndFileQueryUnaffected()
    {
        using var host = FileManagementTestHost.Create();
        var root = await host.CreateFolderAsync("docs", "文档");
        var child = await host.CreateFolderAsync("attach", "附件", root.Id);
        await host.UploadFileAsync(root.Id, "a.txt", SampleBytes);

        await host.Manager.RenameFolderAsync(root.Id, "文档中心（新）", CancellationToken.None);

        var renamed = (await host.Manager.GetSubFoldersAsync(null, CancellationToken.None)).Single(f => f.Id == root.Id);
        Assert.Equal("文档中心（新）", renamed.Name);
        Assert.Equal("/docs/", renamed.Path);   // Path 由 Code 构建 → 重命名不重算（C3）

        var childAfter = (await host.Manager.GetSubFoldersAsync(root.Id, CancellationToken.None)).Single(f => f.Id == child.Id);
        Assert.Equal("attach", childAfter.Code);
        Assert.Equal("/docs/attach/", childAfter.Path);  // 子树 Path 不变

        var files = await host.Manager.GetFilesAsync(root.Id, ct: CancellationToken.None);
        var file = Assert.Single(files);
        Assert.Equal("a.txt", file.Name);        // 文件存 FolderId → 查询不受影响（C6）
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("../x")]
    [InlineData("a/b")]
    public async Task RenameFolder_InvalidName_Throws(string newName)
    {
        using var host = FileManagementTestHost.Create();
        var root = await host.CreateFolderAsync("docs", "文档");

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => host.Manager.RenameFolderAsync(root.Id, newName, CancellationToken.None));
    }

    // ── GetFolderTreeAsync 层级正确 + SortOrder 排序 ──

    [Fact]
    public async Task GetFolderTree_AssemblesHierarchy_SortedBySortOrder()
    {
        using var host = FileManagementTestHost.Create();
        var root = await host.CreateFolderAsync("root", "根目录", sortOrder: 5);
        await host.CreateFolderAsync("beta", "B", root.Id, sortOrder: 2);
        var alpha = await host.CreateFolderAsync("alpha", "A", root.Id, sortOrder: 1);
        await host.CreateFolderAsync("leaf", "叶子", alpha.Id, sortOrder: 9);
        await host.CreateFolderAsync("gamma", "G", root.Id, sortOrder: 3);

        var tree = await host.Manager.GetFolderTreeAsync(CancellationToken.None);

        Assert.NotNull(tree);
        var rootNode = FindNodeByCode(tree!, "root");
        Assert.NotNull(rootNode);
        // SortOrder 同级升序（1/2/3）
        Assert.Equal(new[] { "alpha", "beta", "gamma" }, rootNode!.Children.Select(c => c.Code));
        // 嵌套一层
        var alphaNode = rootNode.Children.Single(c => c.Code == "alpha");
        Assert.Equal("leaf", Assert.Single(alphaNode.Children).Code);
    }

    [Fact]
    public async Task GetSubFolders_NullParent_ReturnsRoots_ExplicitSortOrder()
    {
        using var host = FileManagementTestHost.Create();
        // 显式 SortOrder——期望按升序返回
        var z = await host.CreateFolderAsync("zdir", "Z", sortOrder: 5);
        var adir = await host.CreateFolderAsync("adir", "A", sortOrder: 3);
        _ = z;
        _ = adir;

        var roots = await host.Manager.GetSubFoldersAsync(null, CancellationToken.None);

        Assert.Equal(2, roots.Count);
        Assert.Equal(new[] { "adir", "zdir" }, roots.Select(f => f.Code).ToArray());
    }

    // ── Helpers：BFS 树查找（容忍真实根/虚拟根两种形态） ──

    private static FileFolderTreeNode? FindNodeByCode(FileFolderTreeNode node, string code)
    {
        var queue = new Queue<FileFolderTreeNode>();
        queue.Enqueue(node);
        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (current.Code == code)
                return current;
            foreach (var child in current.Children)
                queue.Enqueue(child);
        }

        return null;
    }
}