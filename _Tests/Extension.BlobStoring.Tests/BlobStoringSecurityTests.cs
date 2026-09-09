using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// BlobStoring 安全与配置绑定测试（评审 C1/C2 修复验证）：
/// <list type="bullet">
/// <item>① 四入口（Upload/Download/Delete/Exists）路径防穿越——非法输入抛 <see cref="ArgumentException"/>（fail-closed，不再静默返回 null/false）；</item>
/// <item>② 合法路径回归——内部生成的 <c>{guid}/{name}</c> 路径不受影响（不得破坏既有合法行为）；</item>
/// <item>③ <see cref="BlobStoringExtensionInitializer{TUserInfo}"/> Options 绑定——<c>TKWF:BlobStoring</c> 配置节 → <see cref="BlobStoringOptions"/>。</item>
/// </list>
/// </summary>
public class BlobStoringSecurityTests
{
    // ── ① UploadAsync 防穿越（name 参数——Path.Combine(Guid, name) 中 name 逃逸） ──

    [Theory]
    [InlineData("../../evil.txt")]
    [InlineData("..\\..\\evil.txt")]
    [InlineData("a/../b.txt")]
    [InlineData("C:evil.txt")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task UploadAsync_UnsafeName_ThrowsArgumentException(string name)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.UploadAsync(name, new MemoryStream(new byte[] { 1 }), "text/plain"));
    }

    // ── ① Download/Delete/Exists 防穿越（path 参数） ──

    [Theory]
    [InlineData("../../x")]
    [InlineData("..\\..\\x")]
    [InlineData("a/../x")]
    [InlineData("C:\\x")]
    [InlineData("C:/x")]
    [InlineData(".")]
    [InlineData("..")]
    [InlineData("")]
    [InlineData("   ")]
    public async Task DownloadAsync_UnsafePath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.DownloadAsync(path));
    }

    [Theory]
    [InlineData("../../x")]
    [InlineData("..\\..\\x")]
    [InlineData("a/../x")]
    [InlineData("C:\\x")]
    [InlineData("C:/x")]
    public async Task DeleteAsync_UnsafePath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.DeleteAsync(path));
    }

    [Theory]
    [InlineData("../../x")]
    [InlineData("..\\..\\x")]
    [InlineData("a/../x")]
    [InlineData("C:\\x")]
    [InlineData("C:/x")]
    public async Task ExistsAsync_UnsafePath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ExistsAsync(path));
    }

    // ── ② 合法路径回归（防穿越不得破坏内部生成的 {guid}/{name} 路径） ──

    [Fact]
    public async Task UploadThenDownload_LegalPath_Works()
    {
        var service = CreateService();

        var content = Encoding.UTF8.GetBytes("hello blob");
        var ct = TestContext.Current.CancellationToken;
        var info = await service.UploadAsync("photo.png", new MemoryStream(content), "image/png", ct);

        Assert.NotNull(info);
        Assert.NotNull(info!.Path);
        Assert.Contains(Path.DirectorySeparatorChar, info.Path); // 内部路径形如 {guid}/{name}（多段相对路径）

        Assert.True(await service.ExistsAsync(info.Path, ct));

        var downloaded = await service.DownloadAsync(info.Path, ct);
        Assert.NotNull(downloaded);
        using var reader = new StreamReader(downloaded!);
        Assert.Equal("hello blob", await reader.ReadToEndAsync(ct));

        Assert.True(await service.DeleteAsync(info.Path, ct));
        Assert.False(await service.ExistsAsync(info.Path, ct));
    }

    [Fact]
    public async Task DownloadAsync_MultiSegmentLegalPath_DoesNotThrow()
    {
        // {guid}/{name} 形式的多段合法相对路径——不存在返回 null，而非抛 ArgumentException
        var service = CreateService();

        var result = await service.DownloadAsync("0123456789abcdef0123456789abcdef/missing.txt");

        Assert.Null(result);
    }

    // ── ③ Options 绑定（BlobStoringExtensionInitializer → TKWF:BlobStoring 节） ──

    [Fact]
    public void ConfigureServices_Registers_IConfigureOptions_Descriptor()
    {
        var services = new ServiceCollection();
        new BlobStoringExtensionInitializer<BlobStoringUserInfo>().ConfigureServices(services);

        // AddOptions<BlobStoringOptions>().BindConfiguration(...) → 注册 IConfigureOptions<BlobStoringOptions>
        var descriptor = services.FirstOrDefault(d => d.ServiceType == typeof(IConfigureOptions<BlobStoringOptions>));

        Assert.NotNull(descriptor);
    }

    [Fact]
    public void ConfigureServices_BindConfiguration_AppliesTKWFBlobStoringSection()
    {
        var services = new ServiceCollection();

        // 模拟消费方 appsettings.json：TKWF:BlobStoring 节 → BindConfiguration 绑定生效
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder()
            .Add(new InMemoryConfigurationSource(new Dictionary<string, string?>
            {
                ["TKWF:BlobStoring:RootPath"] = "/data/blobs-custom",
                ["TKWF:BlobStoring:IsEnabled"] = "false"
            }))
            .Build());

        new BlobStoringExtensionInitializer<BlobStoringUserInfo>().ConfigureServices(services);

        using var sp = services.BuildServiceProvider();
        var options = sp.GetRequiredService<IOptions<BlobStoringOptions>>().Value;

        Assert.Equal("/data/blobs-custom", options.RootPath);
        Assert.False(options.IsEnabled);
    }

    // ── Test helpers ──

    /// <summary>创建 LocalStorageService（临时根目录 + OptionsWrapper + NullLogger）。</summary>
    private static LocalStorageService CreateService(string? rootPath = null)
    {
        var root = rootPath ?? Path.Combine(Path.GetTempPath(), "tkwf-blobtest-" + Guid.NewGuid().ToString("N"));
        return new LocalStorageService(
            new OptionsWrapper<BlobStoringOptions>(new BlobStoringOptions { RootPath = root }),
            NullLogger<LocalStorageService>.Instance);
    }

    /// <summary>最小内存配置源（避免测试引入 Configuration.Memory 包——对齐 Settings.Tests 先例）。</summary>
    private sealed class InMemoryConfigurationSource : IConfigurationSource
    {
        private readonly IDictionary<string, string?> _values;

        public InMemoryConfigurationSource(IDictionary<string, string?> values) => _values = values;

        public IConfigurationProvider Build(IConfigurationBuilder builder)
            => new InMemoryConfigurationProvider(_values);
    }

    /// <summary>最小内存配置提供者：直接把字典作为扁平键值暴露。</summary>
    private sealed class InMemoryConfigurationProvider : ConfigurationProvider
    {
        private readonly IDictionary<string, string?> _values;

        public InMemoryConfigurationProvider(IDictionary<string, string?> values) => _values = values;

        public override void Load() => Data = new Dictionary<string, string?>(_values, StringComparer.OrdinalIgnoreCase);
    }
}
