using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.WeCom;

/// <summary>
/// 企业微信通讯录/事件回调通道（`ISsoChannel: wecom_event`）——组织变更/事件推送入站通知。
/// <para>流程（M4 方案 §3.2）：企业微信通讯录变更/事件推送 → 装配层接收端点（映射入站）→ 本通道
/// <see cref="AuthenticateAsync"/> 执行<b>三道闸门</b>：① <see cref="WeComEventCrypto.VerifySignature"/>
/// 验签（信任根一票否决）；② <see cref="WeComEventCrypto.DecryptAndVerify"/> AES 解密并<b>校验 ReceiveId
/// （自建 corpid / 三方 suiteid——Oracle 评审 P1-1：防跨企业回调混串）</b>；③
/// <see cref="WeComEventCrypto.ExtractFromXml"/> 提取 <c>UserID</c>/<c>OpenUserID</c> → external_uid 双策略
/// （三方 <c>open_userid</c> / 自建 <c>{CorpId}:{userid}</c> 复合——Oracle 评审 P0-1）→
/// 返回 <see cref="SsoChannelAuthResult"/>。回调只做增量/解绑通知——官方"无法保证 100% 回调成功"→
/// 业务不强依赖回调（认证主路径走 OAuth 出站）。</para>
/// <para>多通道联邦（v0.3.0）：channel 由 <see cref="ChannelConfig"/>（工厂预取传入）——<c>null</c> = 集合模板实例
/// （不直接认证）。凭证（CorpId/Token/EncodingAESKey）从 <see cref="ChannelConfig.Extra"/> 使用
/// （M7：入站信任根负载进 Extra，不挤占公共列）。</para>
/// <para><b>I2-2 契约跟进（2026-11-09，V0.4.2）</b>：<see cref="HandleEventAsync"/> 验签→解密→事件归一→产出
/// <see cref="ChannelEventOutcome.Parsed(ChannelEvent)"/>（`wecom.` 前缀 Attributes / OccurredAt UTC / RawPayload
/// 明文 XML——编排链 uid 只读映射 + handler 双桶迭代可达，WC-1 闭环）；<see cref="VerifyUrlAsync"/>（WC-2）GET
/// echostr URL 验证握手——<b>委托 <see cref="WeComEventCrypto.VerifyAndDecryptEcho"/></b>（验签 + AES 解密 echostr →
/// 回显明文；失败归一 null——装配层 400 语义）。wecom_event 能力面声明（<c>OAuth | Event</c>，<b>不声明 Follow</b>
/// ——企微关注应用事件按"记事实不翻状态"原文透传，P1-3）。</para>
/// <para>Context 参数（装配层接收端点传入——推送 URL query + body）：<c>Parameters["msg_signature"]</c>（企业微信
/// 官方参数名；兼容 "signature" 别名）/<c>["timestamp"]</c>/<c>["nonce"]</c>/<c>["msg_encrypt"]</c>。</para>
/// <para>注册：库扩展方法 <c>AddWeComFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92）；
/// 集合元素 = 模板实例（channel=null），真实实例由工厂帧内 ActivatorUtilities 构造。</para>
/// </summary>
[DiContractIgnore]
public sealed class WeComEventChannel : DomainServiceBase, ISsoChannel
{
    private readonly ChannelConfig? _channel;
    private readonly ILogger<WeComEventChannel> _logger;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务）；
    /// <paramref name="channel"/> 为 null = 集合模板实例（工厂类型索引源，不直接认证——v0.3.0 多通道）。
    /// 凭证（Token/EncodingAESKey/CorpId——三道闸门信任根 + receiveid 期望）在 <c>AuthenticateAsync</c> 从 cfg.Extra 使用。
    /// <paramref name="logger"/> = 结构化日志（工厂 ActivatorUtilities 从 DI 自动解析；模板实例直构时 null →
    /// NullLogger 容错——对齐 WeChatEventChannel 先例）。</summary>
    public WeComEventChannel(IDomainUser user, ChannelConfig? channel = null, ILogger<WeComEventChannel>? logger = null) : base(user)
    {
        _channel = channel;
        // 模板实例（集合类型索引源——ctor channel=null）可能直构不经 DI——logger 缺失时 NullLogger 容错（不抛）
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WeComEventChannel>.Instance;
    }

    /// <inheritdoc />
    public string ChannelType => "wecom_event";

    /// <inheritdoc />
    public string ChannelId => _channel?.ChannelId ?? "";

    /// <inheritdoc />
    public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (_channel is null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0));   // 模板实例不可直接认证
        if (!_channel.IsEnabled)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_DISABLED", 0));

        // 平台特定负载（Extra——CorpId/Token/EncodingAESKey 三道闸门信任根 + receiveid 期望）
        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        _channel.Extra.TryGetValue("CorpId", out var corpId);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey) || string.IsNullOrEmpty(corpId))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_EVENT_CONFIG_MISSING", 0));

        if (context.Parameters == null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_EVENT_PARAMS_MISSING", 0));

        // 企业微信官方 URL 参数名 = msg_signature；装配层亦可能透传 "signature"（兼容两种命名）
        if (!context.Parameters.TryGetValue("msg_signature", out var signature))
            context.Parameters.TryGetValue("signature", out signature);
        if (!context.Parameters.TryGetValue("timestamp", out var timestamp)
            || !context.Parameters.TryGetValue("nonce", out var nonce)
            || !context.Parameters.TryGetValue("msg_encrypt", out var msgEncrypt)
            || string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_EVENT_PARAMS_MISSING", 0));

        // 信任根一票否决：验签失败 → 拒绝（绝不解密伪造密文）
        if (!WeComEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_SIGNATURE_INVALID", 0));

        try
        {
            // AES 解密 + ReceiveId 校验（第二道闸门——自建 corpid / 三方 suiteid，不符抛）→ 事件体 XML → 身份提取
            // ⚠️ CPU-only 无出站调用（回调不阻塞业务——验签+解密+落库归阻塞路径，业务异步）
            var xml = WeComEventCrypto.DecryptAndVerify(encodingAesKey!, msgEncrypt!, corpId!);
            var (userId, openUserId) = WeComEventCrypto.ExtractFromXml(xml);

            // external_uid 双策略（Oracle 评审 P0-1）——三方 open_userid（全局唯一）/ 自建 {CorpId}:{userid} 复合
            string? externalUserId;
            if (!string.IsNullOrEmpty(openUserId))
                externalUserId = openUserId;
            else if (!string.IsNullOrEmpty(userId))
                externalUserId = $"{corpId}:{userId}";
            else
                return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_EVENT_IDENTITY_MISSING", 0));

            return Task.FromResult(new SsoChannelAuthResult(true, externalUserId, null, 2));  // AuthLevel=2
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法/receiveid 不匹配——降级失败（信任根仍通过签名；密文损坏/企业不符返回失败）
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECOM_EVENT_DECRYPT_FAILED", 0));
        }
    }

    /// <inheritdoc />
    public Task<string?> VerifyUrlAsync(ChannelInboundContext context, CancellationToken ct = default)
    {
        // GET echostr URL 验证握手（WC-2——直接委托 WeComEventCrypto.VerifyAndDecryptEcho）：
        // 验签 + AES 解密 echostr → 回显明文（1s 硬约束）；任何失败（缺参/验签失败/解密失败/模板/禁用）→ null
        // （装配层映射 400——企业微信后台保存回调 URL 校验闭环）。
        if (_channel is null)
            return Task.FromResult<string?>(null);
        if (!_channel.IsEnabled)
        {
            _logger.LogWarning("企业微信 URL 验证拒绝：通道已禁用 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        _channel.Extra.TryGetValue("CorpId", out var corpId);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey) || string.IsNullOrEmpty(corpId))
        {
            _logger.LogWarning("企业微信 URL 验证拒绝：凭证缺失（Token/EncodingAESKey/CorpId）ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        if (context.Parameters == null)
        {
            _logger.LogWarning("企业微信 URL 验证拒绝：参数包缺失 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        // 企微官方 GET 验证参数（无 signature 明文模式分支——文档以 msg_signature 唯一签名键；signature 别名兼容保留
        // 对齐库内双命名惯例）——缺任一 → null
        if (!context.Parameters.TryGetValue("msg_signature", out var msgSignature))
            context.Parameters.TryGetValue("signature", out msgSignature);
        if (!context.Parameters.TryGetValue("timestamp", out var timestamp)
            || !context.Parameters.TryGetValue("nonce", out var nonce)
            || !context.Parameters.TryGetValue("echostr", out var echostr)
            || string.IsNullOrEmpty(msgSignature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(echostr))
        {
            _logger.LogWarning("企业微信 URL 验证拒绝：验签参数缺失 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        try
        {
            // 直接委托既有 VerifyAndDecryptEcho 原语（WC-2 评审建议原文）——验签失败返回 null（回显失败语义），
            // 解密/ReceiveId 校验失败抛 → 整体 catch 归一 null（对齐微信先例——解密失败回 null 让装配层 400，
            // 不产生 503 噪音）
            var echoPlain = WeComEventCrypto.VerifyAndDecryptEcho(
                token!, timestamp!, nonce!, echostr!, msgSignature!, encodingAesKey!, corpId!);
            _logger.LogInformation("企业微信 URL 验证通过：ChannelId={ChannelId} 回显解密明文", _channel.ChannelId);
            return Task.FromResult<string?>(echoPlain);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            _logger.LogWarning("企业微信 URL 验证失败：echostr AES 解密/ReceiveId 校验异常 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }
    }

    /// <inheritdoc />
    public Task<ChannelEventOutcome> HandleEventAsync(ChannelInboundContext context, CancellationToken ct = default)
    {
        // 与 AuthenticateAsync 同款守卫（模板实例/禁用/配置缺失/参数缺失/验签失败/解密失败 → Rejected/NotApplicable）。
        // ⚠️ 重复验签+解密链（Oracle P2-2 记录——性能非安全，保留设计）：装配层端点已先验签成功，本方法仍自验——
        // 本方法是通道契约方法，可被直连调用（不经装配层），验签信任根须自持（纵深防御）。
        if (_channel is null)
            return Task.FromResult(ChannelEventOutcome.NotApplicable);   // 模板实例（集合类型索引源）不解析
        if (!_channel.IsEnabled)
            return Task.FromResult(ChannelEventOutcome.NotApplicable);

        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        _channel.Extra.TryGetValue("CorpId", out var corpId);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey) || string.IsNullOrEmpty(corpId))
            return Task.FromResult(ChannelEventOutcome.Rejected);         // 信任根配置缺失——绝不解析

        if (context.Parameters == null)
            return Task.FromResult(ChannelEventOutcome.Rejected);

        // msg_signature 官方主/signature 别名 + 密文 msg_encrypt query 或 __raw body XML <Encrypt> 提取（同 AuthenticateAsync）
        var (signature, timestamp, nonce, msgEncrypt) = ExtractVerifyParams(context.Parameters, ct);
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
            return Task.FromResult(ChannelEventOutcome.Rejected);

        // 信任根一票否决：验签失败 → 拒绝（绝不解密伪造密文）
        if (!WeComEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
        {
            _logger.LogWarning("企业微信事件验签失败：msg_signature 校验不通过（信任根一票否决）ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(ChannelEventOutcome.Rejected);
        }

        try
        {
            // 二道闸门解密（ReceiveId=corpid/suiteid 恒定时间比对内置）→ 事件 XML 解析 → 归一 → ChannelEvent
            var xml = WeComEventCrypto.DecryptAndVerify(encodingAesKey!, msgEncrypt!, corpId!);
            var (eventValue, changeType, toUserName, fromUserName, userId, openUserId, eventKey, createTime) = ParseEventXml(xml);

            // external_uid 统一三源解析链（§4.3——OpenUserID → UserID 复合 → FromUserName 兜底，三源取一；
            // 关注事件 subscribe/unsubscribe 身份 = FromUserName（openid/userid 形态按应用语境））
            string? externalUserId;
            if (!string.IsNullOrEmpty(openUserId))
                externalUserId = openUserId;
            else if (!string.IsNullOrEmpty(userId))
                externalUserId = $"{corpId}:{userId}";
            else if (!string.IsNullOrEmpty(fromUserName))
                externalUserId = fromUserName;
            else
            {
                // 解密成功但身份标识全空 → NotApplicable——不造半个事件（契约 L12-13）
                _logger.LogInformation("企业微信事件入站缺身份标识（编排跳过——NotApplicable）：ChannelId={ChannelId} Event={Event}",
                    _channel.ChannelId, eventValue ?? "");
                return Task.FromResult(ChannelEventOutcome.NotApplicable);
            }

            // 归一（§4.3：change_contact 复合保留子类型；subscribe/unsubscribe 原文透传——P1-3 选项 b 不翻状态；
            // 缺 Event/ChangeType → unknown）+ OccurredAt（CreateTime 秒级 → UTC；空 → UtcNow）
            var (normalizedType, source) = NormalizeWeComEvent(eventValue, changeType);
            var occurredAt = createTime.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(createTime.Value).UtcDateTime
                : DateTime.UtcNow;

            var evt = new ChannelEvent
            {
                ChannelId = _channel.ChannelId,
                ChannelType = "wecom_event",
                ExternalUserId = externalUserId,
                EventType = normalizedType,
                EventKey = eventKey,                          // 点击事件 key 透传（可空）
                Source = source,                              // ChangeType 或事件来源（统计维度）
                MessageId = null,                             // 企微无 MsgId 概念
                OccurredAt = occurredAt,
                ReceivedAt = DateTime.UtcNow,
                RawPayload = xml,                             // 解密后明文 XML——handler/SyncProxy 透传数据源
                Attributes = new Dictionary<string, string?>
                {
                    ["wecom.Event"] = eventValue,             // XML Event 原文（诊断）
                    ["wecom.ChangeType"] = changeType,        // 通讯录子类型（统计维度）
                    ["wecom.ToUserName"] = toUserName,        // 消息目标企业（CorpId）
                    ["wecom.FromUserName"] = fromUserName,    // 关注事件触发者（诊断）
                    ["wecom.UserID"] = userId,                // 自建成员 userid
                    ["wecom.OpenUserID"] = openUserId,        // 三方成员 open_userid
                    ["wecom.CreateTime"] = createTime?.ToString(),   // OccurredAt 原始值（秒级字符串）
                    // 装配层注入的本站点请求基址（HandleEvent → ChannelInboundContext.Parameters["base_url"]）——
                    // 透传 handler 供链接组装（对齐 wechat.base_url 先例）
                    ["wecom.base_url"] = context.Parameters?.TryGetValue("base_url", out var baseUrl) == true ? baseUrl : null,
                },
            };

            _logger.LogInformation("企业微信事件入站：ChannelId={ChannelId} EventType={EventType} ExternalUserId={ExternalUserId}",
                _channel.ChannelId, normalizedType, externalUserId);

            return Task.FromResult(ChannelEventOutcome.Parsed(evt));
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法/receiveid 不匹配——信任根失败（Rejected——编排短路：不记录、不回复、不回退 handler）
            _logger.LogWarning("企业微信事件解密失败：msg_encrypt AES 解密/XML 解析异常 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(ChannelEventOutcome.Rejected);
        }
    }

    /// <summary>
    /// 企业微信事件类型归一（§4.3——"记事实不翻状态"）：
    /// <para>① <c>change_contact</c> + <c>ChangeType</c> → 复合 <c>change_contact:{ChangeType}</c>（同时保留事件面与
    /// 子类型面，Statistics 维度不丢——§4.3 建议；自建/三方通讯录事实）；</para>
    /// <para>② <c>subscribe</c>/<c>unsubscribe</c>（成员关注应用事件）→ <b>原文透传</b>（P1-3 定案选项 b——**不产
    /// follow/unfollow、不翻关注状态**：企微成员关注应用事件按"记事实不翻状态"；<c>FollowStateService</c> 以事件类型
    /// 判据仅对 follow/unfollow/scan_follow 翻状态，subscribe 只落事实行）；</para>
    /// <para>③ 其余事件（enter_agent/click 等）→ 原文透传；缺 Event/ChangeType → <c>unknown</c> 哨兵。</para>
    /// </summary>
    /// <returns>(归一 EventType, Source)——Source = ChangeType 或事件来源（可空）。</returns>
    private static (string Type, string? Source) NormalizeWeComEvent(string? eventValue, string? changeType)
    {
        if (string.IsNullOrWhiteSpace(eventValue) && string.IsNullOrWhiteSpace(changeType))
            return ("unknown", changeType);
        // change_contact：复合保留子类型（§4.3 建议——Statistics 维度不丢）
        if (string.Equals(eventValue, "change_contact", StringComparison.Ordinal) && !string.IsNullOrWhiteSpace(changeType))
            return ($"change_contact:{changeType}", changeType);
        // subscribe/unsubscribe/其余：原文透传（记事实不翻状态——P1-3）
        return (eventValue ?? changeType ?? "unknown", changeType);
    }

    /// <summary>
    /// 密文参数探查：企业微信官方 URL 参数 <c>msg_signature</c>（兼容 "signature" 别名）/timestamp/nonce +
    /// 密文 <c>msg_encrypt</c>（query 或 <c>__raw</c> body XML <c>&lt;Encrypt&gt;</c> 提取——对齐微信先例
    /// <c>WeChatEventChannel.cs:127-143</c> 的 <c>ExtractEncrypt</c> 兜底提取模式；<c>__raw</c> 非 XML → 保持缺失语义）。
    /// </summary>
    private static (string? Signature, string? Timestamp, string? Nonce, string? MsgEncrypt) ExtractVerifyParams(
        IReadOnlyDictionary<string, string?> parameters, CancellationToken ct)
    {
        if (!parameters.TryGetValue("msg_signature", out var signature))
            parameters.TryGetValue("signature", out signature);
        parameters.TryGetValue("timestamp", out var timestamp);
        parameters.TryGetValue("nonce", out var nonce);
        if (!parameters.TryGetValue("msg_encrypt", out var msgEncrypt))
            msgEncrypt = null;

        // 安全模式 POST 消息推送：msg_encrypt 不在 query——在 body XML 的 <Encrypt>（__raw 提取补充）
        if (string.IsNullOrEmpty(msgEncrypt)
            && parameters.TryGetValue("__raw", out var rawBody) && !string.IsNullOrEmpty(rawBody))
        {
            try
            {
                var doc = XDocument.Parse(rawBody);
                var enc = doc.Root?.Element("Encrypt")?.Value;
                if (!string.IsNullOrWhiteSpace(enc))
                    msgEncrypt = enc.Trim();
            }
            catch (System.Xml.XmlException) when (ct.IsCancellationRequested is false)
            {
                // __raw 非 XML——URL 验证/直调场景，保持原参数缺失语义
            }
        }
        return (signature, timestamp, nonce, msgEncrypt);
    }

    /// <summary>事件体 XML 顶层字段读取（复用 XDocument，不改 <see cref="WeComEventCrypto.ExtractFromXml"/> 语义）。</summary>
    private static (string? Event, string? ChangeType, string? ToUserName, string? FromUserName,
        string? UserId, string? OpenUserId, string? EventKey, long? CreateTime) ParseEventXml(string xml)
    {
        XDocument doc;
        try
        {
            doc = XDocument.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            throw new System.IO.InvalidDataException("企业微信回调 XML 非法");
        }
        var root = doc.Root;
        static string? Read(XElement? r, string name)
        {
            var value = r?.Element(name)?.Value;
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }
        var eventValue = Read(root, "Event");
        var changeType = Read(root, "ChangeType");
        var toUserName = Read(root, "ToUserName");
        var fromUserName = Read(root, "FromUserName");
        var userId = Read(root, "UserID");
        var openUserId = Read(root, "OpenUserID");
        var eventKey = Read(root, "EventKey");
        long? createTime = long.TryParse(root?.Element("CreateTime")?.Value, out var v) ? v : null;
        return (eventValue, changeType, toUserName, fromUserName, userId, openUserId, eventKey, createTime);
    }
}