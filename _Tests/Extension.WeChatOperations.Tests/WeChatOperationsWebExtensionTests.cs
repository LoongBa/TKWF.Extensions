using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using TKWF.Ext.WeChatOperations.DTOs;
using Xunit;

namespace TKWF.Ext.WeChatOperations.Tests;

/// <summary>
/// T22 gate（2026-10-27 补，转达 DMP v0.9.21——SG2 门面聚合缺口）：<see cref="WeChatOperationsWebExtension"/>
/// 端点级黑盒冒烟——25 端点逐一断言<b>已挂载（200）+ handler→门面绑定（Fake Invoked）</b>——断链（未挂载 → 404）即测试红。
/// <para>背景：V0.4.0 曾删 WebExtension 依赖消费方 SG1b 聚合；B4（v4.10.82/83/84 referencesAspNetCore 门控）后扩展
/// 落入 ExtensionEndpointRegistrations 发射分支 → 消费方 SG2 结构性让路 → 25 端点两头落空（运行时 404）——V0.6.0
/// e770087 重建 WebExtension（DeclareEndpoints + MapGenerated + ExtensionModule 前缀）修复。此测试锁定该修复：
/// 端点挂载断链必须炸测试（此前 143 例门面单测全绿不覆盖 HTTP 端点 → 断链静默）。</para>
/// <para>认证语义：生成端点全 <c>RequireAuthorization()</c>；宿主 <c>HttpAuth.ExcludedPathPrefixes</c> 排除 /api
/// （对齐 AuthCenter 先例——401 fail-closed 归框架 HttpAuthentication 中间件测试域，非本扩展责任）。</para>
/// <para>前缀断言：<c>[assembly: ExtensionModule("WeChatOperations","wechat-ops")]</c> + RestBasePath 缺省 /api
/// → 组 = /api/wechat-ops（= <see cref="WeChatOperationsEndpointHost.Url"/> 常量）。</para>
/// </summary>
public sealed class WeChatOperationsWebExtensionTests
{
    private const string ChannelId = "wechat-mp-main";

    // ═══════════════════ 运营端点（7 门面） ═══════════════════

    [Fact]
    public async Task GetChannels_ReturnsList_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url("/channels"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.ChannelConfig.ListInvoked);
    }

    [Fact]
    public async Task PostChannels_SaveRequest_BindsAndSaves_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.PostAsJsonAsync(WeChatOperationsEndpointHost.Url("/channels"),
            new SaveChannelRequest(0, ChannelId, "wx_main", "主公众号", null, true, "wechat_event", "secret", "token", "aeskey"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.ChannelConfig.SaveInvoked);
        Assert.Equal("wechat-mp-main", WeChatOperationsEndpointHost.ChannelConfig.LastRequest?.ChannelId);
        Assert.Equal("wx_main", WeChatOperationsEndpointHost.ChannelConfig.LastRequest?.AppId);
    }

    [Fact]
    public async Task GetMenu_ReturnsCurrent_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url($"/channels/{ChannelId}/menu"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Menu.GetCurrentInvoked);
    }

    [Fact]
    public async Task PostMenu_PublishRequest_Binds_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.PostAsJsonAsync(WeChatOperationsEndpointHost.Url($"/channels/{ChannelId}/menu"),
            new PublishMenuRequest("""{"button":[{"type":"click","name":"测试","key":"test_key"}]}"""));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Menu.PublishInvoked);
    }

    [Fact]
    public async Task GetMenuVersions_ReturnsList_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url($"/channels/{ChannelId}/menu/versions"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Menu.ListVersionsInvoked);
    }

    [Fact]
    public async Task PostMenuRollback_Binds_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.PostAsJsonAsync(WeChatOperationsEndpointHost.Url($"/channels/{ChannelId}/menu/rollback"),
            new RollbackMenuRequest(5));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Menu.RollbackInvoked);
    }

    [Fact]
    public async Task GetRules_ReturnsList_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url($"/channels/{ChannelId}/rules?scene_type=2"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.ReplyRule.ListInvoked);
    }

    [Fact]
    public async Task PostRules_EntityBody_Binds_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.PostAsJsonAsync(WeChatOperationsEndpointHost.Url($"/channels/{ChannelId}/rules"),
            new { ChannelId, SceneType = 2, TriggerKey = "优惠", Content = "欢迎", IsEnabled = true });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.ReplyRule.SaveInvoked);
    }

    [Fact]
    public async Task GetMaterials_ReturnsList_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url("/materials?kind=1"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Material.ListInvoked);
    }

    [Fact]
    public async Task PostMaterials_EntityBody_Binds_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.PostAsJsonAsync(WeChatOperationsEndpointHost.Url("/materials"),
            new { Name = "促销图文", Kind = 2 });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Material.SaveInvoked);
    }

    [Fact]
    public async Task GetSubscriptionStats_ReturnsTrend_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url($"/channels/{ChannelId}/subscription-stats?from=2026-10-01&to=2026-10-31&source="));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.SubscriptionStat.TrendInvoked);
    }

    [Fact]
    public async Task GetSubscriptionStatsSummary_ReturnsSummary_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url($"/channels/{ChannelId}/subscription-stats/summary?from=2026-10-01&to=2026-10-31&source="));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.SubscriptionStat.SummaryInvoked);
    }

    [Fact]
    public async Task GetLogsInteractions_ReturnsList_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url($"/channels/{ChannelId}/logs/interactions?openId=u-1&triggerType=2"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.LogQuery.InteractionsInvoked);
    }

    [Fact]
    public async Task GetLogsPublishes_ReturnsList_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url($"/channels/{ChannelId}/logs/publishes"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.LogQuery.PublishesInvoked);
    }

    [Fact]
    public async Task GetStatus_ReturnsSummary_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url($"/channels/{ChannelId}/status"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Status.Invoked);
    }

    // ═══════════════════ 网关端点（1 门面 9 端点） ═══════════════════

    [Fact]
    public async Task GetGatewayRules_ReturnsList_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url("/gateway/rules?channelId=wechat-mp-main&msgType=text&eventType="));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Gateway.ListRulesInvoked);
    }

    [Fact]
    public async Task PostGatewayRules_EntityBody_Binds_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.PostAsJsonAsync(WeChatOperationsEndpointHost.Url("/gateway/rules"),
            new { ChannelId, Name = "路由规则", MsgType = "text", MatchMode = 1, TriggerKey = "优惠" });

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Gateway.SaveRuleInvoked);
    }

    [Fact]
    public async Task DeleteGatewayRule_Returns200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.DeleteAsync(WeChatOperationsEndpointHost.Url("/gateway/rules/11"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Gateway.DeleteRuleInvoked);
    }

    [Fact]
    public async Task GetGatewayEndpoints_ReturnsList_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url("/gateway/endpoints?channelId=wechat-mp-main"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Gateway.ListEndpointsInvoked);
    }

    [Fact]
    public async Task PostGatewayEndpoints_SaveEndpointRequest_Binds_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.PostAsJsonAsync(WeChatOperationsEndpointHost.Url("/gateway/endpoints"),
            new SaveEndpointRequest(0, ChannelId, "下游A", "https://downstream.example.com/webhook", null, "POST", 2, true, 0, "plain-token", 3, null));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Gateway.SaveEndpointInvoked);
    }

    [Fact]
    public async Task DeleteGatewayEndpoint_Returns200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.DeleteAsync(WeChatOperationsEndpointHost.Url("/gateway/endpoints/13"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Gateway.DeleteEndpointInvoked);
    }

    [Fact]
    public async Task GetGatewayStatus_ReturnsStatus_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url("/gateway/status"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Gateway.StatusInvoked);
    }

    [Fact]
    public async Task PostGatewayEnabled_Binds_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.PostAsJsonAsync(WeChatOperationsEndpointHost.Url("/gateway/enabled"),
            new SetGatewayEnabledRequest(true));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Gateway.SetEnabledInvoked);
    }

    [Fact]
    public async Task GetGatewayForwardLogs_ReturnsList_200()
    {
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url($"/gateway/forward-logs?channelId={ChannelId}"));

        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        Assert.True(WeChatOperationsEndpointHost.Gateway.ForwardLogsInvoked);
    }

    // ═══════════════════ 负向/边界 ═══════════════════

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        // 路由确实来自生成端点（非全放行兜底）——未知路径必须 404，证明冒烟有效
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.GetAsync(WeChatOperationsEndpointHost.Url("/channels/wechat-mp-main/does-not-exist"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }

    [Fact]
    public async Task RouteAsync_NotExposed_RouteThroughGateway404OrKnownRoutesOnly()
    {
        // RouteAsync 为事件内部入口（ExposeRest=false）——无 /gateway/route 端点；断言未知子路径 404
        var client = WeChatOperationsEndpointHost.Server.CreateClient();
        var resp = await client.PostAsync(WeChatOperationsEndpointHost.Url("/gateway/route"), new StringContent("{}", System.Text.Encoding.UTF8, "application/json"));

        Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
    }
}