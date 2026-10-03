using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.Authentication.Tests;

/// <summary>D12：PlatformCredential——AES-GCM 落库加密（DB 无明文）/ 读路径解密 / 唯一约束 / SetEnabled。
/// <para>V4.10.53（领域自治根治后重写）：门面继承 DomainServiceBase——StubDomainUser 直构（经基类 User 取上下文），
/// DataService 经 User.Use&lt;具体类&gt;() NoAop 直建（IEntityDAC 从 DI 解析）。业务断言语义不变。</para></summary>
public class PlatformCredentialServiceTests
{
    private static (PlatformCredentialService Service, PlatformCredentialEntityDataService Ds) CreateService()
    {
        var fsql = AuthenticationTestHost.CreateInMemoryFreeSql();
        var options = AuthenticationTestHost.CreateOptions();
        options.SecretEncryptionKeyPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"tkwf-auth-key-{System.Guid.NewGuid():N}.key");
        var stub = AuthenticationTestHost.CreateStub(fsql);
        var service = new PlatformCredentialService(stub, Options.Create(options), NullLogger<PlatformCredentialService>.Instance);
        return (service, stub.Use<PlatformCredentialEntityDataService>());
    }

    [Fact]
    public async Task Create_StoresCiphertext_DbHasNoPlaintext()
    {
        var (service, ds) = CreateService();
        var credential = new PlatformCredentialEntity
        {
            Platform = AuthTypes.Wechat,
            AppType = "mp",
            AppId = "wx-123",
            AppName = "测试公众号"
        };
        await service.CreateAsync(credential, "secret-abc");

        var stored = await ds.GetByAppAsync(AuthTypes.Wechat, "mp");
        Assert.NotNull(stored);
        // DB 无明文
        Assert.DoesNotContain("secret-abc", stored!.AppSecretEncrypted);

        // 读路径解密
        var secret = await service.GetSecretAsync(AuthTypes.Wechat, "mp");
        Assert.NotNull(secret);
        Assert.Equal("wx-123", secret!.AppId);
        Assert.Equal("secret-abc", secret.AppSecret);
    }

    [Fact]
    public async Task Create_DuplicateUniqueKey_Throws()
    {
        var (service, _) = CreateService();
        await service.CreateAsync(new PlatformCredentialEntity { Platform = AuthTypes.Wechat, AppType = "mp", AppId = "wx-1" }, "s1");
        await Assert.ThrowsAnyAsync<System.Exception>(() =>
            service.CreateAsync(new PlatformCredentialEntity { Platform = AuthTypes.Wechat, AppType = "mp", AppId = "wx-1" }, "s2"));
    }

    [Fact]
    public async Task Update_RotateSecret_ReEncrypts()
    {
        var (service, _) = CreateService();
        var credential = new PlatformCredentialEntity { Platform = AuthTypes.Wechat, AppType = "mp", AppId = "wx-2" };
        await service.CreateAsync(credential, "old-secret");

        // 更新（重加密）
        var stored = await service.GetAsync(AuthTypes.Wechat, "mp");
        await service.UpdateAsync(stored!, "new-secret");

        var secret = await service.GetSecretAsync(AuthTypes.Wechat, "mp");
        Assert.Equal("new-secret", secret!.AppSecret);
    }

    [Fact]
    public async Task SetEnabled_Disabled_GetSecretReturnsNull()
    {
        var (service, ds) = CreateService();
        var credential = new PlatformCredentialEntity { Platform = AuthTypes.Wechat, AppType = "mp", AppId = "wx-3" };
        await service.CreateAsync(credential, "secret");
        var id = (await ds.GetByAppAsync(AuthTypes.Wechat, "mp"))!.Id;

        await service.SetEnabledAsync(id, false);
        var secret = await service.GetSecretAsync(AuthTypes.Wechat, "mp");
        Assert.Null(secret);
    }
}
