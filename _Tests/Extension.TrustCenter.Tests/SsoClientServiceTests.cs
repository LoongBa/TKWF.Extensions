using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;
using TKW.Framework.Domain.KeyManagement;

namespace TKWF.Ext.TrustCenter.Tests;

/// <summary>
/// SSO client 服务测试（T4——应用注册 + client credential 校验 + HMAC 密钥 + origin 白名单）。
/// <para><b>TrustCenter 剥离（2026-10-09）</b>：自 Federation.Tests 迁入——命名空间 TKWF.Ext.TrustCenter.Tests，
/// <c>FederationOptions</c> → <see cref="TrustCenterOptions"/>；keyed 密钥键 <c>SymmetricKeyProviderKeys.Federation</c>
/// → <see cref="SymmetricKeyProviderKeys.TrustCenter"/>（v4.10.70 主框架常量并入，C11 第 5 扩展边界兑现）。</para>
/// </summary>
public class SsoClientServiceTests
{
    private static (SsoClientService Service, SsoClientEntityDataService Ds, ISymmetricKeyProvider Keys) CreateService()
    {
        var fsql = TrustCenterTestHost.CreateInMemoryFreeSql();
        var stub = TrustCenterTestHost.CreateStub(fsql);
        var options = TrustCenterTestHost.CreateOptions();
        // 直构 FileSymmetricKeyProvider（对齐 DI 注册语义——生产模式密钥文件已预置，构造即加载前 32 字节）
        var keys = new FileSymmetricKeyProvider(options.SecretEncryptionKeyPath, options.IsProduction);
        var service = new SsoClientService(stub, keys, Options.Create(options), NullLogger<SsoClientService>.Instance);
        return (service, stub.Use<SsoClientEntityDataService>(), keys);
    }

    [Fact]
    public async Task Register_GeneratesSecretsEncrypted()
    {
        var (service, ds, _) = CreateService();
        var client = await service.RegisterAsync(["https://app.example.com"], ["profile:basic"], default);

        Assert.StartsWith("app-", client.AppId);
        Assert.Contains("https://app.example.com", client.Info.OriginWhitelist);
        Assert.True(client.Info.IsEnabled);

        // ★2026-10-14 注册契约缺口修复：注册响应一次性携带明文 client_secret（RFC 7591）——
        // 非空 + 可经 ValidateClientCredentialAsync 通过（动态注册子应用兑现身份的关键）
        Assert.False(string.IsNullOrWhiteSpace(client.ClientSecret));
        Assert.True(await service.ValidateClientCredentialAsync(client.AppId, client.ClientSecret, default));

        // 落库密文（AES-GCM）——明文绝不落库
        var row = await ds.EntityGetAsync(m => m.AppId == client.AppId, default);
        Assert.NotNull(row);
        Assert.False(string.IsNullOrEmpty(row!.ClientSecretEncrypted));
        Assert.False(string.IsNullOrEmpty(row.HmacSecretEncrypted));
        // 明文 != 密文（加密语义验证）
        Assert.NotEqual(client.ClientSecret, row.ClientSecretEncrypted);
    }

    [Fact]
    public async Task ValidateClientCredential_RoundTrip()
    {
        // 直接种实体 + 解密校验链路（注册响应 secret 外，校验链路本体正负断言）
        var (service, ds, keys) = CreateService();
        var appId = "app-roundtrip";
        var clientSecret = "secret-abc";
        var hmacSecret = "hmac-abc";
        await ds.EntityCreateAsync(new SsoClientEntity
        {
            AppId = appId,
            OriginWhitelist = "[\"https://app.example.com\"]",
            Scopes = "[\"profile:basic\"]",
            ClientSecretEncrypted = keys.Encrypt(clientSecret),
            HmacSecretEncrypted = keys.Encrypt(hmacSecret),
            IsEnabled = true,
        }, default);

        Assert.True(await service.ValidateClientCredentialAsync(appId, clientSecret, default));
        Assert.False(await service.ValidateClientCredentialAsync(appId, "wrong-secret", default));
        Assert.Equal(hmacSecret, await service.GetHmacSecretAsync(appId, default));
    }

    [Fact]
    public async Task IsOriginAllowed_ExactMatch()
    {
        var (service, _, _) = CreateService();
        var client = await service.RegisterAsync(["https://app.example.com"], ["profile:basic"], default);

        Assert.True(await service.IsOriginAllowedAsync(client.AppId, "https://app.example.com", default));
        Assert.False(await service.IsOriginAllowedAsync(client.AppId, "https://evil.example.com", default));   // 不同 origin 拒
        Assert.False(await service.IsOriginAllowedAsync(client.AppId, "https://app.example.com.evil.com", default)); // 子域混淆拒
        Assert.False(await service.IsOriginAllowedAsync("app-not-exist", "https://app.example.com", default)); // 未注册拒
    }
}
