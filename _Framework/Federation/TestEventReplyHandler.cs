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
/// 内置默认 handler（微信事件图文回复——模板驱动，两级模型第一级；2026-10-16 W2/模板化升级）。
/// <para>受开关 <see cref="FederationEventReplyOptions.TestEventReplyHandlerEnabled"/>（默认 false——P1-1 fail-safe
/// 显式开启）注册；收到事件（微信未认证号推送，<c>ChannelType=wechat_event</c>）→ 载荷 =
/// <see cref="FederatedIdentity"/>（openid/uid 填充）→ 基类签密封票据 → 图文链接回复（URL 指内置测试端点
/// <c>{BaseUrl?}/{RoutePrefix}/{platformId}/test/{channelId}/{ticket}</c>——platformId 经基类入口预查 registry，
/// BaseUrl 空 = 相对路径默认指向本服务同源）。</para>
/// <para><b>图文模板（两级模型第一级——不同 channelId 各自模板，默认 handler 共用）</b>：
/// 经 <see cref="ReplyContext.Channel.Extra"/> 键 <see cref="NewsReplyTemplateKey"/>（<c>news_reply</c>）读取——
/// JSON <c>{ title, description, link, pic_url, ttl_seconds, enabled }</c>；占位符
/// <c>{"{uid}","{openid}","{channel_id}","{channel_type}","{ticket}"}</c>（<c>link</c> 模板的 <c>{ticket}</c>
/// 由基类签票后注入 <see cref="EventReplyHandlerBase.BuildTemplateUrl"/>）；<c>link</c> 空 = 回退内置测试端点 URL；
/// 模板缺失 = 回退内置测试默认；<c>enabled=false</c> → 该通道不回复（null，不拦截其它 handler）。
/// 特殊需求（复杂条件/多图文/外部数据）→ 自定义 handler 继承 <see cref="EventReplyHandlerBase"/>（第二级）。</para>
/// <para>消费方接入业务 handler 后保持测试开关关闭（测试设施整体不启用——联动见使用指南"测试设施生命周期"小节）。</para>
/// </summary>
public sealed class TestEventReplyHandler : EventReplyHandlerBase
{
    /// <summary>图文模板配置键（<see cref="ReplyContext.Channel.Extra"/>——通道级图文配置，模板驱动两级模型第一级）。</summary>
    public const string NewsReplyTemplateKey = "news_reply";

    /// <summary>通道类型——仅事件通道（微信未认证号事件推送）。</summary>
    public override string ChannelType => "wechat_event";

    /// <summary>构造（守卫工厂 ActivatorUtilities 帧内供给 IDomainUser——Options 经基类 IOptions 注入）。</summary>
    public TestEventReplyHandler(IDomainUser user,
        Microsoft.Extensions.Options.IOptions<FederationEventReplyOptions>? options = null) : base(user, options)
    {
    }

    /// <summary>
    /// 业务规则（模板驱动）：经 <see cref="ReplyContext"/> 读通道图文模板 → 载荷；模板缺失回退内置测试默认。
    /// <para>ExpectedClaimant=null（P0-1——测试端点 Redeem context=null，non-null ExpectedClaimant 必致
    /// CLAIMANT_MISMATCH；可转让语义由密封票据高熵随机码自保护）。</para>
    /// </summary>
    protected override Task<EventReplyPayload?> BuildReplyAsync(
        ReplyContext context, CancellationToken ct)
    {
        // P1-1：开关关闭（默认 false）→ 不回复不拦截（测试设施整体不启用，业务 handler 正常流转）
        if (!TestEventReplyHandlerEnabled)
            return Task.FromResult<EventReplyPayload?>(null);

        var parameters = context.Context.Parameters;
        var openId = GetValue(parameters, "external_user_id");
        var uid = GetValue(parameters, "uid");
        var channelId = GetValue(parameters, "channel_id");

        // 模板读取（Extra["news_reply"]——通道级图文配置；缺失/损坏回退内置）
        var template = ReadTemplate(context.Channel.Extra);
        if (template is { Enabled: false })
            return Task.FromResult<EventReplyPayload?>(null);   // enabled=false → 该通道不回复

        // 载荷 = FederatedIdentity 序列化（测试端点 RedeemAsync<FederatedIdentity> 反序列化显示）
        var identity = new FederatedIdentity(
            UId: string.IsNullOrEmpty(uid) ? (openId ?? string.Empty) : uid!,
            ChannelId: channelId ?? string.Empty,
            ExternalUserId: openId ?? string.Empty,
            ChannelType: ChannelType);
        var payloadJson = JsonSerializer.Serialize(identity);

        var title = ReplacePlaceholders(template?.Title ?? "微信接入测试", uid, openId, channelId);
        var description = ReplacePlaceholders(
            template?.Description ?? $"openid={openId} uid={uid}——点击验证 ticket 兑换闭环", uid, openId, channelId);
        var ttl = template?.TtlSeconds is > 0
            ? TimeSpan.FromSeconds(template.TtlSeconds.Value)
            : TimeSpan.FromMinutes(5);

        return Task.FromResult<EventReplyPayload?>(
            new EventReplyPayload(
                Title: title,
                Description: description,
                PayloadJson: payloadJson,
                Ttl: ttl,
                ExpectedClaimant: null,                  // P0-1：可转让（测试端点 Redeem context=null）
                PicUrl: template?.PicUrl,
                LinkTemplate: template?.Link));         // 空 → 基类回退内置测试端点 URL
    }

    private static string? GetValue(IReadOnlyDictionary<string, string?> parameters, string key)
        => parameters.TryGetValue(key, out var v) ? v : null;

    /// <summary>占位符替换（{uid}/{openid}/{channel_id}/{channel_type}——{ticket} 仅 link 由基类注入）。</summary>
    private string ReplacePlaceholders(string text, string? uid, string? openId, string? channelId)
        => text
            .Replace("{uid}", uid ?? string.Empty)
            .Replace("{openid}", openId ?? string.Empty)
            .Replace("{channel_id}", channelId ?? string.Empty)
            .Replace("{channel_type}", ChannelType);

    /// <summary>读取图文模板（Extra["news_reply"] JSON——容错：缺失/解析失败 → null 回退内置）。</summary>
    private static NewsReplyTemplate? ReadTemplate(IReadOnlyDictionary<string, string?>? extra)
    {
        if (extra is null || !extra.TryGetValue(NewsReplyTemplateKey, out var raw) || string.IsNullOrWhiteSpace(raw))
            return null;
        try
        {
            return JsonSerializer.Deserialize<NewsReplyTemplate>(raw);
        }
        catch (JsonException)
        {
            return null;   // 配置损坏 → 回退内置（不拦截业务 handler）
        }
    }

    /// <summary>图文模板 DTO（Extra["news_reply"] JSON——Title/Description/Link 占位符模板；Link 空 = 内置测试端点 URL）。</summary>
    private sealed record NewsReplyTemplate(
        string? Title,
        string? Description,
        string? Link,
        string? PicUrl,
        int? TtlSeconds,
        bool? Enabled);
}
