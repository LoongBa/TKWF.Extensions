using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.FileManagement;

namespace TKWF.Ext.FileManagement.Tests;

/// <summary>
/// D15 查询测试——GetFilesAsync 分页 + 根级文件（FolderId null）+ 目录重命名后文件查询不变（C6）+ SearchFilesAsync 关键字。
/// </summary>
public class FileQueryTests
{
    private static readonly byte[] Content1 = Encoding.UTF8.GetBytes("content one");
    private static readonly byte[] Content2 = Encoding.UTF8.GetBytes("content two");

    [Fact]
    public async Task GetFilesAsync_Pagination_SkipTake()
    {
        using var host = FileManagementTestHost.Create();
        var folder = await host.CreateFolderAsync("docs", "文档");
        for (var i = 1; i <= 5; i++)
            await host.UploadFileAsync(folder.Id, $"f{i}.txt", Encoding.UTF8.GetBytes($"payload {i}"));

        var page1 = await host.Manager.GetFilesAsync(folder.Id, skip: 0, take: 2, ct: CancellationToken.None);
        var page2 = await host.Manager.GetFilesAsync(folder.Id, skip: 2, take: 2, ct: CancellationToken.None);
        var page3 = await host.Manager.GetFilesAsync(folder.Id, skip: 4, take: 50, ct: CancellationToken.None);

        Assert.Equal(2, page1.Count);
        Assert.Equal(2, page2.Count);
        Assert.Single(page3);

        // 分页内容无重叠、无遗漏（不依赖排序语义）
        var allNames = page1.Concat(page2).Concat(page3).Select(f => f.Name).OrderBy(n => n).ToArray();
        Assert.Equal(new[] { "f1.txt", "f2.txt", "f3.txt", "f4.txt", "f5.txt" }, allNames);
    }

    [Fact]
    public async Task GetFilesAsync_RootLevelFiles_FolderIdNull()
    {
        using var host = FileManagementTestHost.Create();
        var folder = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(null, "root.txt", Content1);
        await host.UploadFileAsync(folder.Id, "inside.txt", Content2);

        // FolderId = null → 仅根级文件
        var roots = await host.Manager.GetFilesAsync(null, ct: CancellationToken.None);
        var rootFile = Assert.Single(roots);
        Assert.Equal("root.txt", rootFile.Name);
        Assert.Null(rootFile.FolderId);

        // 目录级查询不含根级文件
        var inside = await host.Manager.GetFilesAsync(folder.Id, ct: CancellationToken.None);
        Assert.Single(inside);
        Assert.Equal("inside.txt", inside[0].Name);
    }

    [Fact]
    public async Task GetFilesAsync_AfterFolderRename_QueryUnaffected()
    {
        using var host = FileManagementTestHost.Create();
        var folder = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(folder.Id, "a.txt", Content1);

        await host.Manager.RenameFolderAsync(folder.Id, "改名后", CancellationToken.None);

        // D8/C6：目录重命名只改 Name → 文件存 FolderId → 按目录查询不变
        var files = await host.Manager.GetFilesAsync(folder.Id, ct: CancellationToken.None);
        var file = Assert.Single(files);
        Assert.Equal("a.txt", file.Name);
    }

    [Fact]
    public async Task SearchFilesAsync_KeywordMatch()
    {
        using var host = FileManagementTestHost.Create();
        var folder = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(folder.Id, "report-final.txt", Content1);
        await host.UploadFileAsync(folder.Id, "invoice.csv", Content2);

        var hits = await host.Manager.SearchFilesAsync("report", ct: CancellationToken.None);
        var hit = Assert.Single(hits);
        Assert.Equal("report-final.txt", hit.Name);

        Assert.Empty(await host.Manager.SearchFilesAsync("不存在的关键字", ct: CancellationToken.None));
    }
}