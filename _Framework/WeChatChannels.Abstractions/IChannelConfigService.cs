using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Permissions.Abstractions;

namespace TKWF.Ext.WeChatChannels;

/// <summary>
/// 公众号渠道配置门面契约（WeChatChannels 通道底座——2026-11-05 自 WeChatOperations 拆分下沉）。
/// <para><b>两源并存</b>（DB 权威 + Options 降级——设计文档 §3.3.4）：本门面为 DB 权威读写面；
/// 出站凭证解析经 <c>DbWeChatCredentialResolver</c>（两源并存 ①）。</para>
/// <para><b>凭证安全（Oracle P1）</b>：<see cref="SaveChannelAsync"/> 入参经 <see cref="SaveChannelRequest"/> wire DTO——
/// 三凭证字段（AppSecret/Token/EncodingAESKey）为<b>明文</b>经 body 传输（防 query string URL 泄漏）；
/// 门面经 keyed <see cref="TKW.Framework.Domain.KeyManagement.ISymmetricKeyProvider"/> AES-GCM 加密落库
/// （<c>AppSecretCipher/TokenCipher/EncodingAESKeyCipher</c>）；DTO 投影 (<see cref="WeChatChannelConfigDto"/>)
/// <b>不含凭证</b>——密文不回显，解密仅出站瞬时（DbWeChatCredentialResolver）。</para>
/// <para>REST 端点（<b>非 Web 扩展形态</b>——2026-11-05 BLOCKER 修复：主包不引 AspNetCore →
/// 消费方 SG2 按 ADR47 能力清单生成控制器（对齐 Identity `/identity/user-roles` / Notifications
/// `/notifications/inbox` 先例——RestRoute 含模块段，URL = RestBasePath + RestRoute 全段））：</para>
/// <list type="bullet">
/// <item><c>GET /api/wechat-channels/channels</c> → <see cref="ListChannelsAsync"/>（List 前缀推导 GET）</item>
/// <item><c>POST /api/wechat-channels/channels</c> → <see cref="SaveChannelAsync"/>（Create 前缀推导 POST；单复杂参数 SaveChannelRequest [FromBody]）</item>
/// </list>
/// <para>消费方：<c>User.Use&lt;IChannelConfigService&gt;()</c>（AddConstructibleService 守卫工厂，帧内解析）。</para>
/// </summary>
public interface IChannelConfigService : IDomainService
{
    /// <summary>按 AppId 查询公众号配置（DTO 投影不含凭证密文；未命中 → null）。
    /// <para><c>ExposeRest=false</c>：单查为运营内部能力（列表已含全部配置），不生成 REST 端点。</para></summary>
    [RequirePermission("WeChatChannels.Query")]
    [ApiExpose(ExposeRest = false)]
    Task<WeChatChannelConfigDto?> GetAsync(string appId, CancellationToken ct = default);

    /// <summary>按 ChannelId 查询公众号配置（DTO 投影不含凭证密文；未命中 → null）。
    /// <para>2026-11-05 Oracle O2 新增——运营侧按 channelId 解析配置（如 MenuService 发布链）的契约入口，替代跨扩展直引 DataService。</para>
    /// <para><c>ExposeRest=false</c>：单查为运营内部能力，不生成 REST 端点。</para></summary>
    [RequirePermission("WeChatChannels.Query")]
    [ApiExpose(ExposeRest = false)]
    Task<WeChatChannelConfigDto?> GetByChannelIdAsync(string channelId, CancellationToken ct = default);

    /// <summary>全部公众号配置列表（DTO 投影不含凭证密文）。<para>REST：<c>GET /api/wechat-channels/channels</c>。</para></summary>
    [RequirePermission("WeChatChannels.Query")]
    [ApiExpose(RestRoute = "/wechat-channels/channels", RestMethod = "GET")]
    Task<IReadOnlyList<WeChatChannelConfigDto>> ListChannelsAsync(CancellationToken ct = default);

    /// <summary>保存公众号配置——新（Id=0）创建 / 已有更新；<paramref name="request"/> 承载凭证明文
    /// （AppSecret/Token/EncodingAESKey 为<b>明文</b>，null = 保持库中既有密文不变）——门面加密落库。
    /// <para>REST：<c>POST /api/wechat-channels/channels</c>（单复杂参数 SaveChannelRequest → [FromBody] 防 URL 凭证泄漏）。</para></summary>
    [RequirePermission("WeChatChannels.Manage")]
    [ApiExpose(RestRoute = "/wechat-channels/channels", RestMethod = "POST")]
    Task<long> SaveChannelAsync(SaveChannelRequest request, CancellationToken ct = default);

    /// <summary>删除公众号配置（物理删——配置删除后出站凭证经 Options 降级解析）。
    /// <para><c>ExposeRest=false</c>：删除为高操作权限能力，不新增暴露面。</para></summary>
    [RequirePermission("WeChatChannels.Manage")]
    [ApiExpose(ExposeRest = false)]
    Task<bool> DeleteChannelAsync(long id, CancellationToken ct = default);
}