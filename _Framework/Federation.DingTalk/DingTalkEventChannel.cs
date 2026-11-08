using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.DingTalk;

/// <summary>
/// 钉钉事件订阅推送通道（`ISsoChannel: dingtalk_event`）——扫码成功/组织变更等事件身份获取。
/// <para>流程（M3 方案 §3.2）：钉钉事件订阅推送 → 装配层接收端点（映射入站）→ 本通道
/// <see cref="AuthenticateAsync"/> 执行<b>三道闸门</b>：① <see cref="DingTalkEventCrypto.VerifySignature"/>
/// 验签（信任根一票否决）；② <see cref="DingTalkEventCrypto.DecryptMsg"/> AES 解密并<b>校验 receiveid=corpId
/// （第二道闸门——Oracle 评审 P1-1：forged org-change event 可授予企业访问权）</b>；③
/// <see cref="DingTalkEventCrypto.ExtractFromUserId"/> 提取事件体 JSON <c>FromUserId</c>（= openId，应用维度
/// ——Oracle 评审 P1-6）→ 返回 <see cref="SsoChannelAuthResult"/>（ExternalUserId=FromUserId）。</para>
/// <para><b>1200ms 硬约束（Oracle 评审 P1-2）</b>：本通道 <c>AuthenticateAsync</c> = 验签 + 解密 +
/// receiveid 校验 + FromUserId 提取（<b>CPU-only，无出站调用，&lt;50ms</b>）；落库即返 success 是装配层职责
/// （persist-then-ack 模式 + IHostedService/Channel&lt;T&gt; 队列推荐）——1200ms 预算覆盖装配层全链。</para>
/// <para>Context 参数（装配层接收端点传入——钉钉推送 URL query + body）：<c>Parameters["signature"]</c>（钉钉官方
/// 主参数名；兼容 "msg_signature" 别名）/<c>["timestamp"]</c>/<c>["nonce"]</c>/<c>["encrypt"]</c>——官方密文参数名
/// = <b><c>encrypt</c> 主键</b>，兼容 `msg_encrypt` 别名（P0-1 双键双位探查：query 双键 + body JSON 双键任一）。</para>
/// <para><b>I2-2 契约跟进（2026-11-09，V0.4.2）</b>：<see cref="HandleEventAsync"/> 验签→解密→事件归一→产出
/// <see cref="ChannelEventOutcome.Parsed(ChannelEvent)"/>（`dingtalk.` 前缀 Attributes / OccurredAt UTC / RawPayload
/// 明文 JSON——编排链 uid 只读映射 + handler 双桶迭代可达，DT-1 闭环）；<see cref="VerifyUrlAsync"/> 对任意合法入站
/// （check_url 或业务事件）回<b>加密 JSON 应答信封</b>（复用 <see cref="DingTalkEventCrypto.EncryptMsg"/> 生产原语 +
/// <see cref="DingTalkEventCrypto.ComputeSignature"/> 四字段——P0-2，非明文 success；装配层门扩展 §3.6 段 1 先试本方法
/// 段 4 恒回信封，P1-1）；<see cref="AuthenticateAsync"/> 对解密成功但无 FromUserId 的入站（check_url/组织事件——
/// P1-2 放行）返回 <c>success(null)</c>（不再 <c>DINGTALK_EVENT_DECRYPT_FAILED</c> 400）。</para>
/// <para>边界：本通道只管"验签+解密+receiveid 校验+取 FromUserId/事件归一"，不负责事件业务处理/异步编排（归装配层）。</para>
/// <para>注册：库扩展方法 <c>AddDingTalkFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92）；
/// channel 业务参数（CorpId/Token/EncodingAESKey）经工厂预取的 <see cref="ChannelConfig"/> 传入
/// （多通道联邦 v0.3.0——不占 ctor IDomainUser 槽）。</para>
/// </summary>
[DiContractIgnore]
public sealed class DingTalkEventChannel : DomainServiceBase, ISsoChannel
{
    private readonly ChannelConfig? _channel;
    private readonly ILogger<DingTalkEventChannel> _logger;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务）；
    /// <paramref name="channel"/> 为 null = 集合模板实例（工厂类型索引源，不直接认证——v0.3.0 多通道）。
    /// 凭证（CorpId/Token/EncodingAESKey——事件验签三道闸门信任根）在 <c>AuthenticateAsync</c> 从 cfg.Extra 使用。
    /// <paramref name="logger"/> = 结构化日志（工厂 ActivatorUtilities 从 DI 自动解析；模板实例直构时 null →
    /// NullLogger 容错——对齐 WeChatEventChannel 先例）。</summary>
    public DingTalkEventChannel(IDomainUser user, ChannelConfig? channel = null, ILogger<DingTalkEventChannel>? logger = null) : base(user)
    {
        _channel = channel;
        // 模板实例（集合类型索引源——ctor channel=null）可能直构不经 DI——logger 缺失时 NullLogger 容错（不抛）
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DingTalkEventChannel>.Instance;
    }

    /// <inheritdoc />
    public string ChannelType => "dingtalk_event";

    /// <inheritdoc />
    public string ChannelId => _channel?.ChannelId ?? "";

    /// <inheritdoc />
    public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (_channel is null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0));   // 模板实例不可直接认证
        if (!_channel.IsEnabled)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_DISABLED", 0));

        // 平台特定负载（Extra——CorpId/Token/EncodingAESKey 事件验签三道闸门信任根）
        _channel.Extra.TryGetValue("CorpId", out var corpId);
        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        if (string.IsNullOrEmpty(corpId) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "DINGTALK_EVENT_CONFIG_MISSING", 0));

        if (context.Parameters == null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "DINGTALK_EVENT_PARAMS_MISSING", 0));

        // P0-1 双键双位探查：签名键 signature 官方主/msg_signature 别名；密文 encrypt 官方主/msg_encrypt 别名
        //（query 双键 + __raw body JSON 双键任一兜底）
        var (signature, timestamp, nonce, msgEncrypt) = ExtractVerifyParams(context.Parameters, ct);
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "DINGTALK_EVENT_PARAMS_MISSING", 0));

        // 信任根一票否决：验签失败 → 拒绝（绝不解密伪造密文）
        if (!DingTalkEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "DINGTALK_SIGNATURE_INVALID", 0));

        try
        {
            // 安全模式 AES 解密 + receiveid 校验（第二道闸门——corpId 不匹配抛）→ 事件体 JSON → FromUserId(openId)
            // ⚠️ CPU-only 无出站调用（1200ms 硬约束 P1-2——库侧 <50ms，落库归装配层）
            var json = DingTalkEventCrypto.DecryptMsg(encodingAesKey!, msgEncrypt!, corpId!);
            string? fromUserId;
            try
            {
                fromUserId = DingTalkEventCrypto.ExtractFromUserId(json);
            }
            catch (InvalidOperationException) when (ct.IsCancellationRequested is false)
            {
                // P1-2 放行设计：解密成功但无 FromUserId（check_url / user_add_org 等组织事件——
                // ExtractFromUserId L141-163 缺字段抛 InvalidOperationException）→ 放行 success(null)，不再 400——
                // 事件实机回执可达（200 加密信封），编排路径由 HandleEventAsync 无身份守卫 NotApplicable 跳过
                _logger.LogInformation("钉钉事件入站无 FromUserId（check_url/组织事件——P1-2 放行）：ChannelId={ChannelId}", _channel.ChannelId);
                return Task.FromResult(new SsoChannelAuthResult(true, null, null, 0));
            }
            return Task.FromResult(new SsoChannelAuthResult(true, fromUserId, null, 2));  // AuthLevel=2 对齐微信便捷语义
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法/receiveid 不匹配——降级失败（信任根仍通过签名；密文损坏/org 不符返回失败）
            return Task.FromResult(new SsoChannelAuthResult(false, null, "DINGTALK_EVENT_DECRYPT_FAILED", 0));
        }
    }

    /// <inheritdoc />
    public Task<string?> VerifyUrlAsync(ChannelInboundContext context, CancellationToken ct = default)
    {
        // 钉钉回调 URL 验证/事件回执应答应答（P0-2）——任意合法入站（check_url 或业务事件）→ 加密 JSON 应答信封
        // （非明文 success——钉钉官方要求所有回调以加密 JSON 信封回执）；验签/解密失败/缺参/模板/禁用 → null
        // （装配层 400 语义）。本方法可被直连调用（装配层门扩展段 1 + 直调）——验签信任根须自持（纵深防御）。
        if (_channel is null)
            return Task.FromResult<string?>(null);
        if (!_channel.IsEnabled)
        {
            _logger.LogWarning("钉钉 URL 验证/事件回执拒绝：通道已禁用 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        _channel.Extra.TryGetValue("CorpId", out var corpId);
        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        if (string.IsNullOrEmpty(corpId) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey))
        {
            _logger.LogWarning("钉钉 URL 验证/事件回执拒绝：凭证缺失（CorpId/Token/EncodingAESKey）ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        if (context.Parameters == null)
        {
            _logger.LogWarning("钉钉 URL 验证/事件回执拒绝：参数包缺失 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        // P0-1 双键双位探查（同 AuthenticateAsync）
        var (signature, timestamp, nonce, msgEncrypt) = ExtractVerifyParams(context.Parameters, ct);
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
        {
            _logger.LogWarning("钉钉 URL 验证/事件回执拒绝：验签参数缺失 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        // 信任根一票否决：验签失败 → null（绝不解密伪造密文）
        if (!DingTalkEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
        {
            _logger.LogWarning("钉钉 URL 验证/事件回执失败：signature 校验不通过（信任根一票否决）ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        try
        {
            // 二道闸门解密 + EventType 判定基于解密 JSON（P1-5——check_url/业务事件统一回信封；JSON 非法 → null）
            var json = DingTalkEventCrypto.DecryptMsg(encodingAesKey!, msgEncrypt!, corpId!);
            string? eventType;
            try
            {
                using var doc = JsonDocument.Parse(json);
                eventType = doc.RootElement.TryGetProperty("EventType", out var et) ? et.GetString() : null;
            }
            catch (JsonException)
            {
                // 解密成功但 JSON 非法——fail-closed null（不误应答业务）
                _logger.LogWarning("钉钉 URL 验证/事件回执失败：解密 JSON 非法 ChannelId={ChannelId}", _channel.ChannelId);
                return Task.FromResult<string?>(null);
            }
            _logger.LogInformation("钉钉 URL 验证/事件回执通过：ChannelId={ChannelId} EventType={EventType}", _channel.ChannelId, eventType ?? "");

            // 加密应答信封（P0-2，零新 crypto——复用既有 EncryptMsg 生产原语 + ComputeSignature 四字段）：
            // 信封明文 = "success"（钉钉官方回执语义）；四字段名/大小写按官方回调文档构造
            var encrypt = DingTalkEventCrypto.EncryptMsg("success", encodingAesKey!, corpId!);
            var msgSignature = DingTalkEventCrypto.ComputeSignature(token!, timestamp!, nonce!, encrypt);
            var envelope = JsonSerializer.Serialize(new Dictionary<string, string?>
            {
                ["msg_signature"] = msgSignature,
                ["timeStamp"] = timestamp,
                ["nonce"] = nonce,
                ["encrypt"] = encrypt,
            });
            return Task.FromResult<string?>(envelope);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法/receiveid 不匹配——失败 null（装配层 400 语义）
            _logger.LogWarning("钉钉 URL 验证/事件回执失败：AES 解密/格式异常 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }
    }

    /// <inheritdoc />
    public Task<ChannelEventOutcome> HandleEventAsync(ChannelInboundContext context, CancellationToken ct = default)
    {
        // 与 AuthenticateAsync 同款守卫（模板实例/禁用/配置缺失/参数缺失/验签失败/解密失败 → Rejected/NotApplicable）。
        // ⚠️ 重复验签+解密链（Oracle P2-2 记录——性能非安全，保留设计）：装配层端点已先验签成功（段 1/段 2），
        // 本方法仍自验——因本方法是通道契约方法，可被直连调用（不经装配层），验签信任根须自持（纵深防御）。
        if (_channel is null)
            return Task.FromResult(ChannelEventOutcome.NotApplicable);   // 模板实例（集合类型索引源）不解析
        if (!_channel.IsEnabled)
            return Task.FromResult(ChannelEventOutcome.NotApplicable);

        _channel.Extra.TryGetValue("CorpId", out var corpId);
        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        if (string.IsNullOrEmpty(corpId) || string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey))
            return Task.FromResult(ChannelEventOutcome.Rejected);         // 信任根配置缺失——绝不解析

        if (context.Parameters == null)
            return Task.FromResult(ChannelEventOutcome.Rejected);

        // P0-1 双键双位探查（同 AuthenticateAsync——query 双键 + __raw JSON body 双键任一）
        var (signature, timestamp, nonce, msgEncrypt) = ExtractVerifyParams(context.Parameters, ct);
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
            return Task.FromResult(ChannelEventOutcome.Rejected);

        // 信任根一票否决：验签失败 → 拒绝（绝不解密伪造密文）
        if (!DingTalkEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
        {
            _logger.LogWarning("钉钉事件验签失败：signature 校验不通过（信任根一票否决）ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(ChannelEventOutcome.Rejected);
        }

        try
        {
            // 二道闸门解密（receiveid=corpId 恒定时间比对内置）→ 事件 JSON 解析 → 归一 → ChannelEvent
            var json = DingTalkEventCrypto.DecryptMsg(encodingAesKey!, msgEncrypt!, corpId!);
            var (rawEventType, fromUserId, eventCorpId, userId, createAt) = ParseEventJson(json);

            // 解密成功但无 FromUserId（check_url / user_add_org 等组织事件）→ NotApplicable——不造半个事件
            // （契约 L12-13；§3.0 守卫统一判据——无 check_url 占位残留，P2-3 清理）；check_url 由 VerifyUrlAsync
            // 恒回加密信封应答（§3.2/§3.6），本方法不产 url_verify 分支（§4.1 P2-1 标注）
            if (string.IsNullOrWhiteSpace(fromUserId))
            {
                _logger.LogInformation("钉钉事件入站无 FromUserId（编排跳过——NotApplicable）：ChannelId={ChannelId} EventType={EventType}", _channel.ChannelId, rawEventType ?? "");
                return Task.FromResult(ChannelEventOutcome.NotApplicable);
            }

            // 归一（§4.1：SCAN → scan；其余原文透传；空值 unknown）+ OccurredAt（CreateAt 秒级 → UTC；空 → UtcNow）
            var normalizedType = NormalizeDingTalkEvent(rawEventType);
            var occurredAt = createAt.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(createAt.Value).UtcDateTime
                : DateTime.UtcNow;

            var evt = new ChannelEvent
            {
                ChannelId = _channel.ChannelId,
                ChannelType = "dingtalk_event",
                ExternalUserId = fromUserId,             // 解析路径保证非空——空已在守卫返回 NotApplicable
                EventType = normalizedType,
                EventKey = null,                          // 钉钉事件订阅无菜单 key/scene 概念
                Source = eventCorpId,                     // 企业维度事实来源（事件体 CorpId，可空）
                MessageId = null,                         // 钉钉事件订阅无 MsgId 概念
                OccurredAt = occurredAt,
                ReceivedAt = DateTime.UtcNow,
                RawPayload = json,                        // 解密后明文 JSON——handler/SyncProxy 透传数据源
                Attributes = new Dictionary<string, string?>
                {
                    ["dingtalk.EventType"] = rawEventType,                    // JSON EventType 原文（诊断/统计）
                    ["dingtalk.FromUserId"] = fromUserId,                     // 身份事实（同 ExternalUserId 归一值）
                    ["dingtalk.CorpId"] = eventCorpId,                        // 企业维度
                    ["dingtalk.UserId"] = userId,                             // 钉钉 userid 企业维度辅助（非 external_uid）
                    ["dingtalk.CreateAt"] = createAt?.ToString(),             // OccurredAt 原始值（秒级字符串）
                    // 装配层注入的本站点请求基址（HandleEvent → ChannelInboundContext.Parameters["base_url"]）——
                    // 透传 handler 供链接组装（对齐 wechat.base_url 先例）
                    ["dingtalk.base_url"] = context.Parameters?.TryGetValue("base_url", out var baseUrl) == true ? baseUrl : null,
                },
            };

            _logger.LogInformation("钉钉事件入站：ChannelId={ChannelId} EventType={EventType} FromUserId={FromUserId}",
                _channel.ChannelId, normalizedType, fromUserId);

            return Task.FromResult(ChannelEventOutcome.Parsed(evt));
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法/receiveid 不匹配——信任根失败（Rejected——编排短路：不记录、不回复、不回退 handler）
            _logger.LogWarning("钉钉事件解密失败：msg_encrypt AES 解密/JSON 解析异常 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(ChannelEventOutcome.Rejected);
        }
    }

    /// <summary>
    /// 钉钉事件类型归一（§4.1——对齐微信先例"记事实不翻状态"）：平台事件值与归一 EventType 一一对应。
    /// <para>SCAN → <c>scan</c>（扫码成功——FromUserId 携带 openId）；其余（user_add_org/org_dept_create 等组织
    /// 变更事实）→ 原文透传（大小写保留）；缺 EventType → <c>unknown</c> 哨兵（不 reject——解密已过信任根，记事实）。</para>
    /// <para>⚠️ <c>check_url</c> → <c>url_verify</c> 分支<b>不实现</b>（§4.1 P2-1 标注）：check_url 无 FromUserId → 本方法
    /// 已在守卫 NotApplicable 短路，实际不产；URL 验证应答归 <see cref="VerifyUrlAsync"/>（恒回加密信封）。</para>
    /// </summary>
    private static string NormalizeDingTalkEvent(string? eventType)
    {
        if (string.IsNullOrWhiteSpace(eventType))
            return "unknown";
        if (string.Equals(eventType, "SCAN", StringComparison.OrdinalIgnoreCase))
            return "scan";                                // 扫码成功（唯一归一——原文大小写保留其余）
        return eventType;                                 // 其余原文透传（记事实不翻状态）
    }

    /// <summary>
    /// 密文参数双键双位探查（P0-1——钉钉官方事件订阅 HTTP 回调模式）：官方密文参数名 = <c>encrypt</c>。
    /// <para>① 密文读取（query）：<c>encrypt</c> 优先，<c>msg_encrypt</c> 别名兜底；② 密文读取（body）：query 均无 →
    /// 从 <c>__raw</c>（body JSON——双键任一：<c>{"encrypt":"..."}</c> 或 <c>{"msg_encrypt":"..."}</c>）提取；
    /// <c>__raw</c> 非 JSON → 保持参数缺失语义（fail-closed 不误判业务）。</para>
    /// <para>签名键兼容：<c>signature</c> 优先（官方），<c>msg_signature</c> 别名兜底；<c>timestamp</c>/<c>nonce</c> 官方键名直取。</para>
    /// </summary>
    private static (string? Signature, string? Timestamp, string? Nonce, string? MsgEncrypt) ExtractVerifyParams(
        IReadOnlyDictionary<string, string?> parameters, CancellationToken ct)
    {
        // 签名键兼容（官方 signature 主 / msg_signature 别名）
        if (!parameters.TryGetValue("signature", out var signature))
            parameters.TryGetValue("msg_signature", out signature);
        parameters.TryGetValue("timestamp", out var timestamp);
        parameters.TryGetValue("nonce", out var nonce);

        // 密文 query 双键：encrypt 官方主键 / msg_encrypt 兼容别名
        if (!parameters.TryGetValue("encrypt", out var msgEncrypt))
            parameters.TryGetValue("msg_encrypt", out msgEncrypt);

        // body JSON 兜底（对齐 WeChat 先例 ExtractEncrypt 从 body 提取的同款补充语义——__raw 非 JSON 保持缺失语义）
        if (string.IsNullOrEmpty(msgEncrypt)
            && parameters.TryGetValue("__raw", out var rawBody) && !string.IsNullOrEmpty(rawBody))
        {
            try
            {
                using var doc = JsonDocument.Parse(rawBody);
                var root = doc.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (root.TryGetProperty("encrypt", out var enc) && !string.IsNullOrEmpty(enc.GetString()))
                        msgEncrypt = enc.GetString();
                    else if (root.TryGetProperty("msg_encrypt", out var encAlias) && !string.IsNullOrEmpty(encAlias.GetString()))
                        msgEncrypt = encAlias.GetString();
                }
            }
            catch (Exception) when (ct.IsCancellationRequested is false)
            {
                // __raw 非 JSON——保持原参数缺失语义（不误判业务，D-7 负路径锁定）
            }
        }
        return (signature, timestamp, nonce, msgEncrypt);
    }

    /// <summary>事件体 JSON 顶层字段读取（单独读取，不改 <see cref="DingTalkEventCrypto.ExtractFromUserId"/> 语义）。</summary>
    private static (string? EventType, string? FromUserId, string? CorpId, string? UserId, long? CreateAt) ParseEventJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var eventType = root.TryGetProperty("EventType", out var et) ? et.GetString() : null;
        var fromUserId = root.TryGetProperty("FromUserId", out var f) ? f.GetString() : null;
        var corpId = root.TryGetProperty("CorpId", out var c) ? c.GetString() : null;
        var userId = root.TryGetProperty("UserId", out var u) ? u.GetString() : null;
        long? createAt = root.TryGetProperty("CreateAt", out var ca) && ca.TryGetInt64(out var v) ? v : null;
        return (eventType, fromUserId, corpId, userId, createAt);
    }
}