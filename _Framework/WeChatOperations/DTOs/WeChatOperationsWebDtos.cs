using System;

namespace TKWF.Ext.WeChatOperations.DTOs;

/* ============================================================
   Wire DTO（V0.4.0 引入——[GenerateController] 生成端点专用请求 DTO）
   ------------------------------------------------------------------
   ⚠️ 安全裁定（Oracle P1 DTO 投影）：请求 DTO 承载凭证明文（AppSecret/Token/EncodingAESKey/AuthToken）
   经 [FromBody] JSON body 传输——不走 query string（防 URL 日志泄漏）。
   响应 DTO 不含任何凭证密文（密文不回显——列表/详情经业务 DTO 投影）。
   原 WebExtension 手写 minimal API 的请求 DTO 上移为门面契约参数（V0.4.0 替换生成端点）。
   ============================================================ */

/// <summary>保存公众号配置请求（凭证明文可选——null = 保持库中既有密文不变；<b>不落服务端日志</b>——Oracle P1）。
/// <para>V0.4.0 起为 <see cref="TKWF.Ext.WeChatOperations.IChannelConfigService.SaveChannelAsync"/> 的 wire 契约参数（原 WebExtension 手写端点同名 DTO 上移）。</para>
/// <para>D03 1.2 新增：<see cref="ChannelForm"/>（mp_subscription/mp_service/mini_program/work_wechat——
/// <c>ChannelTypeRegistry</c> 校验）/ <see cref="Verification"/>（none/verified）/ <see cref="Alias"/>
/// （对外 slug'——1.6 规则：小写字母数字连字符；可空）。</para></summary>
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

/// <summary>保存结果响应（Id——新建返回自增主键）。</summary>
public sealed record SaveChannelResponse(long Id);

/// <summary>发布菜单请求（raw JSON 透传——编辑器自由形态）。
/// <para>V0.4.0 起为 <see cref="TKWF.Ext.WeChatOperations.IMenuService.PublishAsync"/> 的 wire 契约参数（单复杂参数 → [FromBody]）。</para></summary>
public sealed record PublishMenuRequest(string MenuJson);

/// <summary>回滚菜单请求（目标版本 Id——加载该版本 AfterJson 重新发布）。</summary>
public sealed record RollbackMenuRequest(long VersionId);

/// <summary>
/// 保存下游端点请求（<see cref="TKWF.Ext.WeChatOperations.IMessageGatewayService.SaveEndpointAsync"/> 的 wire 契约参数——V0.4.0 引入；
/// 鉴权令牌 <see cref="AuthTokenPlain"/> 为<b>明文</b>可选（null = 保持库中既有密文不变）——门面经 keyed
/// <see cref="TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider"/> AES-GCM 加密落库（明文不落库不进日志）。
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