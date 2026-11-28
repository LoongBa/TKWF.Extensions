using System;

namespace TKWF.Ext.WeChatChannels;

/* ============================================================
   Wire DTO（2026-11-05 自 WeChatOperations.DTOs.WeChatOperationsWebDtos.cs 拆分下沉——
   通道面 DTO 归 WeChatChannels；运营面（PublishMenuRequest/RollbackMenuRequest/
   SaveEndpointRequest/SetGatewayEnabledRequest/ErrorResponse）留守运营扩展）
   ------------------------------------------------------------------
   ⚠️ 安全裁定（Oracle P1 DTO 投影）：请求 DTO 承载凭证明文（AppSecret/Token/EncodingAESKey）
   经 [FromBody] JSON body 传输——不走 query string（防 URL 日志泄漏）。
   响应 DTO 不含任何凭证密文（密文不回显——列表/详情经业务 DTO 投影）。
   ============================================================ */

/// <summary>保存公众号配置请求（凭证明文可选——null = 保持库中既有密文不变；<b>不落服务端日志</b>——Oracle P1）。
/// <para>原 <c>TKWF.Ext.WeChatOperations.IChannelConfigService.SaveChannelAsync</c> wire 契约参数（拆分下沉，namespace 迁 TKWF.Ext.WeChatChannels）。</para>
/// <para>D03 1.2：<see cref="ChannelForm"/>（mp_subscription/mp_service/mini_program/work_wechat——
/// <c>ChannelTypeRegistry</c> 校验）/ <see cref="Verification"/>（none/verified）/ <see cref="Alias"/>
/// （对外 slug——1.6 规则：小写字母数字连字符；可空）。</para></summary>
public sealed record SaveChannelRequest(
    long? Id,
    string ChannelId,
    string AppId,
    string? DisplayName = null,
    string? Remark = null,
    bool IsEnabled = true,
    string? ChannelType = null,
    string? AppSecret = null,
    string? Token = null,
    string? EncodingAesKey = null,
    string? ChannelForm = null,
    string? Verification = null,
    string? Alias = null);

/// <summary>保存结果响应（Id——新建返回自增主键）。
/// <para><b>源码兼容垫片</b>（2026-11-23 code-review P2）：commit 976c5df 曾把本公开 wire DTO 删除——
/// 对引用契约包的存量消费方属 source-breaking。此处按 v0.1.1 原形状恢复并标 <see cref="ObsoleteAttribute"/>；
/// <see cref="IChannelConfigService.SaveChannelAsync"/> 签名从未改变（直接返回 <see cref="long"/>），
/// 新代码继续消费该 long 返回值，无需构造本类型。</para></summary>
[Obsolete("SaveChannelResponse 已退役——IChannelConfigService.SaveChannelAsync 直接返回 long；仅为源码兼容保留，新代码勿用。")]
public sealed record SaveChannelResponse(long Id);