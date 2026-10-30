using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interception;
using TKWF.Ext.Testing.Shared;
using TKWF.Ext.WeChatOperations.DTOs;
using TKWF.Federation.WeChat;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="DbWeChatCredentialResolver"/> 测试——两源并存（设计文档 §3.3.4）：
/// DB 命中 → AES-GCM 解密返回（明文仅出站瞬时）；DB 未命中 → 回退 Options（WeChatOptions.Channels 按 AppId
/// 精确匹配降级）；两源并存 DB 权威（DB 有记录即使 Options 有同 appId 也取 DB 解密值）。
/// <para>⚠️ DB 读取的 ambient 用户入口（2026-10-30 框架组转达确认）：<c>StaticDomainInterceptor.CurrentContext
/// ?.DomainUser</c>——仅在 AOP 帧内（门面/事件 handler 执行期）非空。本测试宿主（<see cref="WeChatOperationsTestHost"/>）
/// 的守卫工厂<b>不产生装饰器帧</b>（AddConstructibleService 直建实现），故 DB 命中场景经
/// <see cref="RunInFrameAsync"/> 驱动<b>真实生产拦截器</b>（<see cref="StaticDomainInterceptor.InterceptAsync"/>——
/// 消费方装饰器同款入口）构造帧；帧外场景（降级测试）验证 <see cref="StaticDomainInterceptor.CurrentContext"/>
/// 为 null 的行为（生产对应非 AOP 帧直调，fail-safe 降级 Options 不抛出）。</para>
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

        // DB 优先需要 AOP 帧内 ambient 用户（StaticDomainInterceptor.CurrentContext）——经真实拦截器帧执行
        var result = await RunInFrameAsync(provider, user, () => resolver.ResolveAsync("wx-db", TestContext.Current.CancellationToken));

        Assert.NotNull(result);
        Assert.Equal("ch-db", result!.ChannelId);
        Assert.Equal("wx-db", result.AppId);
        Assert.Equal("db-secret", result.AppSecret);      // 解密回明文（仅出站瞬时）
        Assert.Equal("db-token", result.Token);
    }

    /// <summary>DB 未命中 → 降级 Options（WeChatOptions.Channels 按 AppId 精确匹配——现状语义迁移）。
    /// 帧外执行（无 ambient 用户）→ 跳过 DB 查询，Options 兜底语义与生产非 AOP 帧直调一致。</summary>
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

    /// <summary>两源并存 DB 权威——DB 有记录即使 Options 有同 AppId 也取 DB 解密值（DB 优先语义）。
    /// 帧内执行（ambient 用户非空）→ DB 权威生效。</summary>
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
        var result = await RunInFrameAsync(provider, user, () => resolver.ResolveAsync("wx-both", TestContext.Current.CancellationToken));

        // DB 权威——返回 DB 解密值（非 Options 值）
        Assert.NotNull(result);
        Assert.Equal("ch-db", result!.ChannelId);
        Assert.Equal("db-secret", result.AppSecret);
    }

    /// <summary>DB 命中但 AppSecret 密文空（未配置凭证）→ null（调用方抛凭证未配置）。
    /// 帧内执行——DB 命中路径生效。</summary>
    [Fact]
    public async Task ResolveAsync_DbHit_EmptySecret_Null()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        // 仅配置 ChannelId/AppId——三凭证密文空
        await SeedConfigAsync(user, channelId: "ch-empty", appId: "wx-empty", appSecretPlain: null);

        var resolver = provider.GetRequiredService<IWeChatCredentialResolver>();
        var result = await RunInFrameAsync(provider, user, () => resolver.ResolveAsync("wx-empty", TestContext.Current.CancellationToken));

        Assert.Null(result);
    }

    /// <summary>解密失败（密文损坏/密钥不匹配）→ 异常消息不含明文（Oracle P1 异常消息安全五补充）。
    /// 帧内执行——DB 命中路径生效（坏密文行命中 → 解密抛）。</summary>
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
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            RunInFrameAsync(provider, user, () => resolver.ResolveAsync("wx-bad", TestContext.Current.CancellationToken)));

        // 异常消息不含明文（不泄露密钥/密文原文）
        Assert.Contains("解密失败", ex.Message);
        Assert.DoesNotContain("not-a-valid-cipher", ex.Message);
    }

    /// <summary>
    /// 生产形态 fail-safe（审核 P1 处置回归）：非 AOP 帧（现网应不触达——裸后台任务直调等）→
    /// 本解析器必须降级 Options 且<b>不抛出</b>（ambient 用户入口 <c>StaticDomainInterceptor.CurrentContext</c>
    /// 为 null → 跳过 DB 查询）。测试宿主守卫工厂不产生装饰器帧，直接解析 resolver 调用即为帧外场景。
    /// </summary>
    [Fact]
    public async Task ResolveAsync_OutsideAopFrame_DegradesToOptionsWithoutThrow()
    {
        using var _ = this;
        ConfigureWeChatOptions = o => o.Channels.Add(new WeChatChannelConfig
        {
            ChannelId = "ch-opt-fallback",
            AppId = "wx-opt-fallback",
            AppSecret = "opt-secret-fallback",
            Token = "opt-token",
            EncodingAESKey = WeChatOperationsTestSupport.GenerateEncodingAESKey(),
        });
        var (provider, _) = BindScope();

        // 帧外前置确认：测试宿主无装饰器帧 → CurrentContext 为 null（生产非 AOP 帧同态）
        Assert.Null(StaticDomainInterceptor.CurrentContext);

        var resolver = provider.GetRequiredService<IWeChatCredentialResolver>();
        var result = await resolver.ResolveAsync("wx-opt-fallback", TestContext.Current.CancellationToken);

        Assert.NotNull(result);
        Assert.Equal("ch-opt-fallback", result!.ChannelId);
        Assert.Equal("opt-secret-fallback", result.AppSecret);
    }

    private static async Task SeedConfigAsync(DomainUser<TestUserInfo> user, string channelId, string appId,
        string? appSecretPlain, string? tokenPlain = null)
    {
        var service = user.Use<IChannelConfigService>();
        await service.SaveChannelAsync(new SaveChannelRequest(
            Id: null, ChannelId: channelId, AppId: appId, DisplayName: "凭证测试", IsEnabled: true,
            ChannelType: "wechat_event", AppSecret: appSecretPlain, Token: tokenPlain), ct: TestContext.Current.CancellationToken);
    }
}