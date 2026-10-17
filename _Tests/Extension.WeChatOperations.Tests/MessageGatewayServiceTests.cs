using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FreeSql;
using TKW.Framework.Domain;
using TKWF.Federation.WeChat.MessageGateway;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// <see cref="IMessageGatewayService"/> 网关门面测试（V0.2.0 增网关模块——批次 3 门面集成）——生产路径
/// <see cref="WeChatOperationsTestHost"/> + <c>BindScope()</c> + <c>user.Use&lt;IMessageGatewayService&gt;()</c> AOP。
/// <para>覆盖：路由规则 CRUD（新建/完全重复同键检测更新/channel+MsgType+Event 过滤/删除）/ 下游端点 CRUD
/// （AuthToken 明文入→AES-GCM 密文落库 / DTO 投影不含密文 / 被规则引用禁止删除）/ 网关状态
/// （GetGatewayStatusAsync Enabled/计数 + SetGatewayEnabledAsync 运行时覆盖）。</para>
/// <para>⚠️ <see cref="IMessageGatewayService.SetGatewayEnabledAsync"/> 写进程内静态覆盖
/// （<see cref="MessageGatewayService"/>._gatewayEnabledOverride）——测试 finally 复位 +
/// <see cref="WeChatOperationsTestHost.ResetGatewayEnabledOverride"/>（宿主 Dispose 兜底），防测试间泄漏。</para>
/// </summary>
public class MessageGatewayServiceTests : WeChatOperationsTestHost
{
    private const string ChA = "ch-gw-a";
    private const string ChB = "ch-gw-b";

    // ═══════════════════ 路由规则 CRUD ═══════════════════

    /// <summary>SaveRuleAsync Name 必填——空白/缺省抛 ArgumentException（DMP-Lite 需求 5.5——每条规则含名称）。</summary>
    [Fact]
    public async Task SaveRule_MissingName_Throws()
    {
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();

        var ex = await Assert.ThrowsAsync<ArgumentException>(
            () => svc.SaveRuleAsync(new MessageRouteRuleEntity
            {
                ChannelId = ChA,
                MsgType = "text",
                ForwardMode = WeChatMessageForwardModes.LocalOnly,
            }, TestContext.Current.CancellationToken));
        Assert.Contains("Name 必填", ex.Message);
        // 空白名同样拒绝
        await Assert.ThrowsAsync<ArgumentException>(
            () => svc.SaveRuleAsync(new MessageRouteRuleEntity
            {
                ChannelId = ChA, Name = "   ",
                MsgType = "text",
                ForwardMode = WeChatMessageForwardModes.LocalOnly,
            }, TestContext.Current.CancellationToken));
        // 未落库（校验前置拒绝）
        Assert.Empty(await svc.ListRulesAsync(ChA, ct: TestContext.Current.CancellationToken));
    }

    /// <summary>SaveRuleAsync（Id=0 新建）→ 返回新 Id + ListRulesAsync 可见。</summary>
    [Fact]
    public async Task SaveRule_New_CreatesAndListReturns()
    {
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();

        var rule = new MessageRouteRuleEntity
        {
            ChannelId = ChA,
            Name = "优惠券规则",
            MsgType = "text",
            MatchMode = WeChatMessageMatchModes.Exact,
            TriggerKey = "优惠券",
            ForwardMode = WeChatMessageForwardModes.LocalOnly,
            IsEnabled = true,
        };
        var id = await svc.SaveRuleAsync(rule, TestContext.Current.CancellationToken);

        Assert.True(id > 0);
        var rules = await svc.ListRulesAsync(ChA, ct: TestContext.Current.CancellationToken);
        Assert.Single(rules);
        Assert.Equal(id, rules[0].Id);
        Assert.Equal("text", rules[0].MsgType);
        Assert.Equal(WeChatMessageMatchModes.Exact, rules[0].MatchMode);
    }

    /// <summary>SaveRuleAsync 完全重复同键检测——ChannelId+MsgType+Event+MatchMode+TriggerKey 全同 → 更新既有不新建。
    /// 第二次仅改 IsEnabled → 返回同一 Id + 库中仅 1 行 + 字段已覆盖。</summary>
    [Fact]
    public async Task SaveRule_DuplicateKey_UpdatesExistingNotCreate()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();

        var first = await svc.SaveRuleAsync(new MessageRouteRuleEntity
        {
            ChannelId = ChA, Name = "促销规则", MsgType = "text", Event = null,
            MatchMode = WeChatMessageMatchModes.Exact, TriggerKey = "促销",
            ForwardMode = WeChatMessageForwardModes.LocalOnly, IsEnabled = true,
        }, TestContext.Current.CancellationToken);

        var second = await svc.SaveRuleAsync(new MessageRouteRuleEntity
        {
            ChannelId = ChA, Name = "促销规则", MsgType = "text", Event = null,
            MatchMode = WeChatMessageMatchModes.Exact, TriggerKey = "促销",
            ForwardMode = WeChatMessageForwardModes.LocalOnly, IsEnabled = false,   // 同键覆盖——仅改启用
        }, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);   // 同键更新既有——返回原 Id
        var rules = await svc.ListRulesAsync(ChA, ct: TestContext.Current.CancellationToken);
        Assert.Single(rules);          // 不新建
        Assert.False(rules[0].IsEnabled);   // 字段已覆盖
        var count = await provider.GetRequiredService<IFreeSql>()
            .Select<MessageRouteRuleEntity>().Where(r => r.ChannelId == ChA).CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, count);
    }

    /// <summary>ListRulesAsync channelId 过滤 + MsgType/Event 过滤（空 = 通配）。</summary>
    [Fact]
    public async Task ListRules_ChannelAndMsgTypeEventFilter()
    {
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();

        await svc.SaveRuleAsync(new MessageRouteRuleEntity { ChannelId = ChA, Name = "text规则", MsgType = "text", ForwardMode = WeChatMessageForwardModes.LocalOnly }, TestContext.Current.CancellationToken);
        await svc.SaveRuleAsync(new MessageRouteRuleEntity { ChannelId = ChA, Name = "image规则", MsgType = "image", ForwardMode = WeChatMessageForwardModes.LocalOnly }, TestContext.Current.CancellationToken);
        await svc.SaveRuleAsync(new MessageRouteRuleEntity { ChannelId = ChA, Name = "subscribe规则", MsgType = "event", Event = "subscribe", ForwardMode = WeChatMessageForwardModes.LocalOnly }, TestContext.Current.CancellationToken);
        await svc.SaveRuleAsync(new MessageRouteRuleEntity { ChannelId = ChB, Name = "text规则B", MsgType = "text", ForwardMode = WeChatMessageForwardModes.LocalOnly }, TestContext.Current.CancellationToken);

        var allA = await svc.ListRulesAsync(ChA, ct: TestContext.Current.CancellationToken);
        Assert.Equal(3, allA.Count);                       // channelId 过滤
        var textA = await svc.ListRulesAsync(ChA, msgType: "text", ct: TestContext.Current.CancellationToken);
        Assert.Single(textA);
        var subA = await svc.ListRulesAsync(ChA, msgType: "event", eventType: "subscribe", ct: TestContext.Current.CancellationToken);
        Assert.Single(subA);
        var clickA = await svc.ListRulesAsync(ChA, msgType: "event", eventType: "click", ct: TestContext.Current.CancellationToken);
        Assert.Empty(clickA);
        var allB = await svc.ListRulesAsync(ChB, ct: TestContext.Current.CancellationToken);
        Assert.Single(allB);
    }

    /// <summary>DeleteRuleAsync 物理删——删除后 ListRules 空；不存在删除返回 false。</summary>
    [Fact]
    public async Task DeleteRule_Removes()
    {
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();

        var id = await svc.SaveRuleAsync(new MessageRouteRuleEntity { ChannelId = ChA, Name = "删除测试规则", MsgType = "text", ForwardMode = WeChatMessageForwardModes.LocalOnly }, TestContext.Current.CancellationToken);

        var deleted = await svc.DeleteRuleAsync(id, TestContext.Current.CancellationToken);
        Assert.True(deleted);
        Assert.Empty(await svc.ListRulesAsync(ChA, ct: TestContext.Current.CancellationToken));
        Assert.False(await svc.DeleteRuleAsync(id, TestContext.Current.CancellationToken));
    }

    // ═══════════════════ 下游端点 CRUD（AuthToken 密文） ═══════════════════

    /// <summary>SaveEndpointAsync 明文入→AES-GCM 密文落库——库中 AuthTokenCipher 非空且 ≠ 明文（明文永不落库）。</summary>
    [Fact]
    public async Task SaveEndpoint_PlainToken_EncryptedAtRest()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();

        var id = await svc.SaveEndpointAsync(new DownstreamEndpointEntity
        {
            ChannelId = ChA,
            Name = "下游A",
            BaseUrl = "https://down.example.com",
            HttpMethod = "POST",
            TimeoutSeconds = 2,
            AuthType = WeChatEndpointAuthTypes.Bearer,
        }, authTokenPlain: "super-secret-token-2026", TestContext.Current.CancellationToken);

        var ep = await provider.GetRequiredService<IFreeSql>().Select<DownstreamEndpointEntity>()
            .Where(e => e.Id == id).FirstAsync(TestContext.Current.CancellationToken);
        Assert.NotNull(ep);
        Assert.False(string.IsNullOrWhiteSpace(ep!.AuthTokenCipher));
        Assert.NotEqual("super-secret-token-2026", ep.AuthTokenCipher);
        Assert.DoesNotContain("super-secret-token-2026", ep.AuthTokenCipher);   // AES-GCM base64 密文不含明文子串
        Assert.Equal(2, ep.TimeoutSeconds);
    }

    /// <summary>ListEndpointsAsync DTO 投影——不含 AuthTokenCipher 密文（DTO record 编译期无该成员）+
    /// HasCredentials 指示已配置令牌 + 常规字段投影。</summary>
    [Fact]
    public async Task ListEndpoints_DtoProjection_NoCipher_AndHasCredentials()
    {
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();

        var withToken = await svc.SaveEndpointAsync(new DownstreamEndpointEntity
        {
            ChannelId = ChA, Name = "带令牌", BaseUrl = "https://a.example.com", TimeoutSeconds = 2,
            AuthType = WeChatEndpointAuthTypes.Bearer,
        }, authTokenPlain: "token-abc", TestContext.Current.CancellationToken);
        await svc.SaveEndpointAsync(new DownstreamEndpointEntity
        {
            ChannelId = ChA, Name = "无鉴权", BaseUrl = "https://b.example.com", TimeoutSeconds = 2,
            AuthType = WeChatEndpointAuthTypes.None,
        }, authTokenPlain: null, TestContext.Current.CancellationToken);

        var dtos = await svc.ListEndpointsAsync(ChA, TestContext.Current.CancellationToken);
        Assert.Equal(2, dtos.Count);
        var withCred = dtos.Single(d => d.Id == withToken);
        Assert.Equal(ChA, withCred.ChannelId);
        Assert.Equal("带令牌", withCred.Name);
        Assert.True(withCred.HasCredentials);            // 已配置令牌（密文存在）
        Assert.Equal(WeChatEndpointAuthTypes.Bearer, withCred.AuthType);
        Assert.Equal(2000, withCred.TimeoutMs);          // 派生超时毫秒（TimeoutSeconds=2 → 2000——DMP-Lite 5.5"超时毫秒"前端字段，秒契约不变）
        var noCred = dtos.Single(d => d.Name == "无鉴权");
        Assert.False(noCred.HasCredentials);             // 未配置令牌
        // ⚠️ 密文不回显——DownstreamEndpointDto record 无 AuthTokenCipher 成员（编译期保证），HasCredentials 为唯一凭证指示
    }

    /// <summary>DeleteEndpointAsync 被规则引用禁止删除——规则 EndpointId 引用时删除抛 InvalidOperationException。</summary>
    [Fact]
    public async Task DeleteEndpoint_ReferencedByRule_Throws()
    {
        using var _ = this;
        var (provider, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();

        var epId = await svc.SaveEndpointAsync(new DownstreamEndpointEntity
        {
            ChannelId = ChA, Name = "被引用端点", BaseUrl = "https://down.example.com", TimeoutSeconds = 2,
        }, authTokenPlain: null, TestContext.Current.CancellationToken);
        await svc.SaveRuleAsync(new MessageRouteRuleEntity
        {
            ChannelId = ChA, Name = "引用端点规则", MsgType = "text", ForwardMode = WeChatMessageForwardModes.AsyncCopy, EndpointId = epId,
        }, TestContext.Current.CancellationToken);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => svc.DeleteEndpointAsync(epId, TestContext.Current.CancellationToken));
        Assert.Contains("引用", ex.Message);
        // 端点仍存在（删除被拒）
        var count = await provider.GetRequiredService<IFreeSql>()
            .Select<DownstreamEndpointEntity>().Where(e => e.Id == epId).CountAsync(TestContext.Current.CancellationToken);
        Assert.Equal(1, count);
    }

    /// <summary>DeleteEndpointAsync 未被引用——删除成功 + 列表空。</summary>
    [Fact]
    public async Task DeleteEndpoint_Unreferenced_Deletes()
    {
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();

        var id = await svc.SaveEndpointAsync(new DownstreamEndpointEntity
        {
            ChannelId = ChA, Name = "独立端点", BaseUrl = "https://down.example.com", TimeoutSeconds = 2,
        }, authTokenPlain: null, TestContext.Current.CancellationToken);

        var deleted = await svc.DeleteEndpointAsync(id, TestContext.Current.CancellationToken);
        Assert.True(deleted);
        Assert.Empty(await svc.ListEndpointsAsync(ChA, TestContext.Current.CancellationToken));
    }

    // ═══════════════════ 网关状态 ═══════════════════

    /// <summary>GetGatewayStatusAsync 默认（未覆盖运行时开关）——Enabled=Options 默认 false + 规则/端点计数。</summary>
    [Fact]
    public async Task GetGatewayStatus_DefaultDisabled_WithCounts()
    {
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();

        await svc.SaveRuleAsync(new MessageRouteRuleEntity { ChannelId = ChA, Name = "状态规则", MsgType = "text", ForwardMode = WeChatMessageForwardModes.LocalOnly }, TestContext.Current.CancellationToken);
        await svc.SaveEndpointAsync(new DownstreamEndpointEntity
        {
            ChannelId = ChA, Name = "状态端点", BaseUrl = "https://down.example.com", TimeoutSeconds = 2,
        }, authTokenPlain: null, TestContext.Current.CancellationToken);

        var status = await svc.GetGatewayStatusAsync(TestContext.Current.CancellationToken);
        Assert.False(status.Enabled);          // Options 默认 false（fail-closed）
        Assert.Equal(1, status.RuleCount);
        Assert.Equal(1, status.EndpointCount);
    }

    /// <summary>SetGatewayEnabledAsync 运行时覆盖——true 后 GetGatewayStatusAsync Enabled=true；false 复位 false
    /// （进程内覆盖；finally 反射复位 null 防测试泄漏）。</summary>
    [Fact]
    public async Task SetGatewayEnabled_RuntimeOverride_ReflectedInStatus()
    {
        using var _ = this;
        var (_, user) = BindScope();
        ResetGatewayEnabledOverride();
        var svc = user.Use<IMessageGatewayService>();
        try
        {
            await svc.SetGatewayEnabledAsync(true, TestContext.Current.CancellationToken);
            Assert.True((await svc.GetGatewayStatusAsync(TestContext.Current.CancellationToken)).Enabled);

            await svc.SetGatewayEnabledAsync(false, TestContext.Current.CancellationToken);
            Assert.False((await svc.GetGatewayStatusAsync(TestContext.Current.CancellationToken)).Enabled);
        }
        finally
        {
            ResetGatewayEnabledOverride();
        }
    }
}
