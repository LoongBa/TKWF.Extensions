using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.KeyManagement;

namespace TKWF.Ext.AuthCenter.Tests;

/// <summary>D12：PlatformCredential——AES-GCM 落库加密（DB 无明文）/ 读路径解密 / 唯一约束 / SetEnabled。
/// <para>V4.10.53（领域自治根治后重写）：门面继承 DomainServiceBase——StubDomainUser 直构（经基类 User 取上下文），
/// DataService 经 User.Use&lt;具体类&gt;() NoAop 直建（IEntityDAC 从 DI 解析）。业务断言语义不变。</para>
/// <para>E4 密钥管理抽象（V0.7.0）：静态 PlatformCredentialKeyStore 已删——密钥经 FileSymmetricKeyProvider 实例持有
/// （每用例 CreateService 新建独立 provider + 唯一密钥文件路径 → 无跨用例共享，不再需要 ctor Reset）。</para>
/// <para>⚠️ T5（2026-10-09 三层边界）：造数平台字符串改 "federation"（原 TestPlatform 常量已删——
/// <c>PlatformCredentialEntity</c> 保留为平台库共用底座，微信凭证存量由 DBA 清理，见使用指南 SQL 脚本）。</para></summary>
public class PlatformCredentialServiceTests
{
    /// <summary>测试平台字符串（T5 起——微信凭证存量清理语义，平台库共用底座保留）。</summary>
    private const string TestPlatform = "federation";
    private static (PlatformCredentialService Service, PlatformCredentialEntityDataService Ds) CreateService()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var options = AuthenticationTestHost.CreateOptions();
        options.SecretEncryptionKeyPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tkwf-auth-key-{System.Guid.NewGuid():N}.key");
        var stub = AuthenticationTestHost.CreateStub(fsql);
        // keyed ISymmetricKeyProvider 无法按位置传参——直接构造 FileSymmetricKeyProvider（dev 分支：路径缺失 → 随机 32B + 写盘）
        var keys = new FileSymmetricKeyProvider(options.SecretEncryptionKeyPath, isProduction: false, logger: null);
        var service = new PlatformCredentialService(stub, keys, Options.Create(options), NullLogger<PlatformCredentialService>.Instance);
        return (service, stub.Use<PlatformCredentialEntityDataService>());
    }

    [Fact]
    public async Task Create_StoresCiphertext_DbHasNoPlaintext()
    {
        var (service, ds) = CreateService();
        var credential = new PlatformCredentialEntity
        {
            Platform = TestPlatform,
            AppType = "mp",
            AppId = "wx-123",
            AppName = "测试公众号"
        };
        await service.CreateAsync(credential, "secret-abc");

        var stored = await ds.GetByAppAsync(TestPlatform, "mp");
        Assert.NotNull(stored);
        // DB 无明文
        Assert.DoesNotContain("secret-abc", stored!.AppSecretEncrypted);

        // 读路径解密
        var secret = await service.GetSecretAsync(TestPlatform, "mp");
        Assert.NotNull(secret);
        Assert.Equal("wx-123", secret!.AppId);
        Assert.Equal("secret-abc", secret.AppSecret);
    }

    [Fact]
    public async Task Create_DuplicateUniqueKey_Throws()
    {
        var (service, _) = CreateService();
        await service.CreateAsync(new PlatformCredentialEntity { Platform = TestPlatform, AppType = "mp", AppId = "wx-1" }, "s1");
        await Assert.ThrowsAnyAsync<System.Exception>(() =>
            service.CreateAsync(new PlatformCredentialEntity { Platform = TestPlatform, AppType = "mp", AppId = "wx-1" }, "s2"));
    }

    [Fact]
    public async Task Update_RotateSecret_ReEncrypts()
    {
        var (service, _) = CreateService();
        var credential = new PlatformCredentialEntity { Platform = TestPlatform, AppType = "mp", AppId = "wx-2" };
        await service.CreateAsync(credential, "old-secret");

        // 更新（重加密）
        var stored = await service.GetAsync(TestPlatform, "mp");
        await service.UpdateAsync(stored!, "new-secret");

        var secret = await service.GetSecretAsync(TestPlatform, "mp");
        Assert.Equal("new-secret", secret!.AppSecret);
    }

    [Fact]
    public async Task SetEnabled_Disabled_GetSecretReturnsNull()
    {
        var (service, ds) = CreateService();
        var credential = new PlatformCredentialEntity { Platform = TestPlatform, AppType = "mp", AppId = "wx-3" };
        await service.CreateAsync(credential, "secret");
        var id = (await ds.GetByAppAsync(TestPlatform, "mp"))!.Id;

        await service.SetEnabledAsync(id, false);
        var secret = await service.GetSecretAsync(TestPlatform, "mp");
        Assert.Null(secret);
    }
}
