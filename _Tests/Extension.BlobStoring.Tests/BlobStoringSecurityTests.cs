using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// BlobStoring 安全与配置绑定测试（评审 C1/C2 + V0.3.1 BL-P0 修复验证）：
/// <list type="bullet">
/// <item>① 四入口（Upload/Download/Delete/Exists）路径防穿越——非法输入抛 <see cref="ArgumentException"/>（fail-closed，不再静默返回 null/false）；</item>
/// <item>② 合法路径回归——内部生成的 <c>{guid}/{name}</c> 路径不受影响（不得破坏既有合法行为）；</item>
/// <item>③ <see cref="BlobStoringExtensionInitializer{TUserInfo}"/> Options 绑定——<c>TKWF:BlobStoring</c> 配置节 → <see cref="BlobStoringOptions"/>。</item>
/// </list>
/// V0.3.1（BL-P0 rooted/UNC 漏洞闭环）新增：白名单整路径锚定拒绝形态（绝对路径/UNC/盘符+ADS/前导分隔符/空段/尾分隔符/尾点/尾随换行/尾空格/短名/Unicode）+ 规范化前缀双保险正/边界 + 白名单兼容正用例。
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
            () => service.UploadAsync(name, new MemoryStream(new byte[] { 1 }), "text/plain", ct: TestContext.Current.CancellationToken));
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

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.DownloadAsync(path, ct: TestContext.Current.CancellationToken));
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

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.DeleteAsync(path, ct: TestContext.Current.CancellationToken));
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

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ExistsAsync(path, ct: TestContext.Current.CancellationToken));
    }

    // ── ①（V0.3.1 BL-P0 补漏）A 面 Rooted 绝对路径——四入口全覆盖 ──

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\System32\\x")]
    [InlineData("/a/b")]
    public async Task UploadAsync_RootedName_ThrowsArgumentException(string name)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.UploadAsync(name, new MemoryStream(new byte[] { 1 }), "text/plain", ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\System32\\x")]
    [InlineData("/a/b")]
    public async Task DownloadAsync_RootedPath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.DownloadAsync(path, ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\System32\\x")]
    [InlineData("/a/b")]
    public async Task DeleteAsync_RootedPath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.DeleteAsync(path, ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("/etc/passwd")]
    [InlineData("\\Windows\\System32\\x")]
    [InlineData("/a/b")]
    public async Task ExistsAsync_RootedPath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ExistsAsync(path, ct: TestContext.Current.CancellationToken));
    }

    // ── ①（V0.3.1 BL-P0 补漏）B 面 UNC——四入口全覆盖 ──

    [Theory]
    [InlineData("//server/share/x")]
    [InlineData("\\\\server\\share\\x")]
    public async Task UploadAsync_UncName_ThrowsArgumentException(string name)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.UploadAsync(name, new MemoryStream(new byte[] { 1 }), "text/plain", ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("//server/share/x")]
    [InlineData("\\\\server\\share\\x")]
    public async Task DownloadAsync_UncPath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.DownloadAsync(path, ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("//server/share/x")]
    [InlineData("\\\\server\\share\\x")]
    public async Task DeleteAsync_UncPath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.DeleteAsync(path, ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("//server/share/x")]
    [InlineData("\\\\server\\share\\x")]
    public async Task ExistsAsync_UncPath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ExistsAsync(path, ct: TestContext.Current.CancellationToken));
    }

    // ── ①（V0.3.1 BL-P0 补漏）C 面 盘符/ADS（Upload 曾漏测 ADS 形态） ──

    [Theory]
    [InlineData("C:\\evil")]
    [InlineData("good.txt::$DATA")]
    public async Task UploadAsync_DriveOrAdsName_ThrowsArgumentException(string name)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.UploadAsync(name, new MemoryStream(new byte[] { 1 }), "text/plain", ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("C:\\evil")]
    [InlineData("good.txt::$DATA")]
    public async Task ExistsAsync_DriveOrAdsPath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ExistsAsync(path, ct: TestContext.Current.CancellationToken));
    }

    // ── ①（V0.3.1 BL-P0 补漏）E 面 前导分隔符/空段/尾分隔符——上传入口 + 读路径抽测 ──

    [Theory]
    [InlineData("/etc/passwd")]   // 与 A 复用（前导分隔符使能形态）
    [InlineData("a//b")]          // 空段——整路径锚定排除（不复用 RemoveEmptyEntries 吞并）
    [InlineData("a/b/")]          // 尾分隔符——段尾须字母数字必败
    public async Task UploadAsync_LeadingOrEmptySegment_ThrowsArgumentException(string name)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.UploadAsync(name, new MemoryStream(new byte[] { 1 }), "text/plain", ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("a//b")]
    [InlineData("a/b/")]
    public async Task DownloadAsync_EmptyOrTrailingSegment_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.DownloadAsync(path, ct: TestContext.Current.CancellationToken));
    }

    // ── ①（V0.3.1 BL-P0 补漏）F 面 尾点/尾空格/前导点/短名/Unicode/尾随换行——上传入口 + 读路径抽测 ──

    [Theory]
    [InlineData("evil.txt.")]   // 尾点——段尾须字母数字必败
    [InlineData("evil ")]       // 尾空格——空格不入字符集
    [InlineData(".name.txt")]   // 前导点——段首须字母数字
    [InlineData("~1/evil.txt")] // 短名 '~'——不入字符集
    [InlineData("你好.txt")]    // Unicode——不入字符集（兼容性声明：消费方请用 ASCII 安全字符）
    [InlineData("evil.txt\n")]  // 尾随换行——\z 严格字符串末尾必败（$ 锚点会放行 \n 之前）
    public async Task UploadAsync_EdgeName_ThrowsArgumentException(string name)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(
            () => service.UploadAsync(name, new MemoryStream(new byte[] { 1 }), "text/plain", ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("good.txt::$DATA")]
    [InlineData("evil.txt.")]
    [InlineData(".name.txt")]
    [InlineData("a/b/")]
    [InlineData("evil.txt\n")]
    public async Task DeleteAsync_EdgePath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.DeleteAsync(path, ct: TestContext.Current.CancellationToken));
    }

    [Theory]
    [InlineData("evil.txt.")]
    [InlineData("evil ")]
    [InlineData("good.txt::$DATA")]
    [InlineData("a/b/")]
    [InlineData("evil.txt\n")]
    public async Task ExistsAsync_EdgePath_ThrowsArgumentException(string path)
    {
        var service = CreateService();

        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.ExistsAsync(path, ct: TestContext.Current.CancellationToken));
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
        using (var reader = new StreamReader(downloaded!))
        {
            Assert.Equal("hello blob", await reader.ReadToEndAsync(ct));
        }
        // V0.2.0 流式下载契约：返回的 FileStream 持有文件句柄，删除前须先释放（FileShare.Read 不授予删除权限）
        Assert.True(await service.DeleteAsync(info.Path, ct));
        Assert.False(await service.ExistsAsync(info.Path, ct));
    }

    [Fact]
    public async Task DownloadAsync_MultiSegmentLegalPath_DoesNotThrow()
    {
        // {guid}/{name} 形式的多段合法相对路径——不存在返回 null，而非抛 ArgumentException
        var service = CreateService();

        var result = await service.DownloadAsync("0123456789abcdef0123456789abcdef/missing.txt", ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    [Theory]
    [InlineData("photo.JPG")]
    [InlineData("dir_a/nested-file.txt")]
    [InlineData("my_file-v2.tar.gz")]
    [InlineData("0123456789abcdef0123456789abcdef/photo.JPG")]
    public async Task DownloadAsync_LegalPath_DoesNotThrow(string path)
    {
        // V0.3.1 白名单兼容实证（§4.5）：大小写文件名/多段（含内部 {guid}/ 形态）——不存在返回 null，而非抛 ArgumentException
        var service = CreateService();

        var result = await service.DownloadAsync(path, ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    // ── ②（V0.3.1 BL-P0）规范化前缀双保险自身（IsUnderRoot）正/边界 ──

    [Fact]
    public void IsUnderRoot_LegalPath_True()
    {
        // RootPath 下合法相对路径规范化后仍在前缀内（含 full == root 自身边界）
        var root = Path.Combine(Path.GetTempPath(), "tkwf-uroot-" + Guid.NewGuid().ToString("N"));
        var service = CreateService(root);
        var isUnderRoot = GetIsUnderRoot(service);

        Assert.True(isUnderRoot(Path.Combine(root, "a", "b.txt")));
        Assert.True(isUnderRoot(root));
    }

    [Fact]
    public void IsUnderRoot_SiblingPrefix_NotUnderRoot()
    {
        // 前缀边界防误判：RootPath 的兄弟目录（root2）不得判为 root 内
        var root = Path.Combine(Path.GetTempPath(), "tkwf-uroot-" + Guid.NewGuid().ToString("N"));
        var sibling = root + "2";
        var service = CreateService(root);
        var isUnderRoot = GetIsUnderRoot(service);

        Assert.False(isUnderRoot(Path.Combine(sibling, "x")));
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

    /// <summary>反射取私有 IsUnderRoot（规范化前缀双保险自身单测——V0.3.1 BL-P0，§5.3）。</summary>
    private static Func<string, bool> GetIsUnderRoot(LocalStorageService service)
    {
        var method = typeof(LocalStorageService).GetMethod("IsUnderRoot", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("LocalStorageService.IsUnderRoot 私有方法未找到");
        return path => (bool)method.Invoke(service, new object[] { path })!;
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
