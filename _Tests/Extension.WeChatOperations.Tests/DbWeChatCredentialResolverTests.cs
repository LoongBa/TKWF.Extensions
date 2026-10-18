using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.Domain;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.WeChatOperations.DTOs;
using TKWF.Federation.WeChat;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="DbWeChatCredentialResolver"/> 测试——两源并存（设计文档 §3.3.4）：
/// DB 命中 → AES-GCM 解密返回（明文仅出站瞬时）；DB 未命中 → 回退 Options（WeChatOptions.Channels 按 AppId
/// 精确匹配降级）；两源并存 DB 权威（DB 有记录即使 Options 有同 appId 也取 DB 解密值）。
/// <para>注册形态：扩展 Initializer <c>TryAddEnumerable(Scoped&lt;IWeChatCredentialResolver,
/// DbWeChatCredentialResolver&gt;)</c> 追加在平台库缺省 Options 实现（TryAddSingleton）之后——
/// DI 单解析取最后注册 = DB 优先。测试经 <c>provider.GetRequiredService&lt;IWeChatCredentialResolver&gt;()</c>
/// 解析生产注册形态。</para>
/// </summary>
public class DbWeChatCredentialResolverTests : WeChatOperationsTestHost
{
    /// <summary>DB 命中 → 解密返回明文凭证（AES-GCM 密文落库 → 出站瞬时解密）。</summary>
    [Fact]
    public async Task ResolveAsync_DbHit_DecryptsCredentials()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        await SeedConfigAsync(user, channelId: "ch-db", appId: "wx-db", appSecretPlain: "db-secret", tokenPlain: "db-token");

        var resolver = provider.GetRequiredService<IWeChatCredentialResolver>();
        var result = await resolver.ResolveAsync("wx-db", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("ch-db", result!.ChannelId);
        Assert.Equal("wx-db", result.AppId);
        Assert.Equal("db-secret", result.AppSecret);      // 解密回明文（仅出站瞬时）
        Assert.Equal("db-token", result.Token);
    }

    /// <summary>DB 未命中 → 降级 Options（WeChatOptions.Channels 按 AppId 精确匹配——现状语义迁移）。</summary>
    [Fact]
    public async Task ResolveAsync_DbMiss_FallsBackToOptions()
    {
        using var _ = this;
        ConfigureWeChatOptions = o => o.Channels.Add(new WeChatChannelConfig
        {
            ChannelId = "ch-opt",
            AppId = "wx-opt",
            AppSecret = "opt-secret",
            Token = "opt-token",
            EncodingAESKey = WeChatOperationsTestSupport.GenerateEncodingAESKey(),
        });
        var (provider, _) = BindScope();   // 无 DB 配置记录

        var resolver = provider.GetRequiredService<IWeChatCredentialResolver>();
        var result = await resolver.ResolveAsync("wx-opt", ct: TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("ch-opt", result!.ChannelId);
        Assert.Equal("opt-secret", result.AppSecret);     // Options 明文直接返回
    }

    /// <summary>DB 未命中 + Options 无匹配 → null（调用方 WeChatApiClient 抛凭证未配置——语义一致）。</summary>
    [Fact]
    public async Task ResolveAsync_DbMiss_NoOptions_Null()
    {
        using var _ = this;
        var (provider, _) = BindScope();

        var resolver = provider.GetRequiredService<IWeChatCredentialResolver>();
        var result = await resolver.ResolveAsync("wx-unknown", ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    /// <summary>两源并存 DB 权威——DB 有记录即使 Options 有同 AppId 也取 DB 解密值（DB 优先语义）。</summary>
    [Fact]
    public async Task ResolveAsync_TwoSources_DbAuthoritative()
    {
        using var _ = this;
        ConfigureWeChatOptions = o => o.Channels.Add(new WeChatChannelConfig
        {
            ChannelId = "ch-opt",
            AppId = "wx-both",
            AppSecret = "opt-secret",
            Token = "opt-token",
        });
        var (provider, user) = BindScope();
        await SeedConfigAsync(user, channelId: "ch-db", appId: "wx-both", appSecretPlain: "db-secret");

        var resolver = provider.GetRequiredService<IWeChatCredentialResolver>();
        var result = await resolver.ResolveAsync("wx-both", ct: TestContext.Current.CancellationToken);

        // DB 权威——返回 DB 解密值（非 Options 值）
        Assert.NotNull(result);
        Assert.Equal("ch-db", result!.ChannelId);
        Assert.Equal("db-secret", result.AppSecret);
    }

    /// <summary>DB 命中但 AppSecret 密文空（未配置凭证）→ null（调用方抛凭证未配置）。</summary>
    [Fact]
    public async Task ResolveAsync_DbHit_EmptySecret_Null()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        // 仅配置 ChannelId/AppId——三凭证密文空
        await SeedConfigAsync(user, channelId: "ch-empty", appId: "wx-empty", appSecretPlain: null);

        var resolver = provider.GetRequiredService<IWeChatCredentialResolver>();
        var result = await resolver.ResolveAsync("wx-empty", ct: TestContext.Current.CancellationToken);

        Assert.Null(result);
    }

    /// <summary>解密失败（密文损坏/密钥不匹配）→ 异常消息不含明文（Oracle P1 异常消息安全五补充）。</summary>
    [Fact]
    public async Task ResolveAsync_CorruptCipher_Throws_WithoutPlaintext()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        // 直接写损坏密文（绕过门面加密——模拟密钥不匹配/密文损坏）
        var ds = user.Use<WeChatChannelConfigEntityDataService>();
        await ds.EntityCreateAsync(new WeChatChannelConfigEntity
        {
            ChannelId = "ch-bad",
            AppId = "wx-bad",
            AppSecretCipher = "not-a-valid-cipher",
            IsEnabled = true,
            ChannelType = "wechat_event",
            CreateTime = DateTime.UtcNow,
            UpdateTime = DateTime.UtcNow,
        }, ct: TestContext.Current.CancellationToken);

        var resolver = provider.GetRequiredService<IWeChatCredentialResolver>();
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => resolver.ResolveAsync("wx-bad", TestContext.Current.CancellationToken));

        // 异常消息不含明文（不泄露密钥/密文原文）
        Assert.Contains("解密失败", ex.Message);
        Assert.DoesNotContain("not-a-valid-cipher", ex.Message);
    }

    private static async Task SeedConfigAsync(DomainUser<TestUserInfo> user, string channelId, string appId,
        string? appSecretPlain, string? tokenPlain = null)
    {
        var service = user.Use<IChannelConfigService>();
        await service.SaveAsync(new SaveChannelRequest(
            Id: null, ChannelId: channelId, AppId: appId, DisplayName: "凭证测试", IsEnabled: true,
            ChannelType: "wechat_event", AppSecret: appSecretPlain, Token: tokenPlain), ct: TestContext.Current.CancellationToken);
    }
}
