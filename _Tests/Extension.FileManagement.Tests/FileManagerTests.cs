using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Ext.FileManagement;

namespace TKWF.Ext.FileManagement.Tests;

/// <summary>
/// D9-D14 + D16-D17 文件测试——上传成功链路（SHA256/Blob 落盘）、安全校验负路径（D10）、
/// 去重（D11）、下载（D12）、删除（D13）、重命名/移动（D14）、元数据失败补偿（D16）、并发唯一约束（D17）。
/// </summary>
public class FileManagerTests
{
    private static readonly byte[] HelloBytes = Encoding.UTF8.GetBytes("Hello FileManagement");
    private static readonly byte[] OtherBytes = Encoding.UTF8.GetBytes("another payload");

    // ── D9 上传成功链路：校验 → SHA256 → Blob 落盘 → 元数据落库 ──

    [Fact]
    public async Task Upload_ValidContent_ReturnsEntity_BlobPersisted_Sha256Correct()
    {
        using var host = FileManagementTestHost.Create();
        var folder = await host.CreateFolderAsync("docs", "文档");

        var file = await host.UploadFileAsync(folder.Id, "hello.txt", HelloBytes);

        Assert.Equal(folder.Id, file.FolderId);
        Assert.Equal(".txt", file.Extension);
        Assert.Equal("text/plain", file.ContentType);
        Assert.Equal(HelloBytes.LongLength, file.Size);
        Assert.False(string.IsNullOrEmpty(file.StoredPath));
        // SHA256 手算断言（Kind 无关的字节运算）
        Assert.Equal(FileManagementTestHost.Sha256Hex(HelloBytes), file.Sha256, ignoreCase: true);
        // 物理文件真实写入临时目录（步骤 8 Blob 落盘）
        Assert.True(File.Exists(Path.Combine(host.BlobRoot, file.StoredPath)));
        Assert.Equal(1, host.BlobCount());
    }

    [Fact]
    public async Task Upload_ToRootFolder_FolderIdNull()
    {
        using var host = FileManagementTestHost.Create();

        var file = await host.UploadFileAsync(null, "root.txt", HelloBytes);

        Assert.Null(file.FolderId);
        Assert.Equal("root.txt", file.Name);
        Assert.Equal(1, host.BlobCount());
    }

    // ── D10 安全校验负路径：白名单外扩展名 √ / 大小写归一 √ / 超限（seekable + 非 seekable）√ / 非法文件名 √ / ContentType 服务端推导（C5）√ ──

    [Fact]
    public async Task Upload_DisallowedExtension_Throws_NoBlobResidue()
    {
        using var host = FileManagementTestHost.Create();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => host.UploadFileAsync(null, "malware.exe", HelloBytes));

        // 校验先于 Blob（步骤 3 白名单）→ 无残留
        Assert.Equal(0, host.BlobCount());
    }

    [Fact]
    public async Task Upload_UppercaseExtension_NormalizedAndAllowed()
    {
        using var host = FileManagementTestHost.Create();

        // .JPG 大小写归一（C6）→ 白名单 .jpg 放行；ContentType 由扩展名服务端推导（C5）
        var file = await host.UploadFileAsync(null, "photo.JPG", HelloBytes);

        Assert.True(file.Id > 0);
        Assert.Equal("image/jpeg", file.ContentType);
        Assert.Equal(1, host.BlobCount());
    }

    [Fact]
    public async Task Upload_AllowAnyExtension_AcceptsUnknownMime()
    {
        using var host = FileManagementTestHost.Create(options: new FileManagementOptions { AllowAnyExtension = true });

        var file = await host.UploadFileAsync(null, "data.xyz", HelloBytes);

        Assert.True(file.Id > 0);
        // 未知扩展名 → application/octet-stream（C5）
        Assert.Equal("application/octet-stream", file.ContentType);
    }

    [Fact]
    public async Task Upload_SeekableOverMaxSize_Throws()
    {
        using var host = FileManagementTestHost.Create(options: new FileManagementOptions { MaxFileSizeBytes = 1024 });
        var big = new byte[2048];
        await using var stream = new MemoryStream(big);

        // seekable → 步骤 4a：Length 预检 fail-fast（不读流）
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => host.Manager.UploadFileAsync(null, "big.txt", stream, ct: CancellationToken.None));
    }

    [Fact]
    public async Task Upload_NonSeekableOverMaxSize_Throws()
    {
        using var host = FileManagementTestHost.Create(options: new FileManagementOptions { MaxFileSizeBytes = 1024 });
        var big = new byte[2048];
        using var nonSeekable = new NonSeekableStream(new MemoryStream(big));

        // 非 seekable → 步骤 4b：边复制边计数，超限中断读取（C4）
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => host.Manager.UploadFileAsync(null, "big.txt", nonSeekable, ct: CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("./x.txt")]
    [InlineData("../x.txt")]
    [InlineData("a/b.txt")]
    [InlineData("a\\b.txt")]
    [InlineData("C:evil.txt")]
    public async Task Upload_InvalidFileName_Throws(string fileName)
    {
        using var host = FileManagementTestHost.Create();

        // 步骤 1（空/空白）+ 步骤 2（防穿越：. / .. / 分隔符 / 盘符）→ ArgumentException
        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => host.UploadFileAsync(null, fileName, HelloBytes));
    }

    [Fact]
    public async Task Upload_ClientContentTypeIgnored_ServerDerives()
    {
        using var host = FileManagementTestHost.Create();

        // C5：ContentType 服务端推导（.txt → text/plain），不信任客户端伪造值
        var file = await host.UploadFileAsync(null, "doc.txt", HelloBytes, contentType: "text/html");

        Assert.Equal("text/plain", file.ContentType);
    }

    // ── D11 去重：同 SHA256 同目录同名 → 幂等返回既有（Blob 不重复写）；Deduplicate=false 撞 UX 唯一约束 → 业务异常 ──

    [Fact]
    public async Task Upload_DuplicateContentSameFolderSameName_Idempotent_NoDoubleBlob()
    {
        using var host = FileManagementTestHost.Create();
        var folder = await host.CreateFolderAsync("docs", "文档");

        var first = await host.UploadFileAsync(folder.Id, "same.txt", HelloBytes);
        Assert.Equal(1, host.BlobCount());

        var second = await host.UploadFileAsync(folder.Id, "same.txt", HelloBytes);

        // 幂等返回既有（步骤 6 去重预查命中）
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(first.StoredPath, second.StoredPath);
        // Blob 不重复写——临时目录文件数不变
        Assert.Equal(1, host.BlobCount());
    }

    [Fact]
    public async Task Upload_DeduplicateDisabled_SameFolderSameName_ThrowsBusinessException()
    {
        using var host = FileManagementTestHost.Create(options: new FileManagementOptions { Deduplicate = false });
        var folder = await host.CreateFolderAsync("docs", "文档");
        await host.UploadFileAsync(folder.Id, "same.txt", HelloBytes);

        // 关闭去重预查 → 直接撞 UX_ManagedFile_Folder_Name → 捕获转业务异常（P3/C6）
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.UploadFileAsync(folder.Id, "same.txt", HelloBytes));

        Assert.Contains("已存在同名文件", ex.Message);
        // 败者元数据落库失败 → 步骤 10 补偿删除 Blob → 无残留
        Assert.Equal(1, host.BlobCount());
    }

    // ── D12 下载：返回流与元数据；不存在 → 异常 ──

    [Fact]
    public async Task Download_ReturnsStream_MatchesMetadata()
    {
        using var host = FileManagementTestHost.Create();
        var file = await host.UploadFileAsync(null, "down.txt", HelloBytes);

        var result = await host.Manager.DownloadFileAsync(file.Id, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(file.Id, result!.Value.File.Id);
        Assert.Equal("down.txt", result.Value.File.Name);
        using var ms = new MemoryStream();
        await result.Value.Stream.CopyToAsync(ms);
        Assert.Equal(HelloBytes, ms.ToArray());
    }

    [Fact]
    public async Task Download_NonExistent_Throws()
    {
        using var host = FileManagementTestHost.Create();

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.DownloadFileAsync(999_999, CancellationToken.None));
    }

    // ── D13 删除文件：元数据 + Blob 均删（临时目录文件消失） ──

    [Fact]
    public async Task DeleteFile_RemovesMetadataAndBlob()
    {
        using var host = FileManagementTestHost.Create();
        var file = await host.UploadFileAsync(null, "del.txt", HelloBytes);
        Assert.True(File.Exists(Path.Combine(host.BlobRoot, file.StoredPath)));

        await host.Manager.DeleteFileAsync(file.Id, CancellationToken.None);

        // 元数据删 + Blob 删
        Assert.False(File.Exists(Path.Combine(host.BlobRoot, file.StoredPath)));
        Assert.Equal(0, host.BlobCount());
        Assert.Null(await host.Manager.GetFileAsync(file.Id, CancellationToken.None));   // GetFileAsync 不存在返回 null
    }

    // ── D14 重命名文件：目录内唯一约束冲突 → 业务异常（P3）；移动文件：目标目录校验 + 重名检查 + FolderId 更新 ──

    [Fact]
    public async Task RenameFile_Success_NameUpdated()
    {
        using var host = FileManagementTestHost.Create();
        var file = await host.UploadFileAsync(null, "a.txt", HelloBytes);

        await host.Manager.RenameFileAsync(file.Id, "b.txt", CancellationToken.None);

        var renamed = await host.Manager.GetFileAsync(file.Id, CancellationToken.None);
        Assert.Equal("b.txt", renamed.Name);
        Assert.Equal(".txt", renamed.Extension);
    }

    [Fact]
    public async Task RenameFile_DuplicateNameInFolder_ThrowsBusinessException()
    {
        using var host = FileManagementTestHost.Create();
        var folder = await host.CreateFolderAsync("docs", "文档");
        var a = await host.UploadFileAsync(folder.Id, "a.txt", HelloBytes);
        await host.UploadFileAsync(folder.Id, "b.txt", OtherBytes);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Manager.RenameFileAsync(a.Id, "b.txt", CancellationToken.None));

        Assert.Contains("已存在同名文件", ex.Message);
    }

    // ── C1 审核修复：重命名跨扩展名 → 派生列重算 + 白名单不变量在重命名路径成立 ──

    [Fact]
    public async Task RenameFile_CrossExtension_RecomputesDerivedColumns()
    {
        using var host = FileManagementTestHost.Create();
        var file = await host.UploadFileAsync(null, "a.txt", HelloBytes);

        // 同扩展名重命名 → 派生列不变（回归）
        await host.Manager.RenameFileAsync(file.Id, "b.txt", CancellationToken.None);
        var sameExt = await host.Manager.GetFileAsync(file.Id, CancellationToken.None);
        Assert.Equal(".txt", sameExt.Extension);
        Assert.Equal("text/plain", sameExt.ContentType);

        // 跨扩展名重命名 → Extension/ContentType 由新扩展名服务端推导（C5 不变量）
        await host.Manager.RenameFileAsync(file.Id, "b.jpg", CancellationToken.None);
        var crossExt = await host.Manager.GetFileAsync(file.Id, CancellationToken.None);
        Assert.Equal("b.jpg", crossExt.Name);
        Assert.Equal(".jpg", crossExt.Extension);
        Assert.Equal("image/jpeg", crossExt.ContentType);
    }

    [Fact]
    public async Task RenameFile_WhitelistOutsideExtension_Throws()
    {
        using var host = FileManagementTestHost.Create();
        var file = await host.UploadFileAsync(null, "a.txt", HelloBytes);

        // AllowAnyExtension=false（默认）→ 重命名为白名单外扩展名拒绝
        await Assert.ThrowsAsync<ArgumentException>(
            () => host.Manager.RenameFileAsync(file.Id, "a.exe", CancellationToken.None));
    }

    // ── C2 审核修复：根级文件（FolderId=null）同名唯一由应用层保证（SQLite/PostgreSQL 可空唯一索引 NULL 互不相同） ──

    [Fact]
    public async Task Upload_ToRoot_SameName_DuplicateRejected()
    {
        // Deduplicate=false（关闭 SHA256 预查——纯靠应用层同名预检，C2）
        using var host = FileManagementTestHost.Create(options: new FileManagementOptions { Deduplicate = false });
        await host.UploadFileAsync(null, "root.txt", HelloBytes);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.UploadFileAsync(null, "root.txt", OtherBytes));

        Assert.Contains("已存在同名文件", ex.Message);
        Assert.Equal(1, host.BlobCount());   // 预检在 Blob 落盘前——第二次上传无残留（仅首次 1 个 Blob）
    }

    [Fact]
    public async Task MoveFile_TargetFolderNotExists_Throws()
    {
        using var host = FileManagementTestHost.Create();
        var file = await host.UploadFileAsync(null, "m.txt", HelloBytes);

        await Assert.ThrowsAnyAsync<InvalidOperationException>(
            () => host.Manager.MoveFileAsync(file.Id, 999_999, CancellationToken.None));
    }

    [Fact]
    public async Task MoveFile_TargetFolderHasSameName_Throws()
    {
        using var host = FileManagementTestHost.Create();
        var src = await host.CreateFolderAsync("src", "源");
        var dst = await host.CreateFolderAsync("dst", "目标");
        var file = await host.UploadFileAsync(src.Id, "x.txt", HelloBytes);
        await host.UploadFileAsync(dst.Id, "x.txt", OtherBytes);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Manager.MoveFileAsync(file.Id, dst.Id, CancellationToken.None));

        Assert.Contains("已存在同名文件", ex.Message);
    }

    [Fact]
    public async Task MoveFile_Success_UpdatesFolderId()
    {
        using var host = FileManagementTestHost.Create();
        var src = await host.CreateFolderAsync("src", "源");
        var dst = await host.CreateFolderAsync("dst", "目标");
        var file = await host.UploadFileAsync(src.Id, "m.txt", HelloBytes);

        await host.Manager.MoveFileAsync(file.Id, dst.Id, CancellationToken.None);

        var moved = await host.Manager.GetFileAsync(file.Id, CancellationToken.None);
        Assert.Equal(dst.Id, moved.FolderId);
    }

    // ── D16 上传元数据失败补偿：folderId 引用守卫在步骤 9 内失败 → Blob 清理 + 异常传播 ──

    [Fact]
    public async Task Upload_NonexistentFolder_ReferenceGuard_CompensatesBlob()
    {
        using var host = FileManagementTestHost.Create();

        // 上传流程：步骤 8 先写 Blob → 步骤 9 元数据落库（FolderId 引用守卫）失败 → 步骤 10 补偿删除 Blob
        await Assert.ThrowsAnyAsync<Exception>(
            () => host.UploadFileAsync(999_999, "orphan.txt", HelloBytes));

        // 补偿生效：临时目录干净，无残留 Blob
        Assert.Equal(0, host.BlobCount());
    }

    // ── D17 并发同目录同名上传：唯一约束败者 → 业务异常 + 补偿，恰一个成功 ──

    [Fact]
    public async Task ConcurrentUpload_SameFolderSameName_OneSucceeds_OneBusinessException()
    {
        using var host = FileManagementTestHost.Create(options: new FileManagementOptions { Deduplicate = false });
        var folder = await host.CreateFolderAsync("docs", "文档");

        // 去重预查关闭 → 两路都走完整路径（Blob + 元数据），UX_ManagedFile_Folder_Name 唯一约束败者
        // （若保留去重预查，并发窗口可能让后完成者读到先行者已提交 → 幂等双成功，断言不确定性）
        var results = await Task.WhenAll(new[]
        {
            UploadCapture(host, folder.Id, HelloBytes),
            UploadCapture(host, folder.Id, HelloBytes),
        });

        var success = results.Where(r => r.File is not null).ToList();
        var failures = results.Where(r => r.Error is not null).ToList();

        // 恰一个成功 + 一个业务异常（唯一约束败者）
        Assert.Single(success);
        Assert.Single(failures);
        Assert.IsAssignableFrom<InvalidOperationException>(failures[0].Error);
        Assert.Contains("已存在同名文件", failures[0].Error!.Message);
        // 败者补偿删除自身 Blob，成功者 Blob 保留 → 无 Blob 残留
        Assert.Equal(1, host.BlobCount());
    }

    // ── Helpers ──

    private static async Task<(ManagedFileEntity? File, Exception? Error)> UploadCapture(
        FileManagementTestHost host, long? folderId, byte[] content)
    {
        try
        {
            return (await host.UploadFileAsync(folderId, "race.txt", content), null);
        }
        catch (Exception ex)
        {
            return (null, ex);
        }
    }
}