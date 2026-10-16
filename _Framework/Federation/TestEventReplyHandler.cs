using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 内置测试 handler（开发期对接测试用——方案 docs/Federation/EventReplyHandlerBase与内置测试Handler-开发方案.md）。
/// <para>受开关 <see cref="FederationEventReplyOptions.TestEventReplyHandlerEnabled"/>（默认 false——P1-1 fail-safe
/// 显式开启）注册；收到事件（微信未认证号推送，<c>ChannelType=wechat_event</c>）→ 载荷 =
/// <see cref="FederatedIdentity"/>（openid/uid 填充）→ 基类签密封票据 → 图文链接回复（URL 指内置测试端点
/// <c>{BaseUrl?}/{RoutePrefix}/{platformId}/test/{channelId}/{ticket}</c>——platformId 经 registry 反查，
/// BaseUrl 空 = 相对路径默认指向本服务同源）。</para>
/// <para>消费方接入业务 handler 后保持测试开关关闭（测试设施整体不启用——联动见使用指南"测试设施生命周期"小节）。</para>
/// </summary>
public sealed class TestEventReplyHandler : EventReplyHandlerBase
{
    /// <summary>通道类型——仅事件通道（微信未认证号事件推送）。</summary>
    public override string ChannelType => "wechat_event";

    /// <summary>构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser——Options 经基类 IOptions 注入）。</summary>
    public TestEventReplyHandler(IDomainUser user,
        Microsoft.Extensions.Options.IOptions<FederationEventReplyOptions>? options = null) : base(user, options)
    {
    }

    /// <summary>
    /// 业务规则：构造 FederatedIdentity 载荷（P0-2——测试端点 <c>RedeemAsync&lt;FederatedIdentity&gt;</c>
    /// 反序列化属性对齐：UId/ChannelId/ExternalUserId/ChannelType）。
    /// <para>ExpectedClaimant=null（P0-1——测试端点 Redeem claimant=null，non-null ExpectedClaimant 必致
    /// CLAIMANT_MISMATCH；可转让语义由密封票据高熵随机码自保护）。</para>
    /// </summary>
    protected override Task<EventReplyPayload?> BuildReplyAsync(
        SsoChannelEventContext context, CancellationToken ct)
    {
        // P1-1：开关关闭（默认 false）→ 不回复不拦截（测试设施整体不启用，业务 handler 正常流转）
        if (!TestEventReplyHandlerEnabled)
            return Task.FromResult<EventReplyPayload?>(null);

        var parameters = context.Parameters;
        var openId = GetValue(parameters, "external_user_id");
        var uid = GetValue(parameters, "uid");
        var channelId = GetValue(parameters, "channel_id");

        // 载荷 = FederatedIdentity 序列化（测试端点 RedeemAsync<FederatedIdentity> 反序列化显示）
        var payload = new FederatedIdentity(
            UId: string.IsNullOrEmpty(uid) ? (openId ?? string.Empty) : uid!,
            ChannelId: channelId ?? string.Empty,
            ExternalUserId: openId ?? string.Empty,
            ChannelType: ChannelType);
        var payloadJson = JsonSerializer.Serialize(payload);

        return Task.FromResult<EventReplyPayload?>(
            new EventReplyPayload(
                Title: "微信接入测试",
                Description: $"openid={openId} uid={uid}——点击验证 ticket 兑换闭环",
                PayloadJson: payloadJson,
                Ttl: TimeSpan.FromMinutes(5),
                ExpectedClaimant: null));   // P0-1：可转让（测试端点 Redeem claimant=null）
    }

    private static string? GetValue(IReadOnlyDictionary<string, string?> parameters, string key)
        => parameters.TryGetValue(key, out var v) ? v : null;
}
