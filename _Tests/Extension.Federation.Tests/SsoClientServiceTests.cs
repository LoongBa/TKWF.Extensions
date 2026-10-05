using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TKW.Framework.Domain.FreeSql;

namespace TKWF.Ext.Federation.Tests;

/// <summary>
/// SSO client 服务测试（T4——应用注册 + client credential 校验 + HMAC 密钥 + origin 白名单）。
/// </summary>
public class SsoClientServiceTests
{
    private static (SsoClientService Service, SsoClientEntityDataService Ds) CreateService()
    {
        var fsql = SsoTestHost.CreateInMemoryFreeSql();
        var stub = SsoTestHost.CreateStub(fsql);
        var options = SsoTestHost.CreateOptions();
        var service = new SsoClientService(stub, Options.Create(options), NullLogger<SsoClientService>.Instance);
        return (service, stub.Use<SsoClientEntityDataService>());
    }

    [Fact]
    public async Task Register_GeneratesSecretsEncrypted()
    {
        var (service, ds) = CreateService();
        var client = await service.RegisterAsync(["https://app.example.com"], ["profile:basic"], default);

        Assert.StartsWith("app-", client.AppId);
        Assert.Contains("https://app.example.com", client.OriginWhitelist);
        Assert.True(client.IsEnabled);

        // 落库密文（AES-GCM）——明文绝不落库
        var row = await ds.EntityGetAsync(m => m.AppId == client.AppId, default);
        Assert.NotNull(row);
        Assert.False(string.IsNullOrEmpty(row!.ClientSecretEncrypted));
        Assert.False(string.IsNullOrEmpty(row.HmacSecretEncrypted));
    }

    [Fact]
    public async Task ValidateClientCredential_RoundTrip()
    {
        // 经 RegisterAsync 拿不到明文 secret（SsoClientInfo 无 secret 字段）——直接种实体 + 解密校验链路
        var (service, ds) = CreateService();
        var appId = "app-roundtrip";
        var clientSecret = "secret-abc";
        var hmacSecret = "hmac-abc";
        var key = FederationSecretKeyStore.GetKey();
        await ds.EntityCreateAsync(new SsoClientEntity
        {
            AppId = appId,
            OriginWhitelist = "[\"https://app.example.com\"]",
            Scopes = "[\"profile:basic\"]",
            ClientSecretEncrypted = FederationSecretKeyStore.Encrypt(clientSecret, key),
            HmacSecretEncrypted = FederationSecretKeyStore.Encrypt(hmacSecret, key),
            IsEnabled = true,
        }, default);

        Assert.True(await service.ValidateClientCredentialAsync(appId, clientSecret, default));
        Assert.False(await service.ValidateClientCredentialAsync(appId, "wrong-secret", default));
        Assert.Equal(hmacSecret, await service.GetHmacSecretAsync(appId, default));
    }

    [Fact]
    public async Task IsOriginAllowed_ExactMatch()
    {
        var (service, _) = CreateService();
        var client = await service.RegisterAsync(["https://app.example.com"], ["profile:basic"], default);

        Assert.True(await service.IsOriginAllowedAsync(client.AppId, "https://app.example.com", default));
        Assert.False(await service.IsOriginAllowedAsync(client.AppId, "https://evil.example.com", default));   // 不同 origin 拒
        Assert.False(await service.IsOriginAllowedAsync(client.AppId, "https://app.example.com.evil.com", default)); // 子域混淆拒
        Assert.False(await service.IsOriginAllowedAsync("app-not-exist", "https://app.example.com", default)); // 未注册拒
    }
}
