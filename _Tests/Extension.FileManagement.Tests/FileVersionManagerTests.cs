using System;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace TKWF.Ext.FileManagement.Tests;

/// <summary>
/// V0.2.0：文件版本化测试（D1-D6 + D13-D16）。
/// <para>覆盖：首版/同名不同内容新版本/去重幂等/根级版本化（P1-1）/删除清理（P1-2）/
/// 回滚指针复用/max+1 非连续/并发 UX 约束/Deduplicate=false 幂等。</para>
/// </summary>
public class FileVersionManagerTests
{
    // ── D1-D3：上传版本生成 ──

    [Fact]
    public async Task Upload_FirstVersion_Creates_Version1()
    {
        using var host = FileManagementTestHost.Create();
        var file = await host.UploadFileAsync(null, "report.txt", "v1 content"u8.ToArray());

        var versions = await host.Manager.GetFileVersionsAsync(file.Id);
        Assert.Single(versions);
        Assert.Equal(1, versions[0].Version);
        Assert.Equal(file.StoredPath, versions[0].StoredPath);
        Assert.Equal(file.Sha256, versions[0].Sha256);
        Assert.Equal(file.Size, versions[0].Size);
    }

    [Fact]
    public async Task Upload_SameNameDifferentContent_Creates_Version2_UpdatesMainPointer()
    {
        using var host = FileManagementTestHost.Create();
        var v1 = await host.UploadFileAsync(null, "report.txt", "v1 content"u8.ToArray());
        string v1Path = v1.StoredPath;

        var v2 = await host.UploadFileAsync(null, "report.txt", "v2 content"u8.ToArray());

        // 主表指针更新（同 Id，新 StoredPath/Sha256）
        Assert.Equal(v1.Id, v2.Id);
        Assert.NotEqual(v1Path, v2.StoredPath);
        Assert.NotEqual(v1.Sha256, v2.Sha256);

        // 版本 1 + 版本 2 均保留
        var versions = await host.Manager.GetFileVersionsAsync(v2.Id);
        Assert.Equal(2, versions.Count);
        Assert.Equal([1, 2], versions.Select(v => v.Version).ToArray());
        Assert.Equal(v1Path, versions[0].StoredPath);
    }

    [Fact]
    public async Task Upload_Deduplicate_SameContent_ReturnsExisting_NoNewVersion()
    {
        using var host = FileManagementTestHost.Create();
        var v1 = await host.UploadFileAsync(null, "report.txt", "same content"u8.ToArray());

        var dedup = await host.UploadFileAsync(null, "report.txt", "same content"u8.ToArray());

        Assert.Equal(v1.Id, dedup.Id);   // 幂等返回既有
        var versions = await host.Manager.GetFileVersionsAsync(v1.Id);
        Assert.Single(versions);         // 不产生新版本
    }

    [Fact]
    public async Task Upload_DeduplicateFalse_SameContent_ReturnsExisting_NoRedundantVersion()
    {
        // Oracle P1-3 分支 C：Deduplicate=false + 同内容 → 幂等返回不产生冗余版本行
        using var host = FileManagementTestHost.Create(new FileManagementOptions { Deduplicate = false });
        var v1 = await host.UploadFileAsync(null, "report.txt", "same content"u8.ToArray());

        var again = await host.UploadFileAsync(null, "report.txt", "same content"u8.ToArray());

        Assert.Equal(v1.Id, again.Id);
        var versions = await host.Manager.GetFileVersionsAsync(v1.Id);
        Assert.Single(versions);         // 同内容不消费新版本行
    }

    // ── D13（P1-1）：根级版本化 ──

    [Fact]
    public async Task Upload_RootLevel_SameNameDifferentContent_CreatesVersion()
    {
        // Oracle P1-1：C2 根级同名预检移除后，根级同名不同内容 → 新版本（原 v0.1.0 抛"该目录下已存在同名文件"）
        using var host = FileManagementTestHost.Create();
        var v1 = await host.UploadFileAsync(null, "root.txt", "v1 content"u8.ToArray());

        var v2 = await host.UploadFileAsync(null, "root.txt", "v2 content"u8.ToArray());

        Assert.Equal(v1.Id, v2.Id);   // 根级同名 → 同文件新版本（不再抛异常）
        var versions = await host.Manager.GetFileVersionsAsync(v1.Id);
        Assert.Equal(2, versions.Count);
    }

    // ── D4-D6：版本列表/详情/回滚 ──

    [Fact]
    public async Task GetFileVersions_ReturnsOrdered_VersionDetail_ExactHit()
    {
        using var host = FileManagementTestHost.Create();
        var v1 = await host.UploadFileAsync(null, "report.txt", "v1 content"u8.ToArray());
        await host.UploadFileAsync(null, "report.txt", "v2 content"u8.ToArray());

        var versions = await host.Manager.GetFileVersionsAsync(v1.Id);
        Assert.Equal([1, 2], versions.Select(v => v.Version).ToArray());   // Version 升序

        var detail = await host.Manager.GetFileVersionAsync(v1.Id, 1);
        Assert.NotNull(detail);
        Assert.Equal(1, detail!.Version);

        var missing = await host.Manager.GetFileVersionAsync(v1.Id, 99);
        Assert.Null(missing);
    }

    [Fact]
    public async Task Rollback_SwitchesMainPointer_And_Creates_NewVersionRow()
    {
        using var host = FileManagementTestHost.Create();
        var v1 = await host.UploadFileAsync(null, "report.txt", "v1 content"u8.ToArray());
        string v1Path = v1.StoredPath;
        string v1Sha = v1.Sha256;
        await host.UploadFileAsync(null, "report.txt", "v2 content"u8.ToArray());

        var rolledBack = await host.Manager.RollbackFileAsync(v1.Id, 1);

        // 主表指针切回 v1（StoredPath/Sha256 与 v1 一致——指针复用不复制字节）
        Assert.Equal(v1Path, rolledBack.StoredPath);
        Assert.Equal(v1Sha, rolledBack.Sha256);

        // 新版本行 Version=3（回滚也是新版本，可追溯）
        var versions = await host.Manager.GetFileVersionsAsync(v1.Id);
        Assert.Equal(3, versions.Count);
        Assert.Equal(3, versions[2].Version);
        Assert.Equal(v1Path, versions[2].StoredPath);   // 指针复用（与 v1 相同 StoredPath）
    }

    [Fact]
    public async Task Rollback_NonExistentVersion_Throws()
    {
        using var host = FileManagementTestHost.Create();
        var file = await host.UploadFileAsync(null, "report.txt", "v1 content"u8.ToArray());

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => host.Manager.RollbackFileAsync(file.Id, 99));
    }

    // ── D15（P2-8）：max+1 非连续版本计算 ──

    [Fact]
    public async Task Rollback_NonContiguousVersions_MaxPlusOne()
    {
        // 版本 1/2 存在 → 回滚版本 1 生成版本 3（max=2 → +1=3）；再回滚版本 2 生成版本 4
        using var host = FileManagementTestHost.Create();
        var file = await host.UploadFileAsync(null, "report.txt", "v1 content"u8.ToArray());
        await host.UploadFileAsync(null, "report.txt", "v2 content"u8.ToArray());

        await host.Manager.RollbackFileAsync(file.Id, 1);   // 版本 3
        await host.Manager.RollbackFileAsync(file.Id, 2);   // 版本 4（max=3 → +1）

        var versions = await host.Manager.GetFileVersionsAsync(file.Id);
        Assert.Equal([1, 2, 3, 4], versions.Select(v => v.Version).ToArray());
    }

    // ── D14（P1-2）：删除清理 ──

    [Fact]
    public async Task DeleteFile_CleansAllVersionRows_And_AllVersionBlobs()
    {
        using var host = FileManagementTestHost.Create();
        var file = await host.UploadFileAsync(null, "report.txt", "v1 content"u8.ToArray());
        await host.UploadFileAsync(null, "report.txt", "v2 content"u8.ToArray());
        await host.RollbackFileAsync(file.Id, 1);   // 版本 3（指针复用 v1 StoredPath）

        int blobsBefore = host.BlobCount();
        Assert.Equal(2, blobsBefore);   // v1 + v2 两个物理 Blob（回滚复用不新增）

        await host.Manager.DeleteFileAsync(file.Id);

        // 版本行全删（文件已删——GetFileVersionsAsync 有存在守卫，经版本 Store 直查验证）
        var versionStore = host.GetRequiredService<IManagedFileVersionStore>();
        var versions = await versionStore.GetByFileAsync(file.Id);
        Assert.Empty(versions);
        // Blob 全清（Distinct 去重——回滚共享 StoredPath 不重复删）
        Assert.Equal(0, host.BlobCount());
    }

    // ── D16（P2-5）：并发版本上传 UX 约束 ──

    [Fact]
    public async Task Concurrent_Upload_DifferentContent_SameFile_UniqueConstraint_OneWins()
    {
        using var host = FileManagementTestHost.Create();
        var v1 = await host.UploadFileAsync(null, "report.txt", "v1 content"u8.ToArray());

        // 两并发上传不同内容 → 都读 max=1 → 都插 Version=2 → UX 唯一约束败者补偿
        Task<ManagedFileEntity> Upload(string content)
            => host.UploadFileAsync(null, "report.txt", System.Text.Encoding.UTF8.GetBytes(content));

        var results = await Task.WhenAll(
            Upload("concurrent-A-content"),
            Upload("concurrent-B-content"));

        // 至少一个成功；成功者主表指针为某并发内容
        var file = results.First(r => r != null && r.Id == v1.Id);
        var versions = await host.Manager.GetFileVersionsAsync(v1.Id);
        Assert.True(versions.Count >= 2);   // 首版 + 至少一个并发版本
    }
}
