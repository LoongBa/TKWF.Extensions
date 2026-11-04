using System;

namespace TKWF.Ext.WeChatOperations.DTOs;

/* ============================================================
   Wire DTO（V0.4.0 引入——[GenerateController] 生成端点专用请求 DTO）
   ------------------------------------------------------------------
   ⚠️ 安全裁定（Oracle P1 DTO 投影）：请求 DTO 承载凭证明文（AuthToken）
   经 [FromBody] JSON body 传输——不走 query string（防 URL 日志泄漏）。
   ⚠️ V0.9.0 拆分（2026-11-05）：SaveChannelRequest/SaveChannelResponse 已随通道面下沉
   WeChatChannels.Abstractions（namespace TKWF.Ext.WeChatChannels）——本文件仅保留运营面 DTO。
   ============================================================ */

/// <summary>发布菜单请求（raw JSON 透传——编辑器自由形态）。
/// <para>V0.4.0 起为 <see cref="TKWF.Ext.WeChatOperations.IMenuService.PublishAsync"/> 的 wire 契约参数（单复杂参数 → [FromBody]）。</para></summary>
public sealed record PublishMenuRequest(string MenuJson);

/// <summary>回滚菜单请求（目标版本 Id——加载该版本 AfterJson 重新发布）。</summary>
public sealed record RollbackMenuRequest(long VersionId);

/// <summary>
/// 保存下游端点请求（<see cref="TKWF.Ext.WeChatOperations.IMessageGatewayService.SaveEndpointAsync"/> 的 wire 契约参数——V0.4.0 引入；
/// 鉴权令牌 <see cref="AuthTokenPlain"/> 为<b>明文</b>可选（null = 保持库中既有密文不变）——门面经 keyed
/// <see cref="TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider"/> AES-GCM 加密落库（明文不落库不进日志；
/// V0.9.0 起密钥键 = WeChatChannelsKeyProviderKeys.WeChatChannels——跨扩展同信任域）。
/// </summary>
public sealed record SaveEndpointRequest(
    long? Id,
    string ChannelId,
    string Name,
    string BaseUrl,
    string? Path = null,
    string HttpMethod = "POST",
    int TimeoutSeconds = 2,
    bool IsEnabled = true,
    int AuthType = 0,
    string? AuthTokenPlain = null,
    int? RetryCount = null,
    string? Remark = null);

/// <summary>统一错误响应（错误码 + 可选消息）——GenerateController 管道异常映射产出，消费方对齐解析。</summary>
public sealed record ErrorResponse(string Code, string? Message);

/// <summary>网关总开关请求（<see cref="TKWF.Ext.WeChatOperations.IMessageGatewayService.SetGatewayEnabledAsync"/> 的 wire 契约参数——V0.4.0 引入；
/// 进程内运行时覆盖——重启回落 <c>TKWF:WeChatOperations:Gateway:GatewayEnabled</c> 配置值；单复杂参数 → [FromBody]）。</summary>
public sealed record SetGatewayEnabledRequest(bool Enabled);