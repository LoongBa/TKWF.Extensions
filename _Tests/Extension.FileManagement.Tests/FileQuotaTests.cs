using System;
using System.Threading.Tasks;
using Xunit;

namespace TKWF.Ext.FileManagement.Tests;

/// <summary>
/// V0.2.0：配额测试（D7-D10）。
/// <para>覆盖：文件数/目录容量/全局容量三配额拒绝 + 未配置（null）不限制 + 检查位置（Blob 落盘前——超限不产生 Blob）。</para>
/// </summary>
public class FileQuotaTests
{
    // ── D7：文件数配额 ──

    [Fact]
    public async Task MaxFilesPerFolder_Exceeded_Throws_NoBlobLeft()
    {
        using var host = FileManagementTestHost.Create(new FileManagementOptions { MaxFilesPerFolder = 1 });
        var folder = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(folder.Id, "a.txt", "content-a"u8.ToArray());

        int blobsBefore = host.BlobCount();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.UploadFileAsync(folder.Id, "b.txt", "content-b"u8.ToArray()));

        Assert.Equal(blobsBefore, host.BlobCount());   // D10：配额检查在 Blob 落盘前——超限不产生 Blob
    }

    [Fact]
    public async Task MaxFilesPerFolder_NotConfigured_NoLimit()
    {
        using var host = FileManagementTestHost.Create();   // 默认 null 不限制
        var folder = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(folder.Id, "a.txt", "content-a"u8.ToArray());
        var b = await host.UploadFileAsync(folder.Id, "b.txt", "content-b"u8.ToArray());

        Assert.NotNull(b);
    }

    // ── D8：目录容量配额 ──

    [Fact]
    public async Task MaxFolderSizeBytes_Exceeded_Throws_NoBlobLeft()
    {
        using var host = FileManagementTestHost.Create(new FileManagementOptions { MaxFolderSizeBytes = 20 });
        var folder = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(folder.Id, "a.txt", "0123456789"u8.ToArray());   // 10 字节

        int blobsBefore = host.BlobCount();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.UploadFileAsync(folder.Id, "b.txt", "0123456789abcdef"u8.ToArray()));   // 16 字节 → 10+16 > 20

        Assert.Equal(blobsBefore, host.BlobCount());   // D10：超限不产生 Blob
    }

    [Fact]
    public async Task MaxFolderSizeBytes_NotConfigured_NoLimit()
    {
        using var host = FileManagementTestHost.Create();   // 默认 null 不限制
        var folder = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(folder.Id, "a.txt", new byte[100]);
        var b = await host.UploadFileAsync(folder.Id, "b.txt", new byte[200]);

        Assert.NotNull(b);
    }

    // ── D9：全局容量配额 ──

    [Fact]
    public async Task MaxTotalSizeBytes_Exceeded_Throws_NoBlobLeft()
    {
        using var host = FileManagementTestHost.Create(new FileManagementOptions { MaxTotalSizeBytes = 30 });
        var folder = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(folder.Id, "a.txt", "0123456789"u8.ToArray());   // 10 字节

        int blobsBefore = host.BlobCount();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.UploadFileAsync(folder.Id, "b.txt", new byte[25]));   // 10 + 25 > 30

        Assert.Equal(blobsBefore, host.BlobCount());   // D10：超限不产生 Blob
    }

    [Fact]
    public async Task MaxTotalSizeBytes_NotConfigured_NoLimit()
    {
        using var host = FileManagementTestHost.Create();   // 默认 null 不限制
        var folder = await host.CreateFolderAsync("docs", "文档");
        var b = await host.UploadFileAsync(folder.Id, "b.txt", new byte[500]);

        Assert.NotNull(b);
    }

    // ── D8b：多文件累加触发目录容量 ──

    [Fact]
    public async Task MaxFolderSize_AccumulatedAcrossFiles_Exceeded()
    {
        using var host = FileManagementTestHost.Create(new FileManagementOptions { MaxFolderSizeBytes = 15 });
        var folder = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(folder.Id, "a.txt", "0123456789"u8.ToArray());   // 10 字节
        await host.UploadFileAsync(folder.Id, "b.txt", "01234"u8.ToArray());        // 5 字节 → 累计 15

        // 第三个文件：10 + 5 + 6 = 21 > 15 → 拒绝
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.UploadFileAsync(folder.Id, "c.txt", "012345"u8.ToArray()));
    }
}
