using System;
using System.IO;
using System.Threading.Tasks;
using Xunit;

namespace TKWF.Ext.FileManagement.Tests;

/// <summary>
/// V0.3.0：用户级配额测试（ADR-FileManagement-用户级配额所有权语义）。
/// <para>覆盖：用户文件数/容量配额拒绝 + 等值边界 + 匿名/系统/非数值 UserId 降级 + 多用户隔离
/// + 所有权保留（版本化/回滚不动 OwnerId）+ 删除释放配额 + OwnerId 列持久化。</para>
/// <para>V4.10.53（领域自治根治，正确路线）：宿主改走生产路径——门面经 <c>host.ManagerFor(host.User)</c>
/// 直构 FileManager（internal 类 + IVT；可配置桩经基类 <c>User</c> 精确控制认证/UserId/SystemActor——
/// skill §4.5 分层单测模式；测试项目无主框架 DomainUserContext IVT，认证路径不能经守卫工厂 AOP 注入，
/// 匿名 AOP 路径（<see cref="FileManagementTestHost.Manager"/>）覆盖生产解析语义）。</para>
/// </summary>
public class FileUserQuotaTests
{
    /// <summary>构建带认证用户（userId）的宿主——经可配置桩切换认证状态（V0.3.0 用户配额语义）。</summary>
    private static FileManagementTestHost CreateAuthHost(
        FileManagementOptions? options = null, string? userId = "42", bool isSystemActor = false)
    {
        var host = FileManagementTestHost.Create(options);
        host.User.AsAuthenticated(userId ?? "42");
        host.User.IsSystemActor = isSystemActor;
        return host;
    }

    /// <summary>上传辅助：byte[] → Stream（IFileManager 契约签名接收 Stream；host.UploadFileAsync 辅助仅走匿名 Manager）。</summary>
    private static async Task<ManagedFileEntity> UploadAsync(
        IFileManager manager, long? folderId, string fileName, byte[] content)
    {
        await using var stream = new MemoryStream(content, writable: false);
        return await manager.UploadFileAsync(folderId, fileName, stream);
    }

    // ── 用户文件数配额 ──

    [Fact]
    public async Task MaxUserFilesCount_Exceeded_Throws_NoBlobLeft()
    {
        using var host = CreateAuthHost(new FileManagementOptions { MaxUserFilesCount = 1 });
        var manager = host.ManagerFor(host.User);
        var folder = await manager.CreateFolderAsync("docs", "文档");
        await UploadAsync(manager, folder.Id, "a.txt", "content-a"u8.ToArray());

        int blobsBefore = host.BlobCount();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => UploadAsync(manager, folder.Id, "b.txt", "content-b"u8.ToArray()));

        Assert.Equal(blobsBefore, host.BlobCount());   // 超限不产生 Blob（检查在 Blob 落盘前）
    }

    [Fact]
    public async Task MaxUserFilesCount_NotConfigured_NoLimit()
    {
        using var host = CreateAuthHost();   // 默认 null 不限制
        var manager = host.ManagerFor(host.User);
        var folder = await manager.CreateFolderAsync("docs", "文档");
        await UploadAsync(manager, folder.Id, "a.txt", "content-a"u8.ToArray());
        var b = await UploadAsync(manager, folder.Id, "b.txt", "content-b"u8.ToArray());

        Assert.NotNull(b);
    }

    // ── 用户容量配额 ──

    [Fact]
    public async Task MaxUserSizeBytes_Exceeded_Throws_NoBlobLeft()
    {
        using var host = CreateAuthHost(new FileManagementOptions { MaxUserSizeBytes = 20 });
        var manager = host.ManagerFor(host.User);
        var folder = await manager.CreateFolderAsync("docs", "文档");
        await UploadAsync(manager, folder.Id, "a.txt", "0123456789"u8.ToArray());   // 10 字节

        int blobsBefore = host.BlobCount();
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => UploadAsync(manager, folder.Id, "b.txt", "0123456789abcdef"u8.ToArray()));   // 16 → 10+16 > 20

        Assert.Equal(blobsBefore, host.BlobCount());
    }

    [Fact]
    public async Task MaxUserSizeBytes_EqualBoundary_Allowed()
    {
        // Oracle 定案：等值边界（current + contentLength > limit）——相等允许
        using var host = CreateAuthHost(new FileManagementOptions { MaxUserSizeBytes = 20 });
        var manager = host.ManagerFor(host.User);
        var folder = await manager.CreateFolderAsync("docs", "文档");
        await UploadAsync(manager, folder.Id, "a.txt", "0123456789"u8.ToArray());   // 10 字节
        var b = await UploadAsync(manager, folder.Id, "b.txt", "0123456789"u8.ToArray());   // 10 → 10+10 = 20 不超

        Assert.NotNull(b);
    }

    // ── 降级：匿名 / 系统 / 非数值 ──

    [Fact]
    public async Task Anonymous_UserQuotaSkipped_GlobalStillApplies()
    {
        // 匿名（默认桩）→ OwnerId null → 用户配额跳过；全局配额仍生效
        using var host = FileManagementTestHost.Create(new FileManagementOptions
        {
            MaxUserSizeBytes = 5,       // 用户配额（匿名 → 跳过）
            MaxTotalSizeBytes = 15      // 全局兜底（生效）
        });
        var manager = host.ManagerFor(host.User);   // 匿名（默认桩）
        var folder = await manager.CreateFolderAsync("docs", "文档");
        await UploadAsync(manager, folder.Id, "a.txt", "0123456789"u8.ToArray());   // 10 字节（用户配额 5 被跳过，全局 15 内允许）

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => UploadAsync(manager, folder.Id, "b.txt", "0123456789"u8.ToArray()));   // 10+10 > 15 全局拒绝
    }

    [Fact]
    public async Task SystemActor_UserQuotaSkipped()
    {
        // 系统账号（IsSystemActor）→ OwnerId null → 用户配额跳过（全局兜底）
        using var host = CreateAuthHost(new FileManagementOptions { MaxUserFilesCount = 1 }, userId: "42", isSystemActor: true);
        var manager = host.ManagerFor(host.User);
        var folder = await manager.CreateFolderAsync("docs", "文档");
        await UploadAsync(manager, folder.Id, "a.txt", "content-a"u8.ToArray());
        var b = await UploadAsync(manager, folder.Id, "b.txt", "content-b"u8.ToArray());   // 系统账号不受用户配额限制

        Assert.NotNull(b);
        Assert.Null(b.OwnerId);   // 系统上传不归属
    }

    [Fact]
    public async Task NonNumericUserId_UserQuotaSkipped_GlobalStillApplies()
    {
        // GUID/字符串 UserId → long.TryParse 失败 → OwnerId null → 用户配额跳过（全局兜底）
        using var host = CreateAuthHost(new FileManagementOptions
        {
            MaxUserFilesCount = 1,   // 用户配额（非数值 → 跳过）
            MaxTotalSizeBytes = 100  // 全局兜底
        }, userId: "guid-user-id-abc");
        var manager = host.ManagerFor(host.User);
        var folder = await manager.CreateFolderAsync("docs", "文档");
        await UploadAsync(manager, folder.Id, "a.txt", "content-a"u8.ToArray());
        var b = await UploadAsync(manager, folder.Id, "b.txt", "content-b"u8.ToArray());   // 用户配额 1 被跳过

        Assert.NotNull(b);
    }

    // ── 多用户隔离 ──

    [Fact]
    public async Task UserA_QuotaFull_DoesNotAffect_UserB()
    {
        using var host = CreateAuthHost(new FileManagementOptions { MaxUserFilesCount = 1 }, userId: "42");
        var managerA = host.ManagerFor(host.User);   // 用户 42
        var folder = await managerA.CreateFolderAsync("docs", "文档");
        await UploadAsync(managerA, folder.Id, "a.txt", "content-a"u8.ToArray());   // 用户 42 达配额

        // 用户 43 上传不受影响（切换桩 userId——ManagerFor 每次直构新 FileManager 读当前桩）
        host.User.AsAuthenticated("43");
        var managerB = host.ManagerFor(host.User);   // 用户 43
        var folderB = await managerB.CreateFolderAsync("docsB", "文档B");
        var b = await UploadAsync(managerB, folderB.Id, "b.txt", "content-b"u8.ToArray());

        Assert.NotNull(b);
    }

    // ── 所有权保留（Oracle 条件 8）──

    [Fact]
    public async Task VersionUpload_OwnerId_Unchanged()
    {
        // 同宿主内：上传 v1 → 上传同名不同内容 v2 → 主表 OwnerId 仍为原 owner（所有权保留，CanUpdate=false 语义）
        using var host = CreateAuthHost(userId: "42");
        var manager = host.ManagerFor(host.User);
        var folder = await manager.CreateFolderAsync("docs", "文档");
        var v1 = await UploadAsync(manager, folder.Id, "doc.txt", "version-1-content"u8.ToArray());
        Assert.Equal(42L, v1.OwnerId);

        var v2 = await UploadAsync(manager, folder.Id, "doc.txt", "version-2-different-content"u8.ToArray());
        Assert.Equal(42L, v2.OwnerId);   // 新版本分支不动 OwnerId（所有权保留）
    }

    [Fact]
    public async Task Rollback_OwnerId_Unchanged()
    {
        using var host = CreateAuthHost(userId: "42");
        var manager = host.ManagerFor(host.User);
        var folder = await manager.CreateFolderAsync("docs", "文档");
        var v1 = await UploadAsync(manager, folder.Id, "doc.txt", "version-1-content"u8.ToArray());
        Assert.Equal(42L, v1.OwnerId);

        var v2 = await UploadAsync(manager, folder.Id, "doc.txt", "version-2-different-content"u8.ToArray());
        Assert.Equal(42L, v2.OwnerId);

        var rolled = await manager.RollbackFileAsync(v2.Id, 1);
        Assert.Equal(42L, rolled.OwnerId);   // 回滚不动 OwnerId（Oracle 条件 3）
    }

    // ── 删除释放配额（Oracle 条件 4）──

    [Fact]
    public async Task DeleteFile_ReleasesUserQuota()
    {
        using var host = CreateAuthHost(new FileManagementOptions { MaxUserSizeBytes = 30 });
        var manager = host.ManagerFor(host.User);
        var folder = await manager.CreateFolderAsync("docs", "文档");
        var f = await UploadAsync(manager, folder.Id, "a.txt", new byte[20]);   // 20 字节

        await manager.DeleteFileAsync(f.Id);

        // 删除后用户配额释放——可再上传 20 字节（20 < 30 允许）
        var b = await UploadAsync(manager, folder.Id, "b.txt", new byte[20]);
        Assert.NotNull(b);
    }

    // ── OwnerId 列持久化 ──

    [Fact]
    public async Task OwnerId_Persisted_OnUpload()
    {
        using var host = CreateAuthHost(userId: "42");
        var manager = host.ManagerFor(host.User);
        var folder = await manager.CreateFolderAsync("docs", "文档");
        var f = await UploadAsync(manager, folder.Id, "a.txt", "content"u8.ToArray());

        Assert.Equal(42L, f.OwnerId);
        var reloaded = await host.FileDataService.GetByIdAsync(f.Id);
        Assert.Equal(42L, reloaded!.OwnerId);   // 列落库可读
    }

    [Fact]
    public async Task AnonymousUpload_OwnerId_Null()
    {
        using var host = FileManagementTestHost.Create();   // 默认匿名桩
        var manager = host.ManagerFor(host.User);   // 匿名
        var folder = await manager.CreateFolderAsync("docs", "文档");
        var f = await UploadAsync(manager, folder.Id, "a.txt", "content"u8.ToArray());

        Assert.Null(f.OwnerId);   // 匿名上传不归属
    }
}
