using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// LocalStorageService 测试——使用临时目录验证真实文件读写 + 异常静默。
/// </summary>
public class LocalStorageServiceTests : IDisposable
{
    private readonly string _tempDir;

    public LocalStorageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "BlobStoringTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }

    private LocalStorageService CreateService()
    {
        var options = Options.Create(new BlobStoringOptions { RootPath = _tempDir, IsEnabled = true });
        var logger = new FakeLogger<LocalStorageService>();
        return new LocalStorageService(options, logger);
    }

    [Fact]
    public async Task UploadAsync_ValidContent_ReturnsBlobInfo()
    {
        // Arrange
        var service = CreateService();
        var content = new MemoryStream(Encoding.UTF8.GetBytes("Hello, Blob!"));

        // Act
        var result = await service.UploadAsync("test.txt", content, "text/plain");

        // Assert
        Assert.Equal("test.txt", result.Name);
        Assert.False(string.IsNullOrEmpty(result.Path));
        Assert.Equal("text/plain", result.ContentType);
        Assert.True(result.Size > 0);
    }

    [Fact]
    public async Task UploadAsync_ThenDownload_ReturnsSameContent()
    {
        // Arrange
        var service = CreateService();
        var original = Encoding.UTF8.GetBytes("Download test content");
        using var uploadStream = new MemoryStream(original);

        // Act
        var blobInfo = await service.UploadAsync("download.txt", uploadStream, "text/plain");
        using var downloadStream = await service.DownloadAsync(blobInfo.Path);

        // Assert
        Assert.NotNull(downloadStream);
        using var ms = new MemoryStream();
        await downloadStream!.CopyToAsync(ms);
        Assert.Equal(original, ms.ToArray());
    }

    [Fact]
    public async Task DeleteAsync_ExistingFile_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();
        using var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes("Delete me"));
        var blobInfo = await service.UploadAsync("delete.txt", uploadStream, "text/plain");

        // Act
        var result = await service.DeleteAsync(blobInfo.Path);

        // Assert
        Assert.True(result);

        // 文件已不存在
        var exists = await service.ExistsAsync(blobInfo.Path);
        Assert.False(exists);
    }

    [Fact]
    public async Task ExistsAsync_ExistingFile_ReturnsTrue()
    {
        // Arrange
        var service = CreateService();
        using var uploadStream = new MemoryStream(Encoding.UTF8.GetBytes("Exist check"));
        var blobInfo = await service.UploadAsync("exists.txt", uploadStream, "text/plain");

        // Act
        var result = await service.ExistsAsync(blobInfo.Path);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public async Task UploadAsync_ExceptionThrown_LogsWarningAndDoesNotThrow()
    {
        // Arrange — 使用一个只读目录来触发异常
        var readOnlyDir = Path.Combine(_tempDir, "readonly");
        Directory.CreateDirectory(readOnlyDir);
        var options = Options.Create(new BlobStoringOptions { RootPath = readOnlyDir });
        var logger = new FakeLogger<LocalStorageService>();
        var service = new LocalStorageService(options, logger);

        // 在 macOS/Linux 上文件系统可能不会抛异常，所以用一个不存在的无效路径模拟
        var invalidOptions = Options.Create(new BlobStoringOptions { RootPath = "Z:\\nonexistent\\deeply\\nested" });
        var service2 = new LocalStorageService(invalidOptions, logger);
        var content = new MemoryStream(Encoding.UTF8.GetBytes("test"));

        // Act — should not throw
        var result = await service2.UploadAsync("test.txt", content, "text/plain");

        // Assert — V0.1.1 评审修复：失败返回 null（区分成功/失败）
        Assert.Null(result);
        Assert.Single(logger.Warnings);
        Assert.Contains("Blob 上传失败", logger.Warnings[0]);
    }

    [Fact]
    public async Task DownloadAsync_NonExistent_ReturnsNull()
    {
        // Arrange
        var service = CreateService();

        // Act
        var result = await service.DownloadAsync("nonexistent/file.txt");

        // Assert
        Assert.Null(result);
    }

    // ── Test helpers ──

    private sealed class FakeLogger<T> : ILogger<T>
    {
        public List<string> Warnings { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning)
                Warnings.Add(formatter(state, exception));
        }
    }
}
