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