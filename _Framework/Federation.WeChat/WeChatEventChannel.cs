using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信事件推送通道（`ISsoChannel: wechat_event`）——公众号事件推送身份获取（形态 B/未认证号）。
/// <para>设计文档 §4.4 形态 B：用户 发消息/点 CLICK 菜单 → 微信推送事件 → 装配层接收端点
/// （验签 + AES 解密的 <b>信任根</b> 由本通道执行）→ <c>FromUserName</c>=openid → 归一
/// <c>(channel_id, openid) → uid</c> → 编排层转 accesscode → 被动回复图文链接。</para>
/// <para><b>信任根一票否决（设计文档 §8.1）</b>：<see cref="WeChatEventCrypto.VerifySignature"/> 验 msg_signature
/// + <see cref="WeChatEventCrypto.DecryptMsg"/> AES 解密——漏验 = 可伪造任意 openid = 体系崩溃。
/// 验签失败返回 <see cref="SsoChannelAuthResult"/>（FailReason=WECHAT_SIGNATURE_INVALID）。</para>
/// <para>Context 参数（装配层接收端点传入——微信推送 URL query + body）：</para>
/// <para>① 安全模式验签：<c>Parameters["signature"]</c>/<c>["timestamp"]</c>/<c>["nonce"]</c>/<c>["msg_encrypt"]</c>；
/// ② 解密后 FromUserName 即 openid。</para>
/// <para>边界：本通道只管"验签+解密+取 openid"，不负责事件业务处理/被动回复（归装配层/编排层——
/// 设计文档 §9：各公众号后端事件接收器自实现，本库提供服务端验签解密能力）。</para>
/// <para>注册：库扩展方法 <c>AddWeChatFederationChannels()</c> 内 TryAddEnumerableConstructible（ADR92）；
/// channel 业务参数经 Options 注入不占 ctor IDomainUser 槽（SSO.WeChat §5.2 P1-3）。</para>
/// <para><b>事件链（外部身份联邦 E3——ADR-Federation-外部身份联邦与密封票据适配 E3）</b>：
/// <see cref="HandleEventAsync"/> 复用验签/解密链 + 提取 MsgType/Event/Content 富化 Parameters（评审 F4）
/// + 新增富化 <c>Parameters["__decrypted_xml"]</c>（解密后明文 XML——平台基类 Parse 数据源，Oracle P1-4）→
/// 返回 <b>null</b>（"无平台回复"——编排器 EventOrchestrator 转 handler 双桶迭代：W3 绑定桶优先
/// → ChannelType 桶回退，微信可达——方案 §3.4 G4/G5）——通道回归"验签+解密+取身份+富化"，XML 信封/迭代权
/// 上移平台基类与编排器（G6）。解密失败/格式非法 → 返回空哨兵 <c>EventReplyResult("", "text/xml")</c>
/// （Oracle P2-3——与正常路径 null 区分，编排器短路空回复不触发 handler 双桶；200 空回复兜底由端点层保证）。</para>
/// </summary>
[DiContractIgnore]
public sealed class WeChatEventChannel : DomainServiceBase, ISsoChannel, IFollowChannelQuery
{
    private readonly ChannelConfig? _channel;
    private readonly WeChatApiClient? _weChatApi;
    private readonly ILogger<WeChatEventChannel> _logger;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务）；
    /// <paramref name="channel"/> 为 null = 集合模板实例（工厂类型索引源，不直接认证——v0.3.0 多通道）。
    /// 凭证（Token/EncodingAESKey——事件验签信任根）在 <c>AuthenticateAsync</c>/<c>HandleEventAsync</c>
    /// 从 cfg.Extra 使用。<paramref name="logger"/> = 结构化日志（工厂 ActivatorUtilities 从 DI 自动解析，
    /// 无需调用点改动）。<paramref name="weChatApi"/> = 出站 ApiClient（关注状态反查 IFollowChannelQuery 用——
    /// typed client DI 注入；模板实例可空）。</summary>
    public WeChatEventChannel(
        IDomainUser user,
        ChannelConfig? channel = null,
        WeChatApiClient? weChatApi = null,
        ILogger<WeChatEventChannel>? logger = null)
        : base(user)
    {
        _channel = channel;
        _weChatApi = weChatApi;
        // 模板实例（集合类型索引源——ctor channel=null）可能直构不经 DI——logger 缺失时 NullLogger 容错（不抛）
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatEventChannel>.Instance;
    }

    /// <inheritdoc />
    public string ChannelType => "wechat_event";

    /// <inheritdoc />
    public string ChannelId => _channel?.ChannelId ?? "";

    /// <summary>关注状态反查（I2 §3.5——经 cgi-bin/user/info；对账路径，不在事件路径调用）。</summary>
    /// <param name="externalUserId">openid。</param>
    /// <returns>关注状态快照；API 失败/未装配 ApiClient → null（调用方自行处置）。</returns>
    public async Task<FollowStateSnapshot?> QueryFollowStateAsync(string externalUserId, CancellationToken ct = default)
    {
        if (_channel is null || _weChatApi is null || string.IsNullOrWhiteSpace(externalUserId)) return null;
        var appId = _channel.AppId;
        if (string.IsNullOrEmpty(appId)) return null;

        var accessToken = await _weChatApi.GetAccessTokenAsync(appId, "mp", ct);
        var state = await _weChatApi.GetFollowStateAsync(accessToken, externalUserId, ct);
        if (state is null) return null;
        return new FollowStateSnapshot(state.IsFollowing, state.SubscribeTime, null);
    }

    /// <inheritdoc />
    public Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default)
    {
        if (_channel is null)
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_NOT_FOUND", 0));   // 模板实例不可直接认证
        if (!_channel.IsEnabled)
        {
            _logger.LogWarning("微信事件验签拒绝：通道已禁用 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(new SsoChannelAuthResult(false, null, "CHANNEL_DISABLED", 0));
        }

        // 平台特定负载（Extra——Token/EncodingAESKey 事件验签信任根）
        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey))
        {
            _logger.LogWarning("微信事件验签拒绝：凭证缺失（Token/EncodingAESKey）ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_EVENT_CONFIG_MISSING", 0));
        }

        if (context.Parameters == null)
        {
            _logger.LogWarning("微信事件验签拒绝：参数包缺失 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_EVENT_PARAMS_MISSING", 0));
        }

        // 双模式解析（P1-1 DMP-Lite 反馈——明文/兼容模式事件推送支持）：
        //   msg_encrypt 是否可得裁定签名语义（有密文=安全/兼容模式 4 参验签+AES 解密；
        //   无密文=明文模式 3 参验签+body 即明文 XML）；签名键兼容 msg_signature/signature 双命名。
        string? signature = null;
        string? timestamp = null;
        string? nonce = null;
        string? msgEncrypt = null;
        string? rawBody = null;
        if (!context.Parameters.TryGetValue("msg_signature", out signature))
            context.Parameters.TryGetValue("signature", out signature);
        context.Parameters.TryGetValue("timestamp", out timestamp);
        context.Parameters.TryGetValue("nonce", out nonce);
        context.Parameters.TryGetValue("msg_encrypt", out msgEncrypt);
        context.Parameters.TryGetValue("__raw", out rawBody);

        // 安全模式 POST 消息推送：msg_encrypt 不在 query——在 body XML 的 <Encrypt>（__raw 提取补充）；
        // 明文模式 body 无 <Encrypt>（ExtractEncrypt 返回 null）→ 密文保持空 → 走明文分支。
        if (string.IsNullOrEmpty(msgEncrypt) && !string.IsNullOrEmpty(rawBody))
        {
            try
            {
                var encryptFromBody = WeChatEventCrypto.ExtractEncrypt(rawBody);
                if (!string.IsNullOrEmpty(encryptFromBody))
                {
                    msgEncrypt = encryptFromBody;
                    if (context.Parameters is Dictionary<string, string?> dict)
                        dict["msg_encrypt"] = encryptFromBody;
                }
            }
            catch (Exception) when (ct.IsCancellationRequested is false)
            {
                // __raw 非 XML——URL 验证/直调场景，保持原参数缺失语义
            }
        }

        if (!string.IsNullOrEmpty(msgEncrypt))
        {
            // 安全/兼容模式：4 参验签 + AES 解密
            if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce))
            {
                _logger.LogWarning("微信事件验签拒绝：签名参数缺失 ChannelId={ChannelId}", _channel.ChannelId);
                return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_EVENT_PARAMS_MISSING", 0));
            }

            if (!WeChatEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt, signature!))
            {
                _logger.LogWarning("微信事件验签失败：msg_signature 校验不通过（信任根一票否决）ChannelId={ChannelId}", _channel.ChannelId);
                return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_SIGNATURE_INVALID", 0));
            }

            try
            {
                var xml = WeChatEventCrypto.DecryptMsg(encodingAesKey!, msgEncrypt);
                var openId = WeChatEventCrypto.ExtractFromUserName(xml);
                _logger.LogInformation("微信事件验签通过：ChannelId={ChannelId} OpenId={OpenId}", _channel.ChannelId, openId);
                return Task.FromResult(new SsoChannelAuthResult(true, openId, null, 2));
            }
            catch (Exception) when (ct.IsCancellationRequested is false)
            {
                _logger.LogWarning("微信事件解密失败：msg_encrypt AES 解密/XML 解析异常 ChannelId={ChannelId}", _channel.ChannelId);
                return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_EVENT_DECRYPT_FAILED", 0));
            }
        }

        // 明文模式：3 参验签 + body 即明文 XML
        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(rawBody))
        {
            _logger.LogWarning("微信事件验签拒绝：明文模式参数缺失（signature/timestamp/nonce/__raw）ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_EVENT_PARAMS_MISSING", 0));
        }

        // 信任根一票否决：3 参验签失败 → 拒绝（明文消息体不参与签名，验签为唯一伪造防线）
        if (!WeChatEventCrypto.VerifySignature3(token!, timestamp!, nonce!, signature!))
        {
            _logger.LogWarning("微信事件验签失败：signature 校验不通过（明文模式——信任根一票否决）ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_SIGNATURE_INVALID", 0));
        }

        try
        {
            var openId = WeChatEventCrypto.ExtractFromUserName(rawBody!);
            _logger.LogInformation("微信事件验签通过（明文模式）：ChannelId={ChannelId} OpenId={OpenId}", _channel.ChannelId, openId);
            return Task.FromResult(new SsoChannelAuthResult(true, openId, null, 2));
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            _logger.LogWarning("微信事件明文 XML 解析失败：ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_EVENT_DECRYPT_FAILED", 0));
        }
    }

    /// <summary>
    /// URL 验证握手（微信后台服务器配置验证契约——GET 请求，装配层 HandleEvent 分派）。
    /// <para>双模式判定（URL 是否带 <c>msg_signature</c>）：</para>
    /// <para>① <b>安全模式</b>（带 <c>msg_signature</c>）：<c>msg_signature = SHA1(sort(token, timestamp, nonce, echostr密文))</c>
    /// ——4 参验签（复用 <see cref="WeChatEventCrypto.VerifySignature"/>，echostr 即"待校验密文消息体"）+ AES 解密
    /// echostr（<see cref="WeChatEventCrypto.DecryptMsg"/>，明文 = random16 + len4 + echostr明文 + appid）→ 回显<b>解密明文</b>
    /// （回显密文必失败——微信官方 WXBizMsgCrypt.VerifyURL 语义）。</para>
    /// <para>② <b>明文模式</b>（无 <c>msg_signature</c>，带 <c>signature</c>）：<c>signature = SHA1(sort(token, timestamp, nonce))</c>
    /// ——3 参验签（<see cref="WeChatEventCrypto.VerifySignature3"/>）→ 原样回显 <c>echostr</c> 原文。</para>
    /// <para>返回：验签+解密成功后回显明文 echostr（text/plain）；任何失败（模板/禁用/配置缺失/参数缺失/验签失败/
    /// 解密失败）返回 null——装配层映射 400（微信后台统一显示"token 验证失败"，不区分具体原因）。</para>
    /// <para>信任根一票否决：漏验 msg_signature/signature = 可伪造任意 openid = 体系崩溃（对齐
    /// <see cref="AuthenticateAsync"/> 同款纵深防御——本方法可被直连调用，验签信任根须自持）。</para>
    /// </summary>
    /// <param name="context">入站报文上下文——query 透传参数（msg_signature/signature/timestamp/nonce/echostr）。</param>
    /// <returns>回显明文 echostr（text/plain）；失败 null。</returns>
    public Task<string?> VerifyUrlAsync(ChannelInboundContext context, CancellationToken ct = default)
    {
        if (_channel is null)
            return Task.FromResult<string?>(null);
        if (!_channel.IsEnabled)
        {
            _logger.LogWarning("微信 URL 验证拒绝：通道已禁用 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey))
        {
            _logger.LogWarning("微信 URL 验证拒绝：凭证缺失（Token/EncodingAESKey）ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        if (context.Parameters == null)
        {
            _logger.LogWarning("微信 URL 验证拒绝：参数包缺失 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        if (!context.Parameters.TryGetValue("timestamp", out var timestamp)
            || !context.Parameters.TryGetValue("nonce", out var nonce)
            || !context.Parameters.TryGetValue("echostr", out var echoStr)
            || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(echoStr))
        {
            _logger.LogWarning("微信 URL 验证拒绝：验签参数缺失 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }

        try
        {
            // 安全模式：带 msg_signature（4 参 SHA1 含 echostr 密文）——AES 解密后回显明文
            if (context.Parameters.TryGetValue("msg_signature", out var msgSignature) && !string.IsNullOrEmpty(msgSignature))
            {
                if (!WeChatEventCrypto.VerifySignature(token!, timestamp!, nonce!, echoStr!, msgSignature))
                {
                    _logger.LogWarning("微信 URL 验证失败：msg_signature 校验不通过（安全模式）ChannelId={ChannelId}", _channel.ChannelId);
                    return Task.FromResult<string?>(null);
                }

                var plain = WeChatEventCrypto.DecryptMsg(encodingAesKey!, echoStr!);
                _logger.LogInformation("微信 URL 验证通过（安全模式）：ChannelId={ChannelId} 回显解密明文", _channel.ChannelId);
                return Task.FromResult<string?>(plain);
            }

            // 明文模式：3 参 signature（signature 优先；兼容装配层透传命名）
            if (!context.Parameters.TryGetValue("signature", out var signature) || string.IsNullOrEmpty(signature))
            {
                _logger.LogWarning("微信 URL 验证拒绝：无 msg_signature 且无 signature ChannelId={ChannelId}", _channel.ChannelId);
                return Task.FromResult<string?>(null);
            }
            if (!WeChatEventCrypto.VerifySignature3(token!, timestamp!, nonce!, signature))
            {
                _logger.LogWarning("微信 URL 验证失败：signature 校验不通过（明文模式）ChannelId={ChannelId}", _channel.ChannelId);
                return Task.FromResult<string?>(null);
            }

            _logger.LogInformation("微信 URL 验证通过（明文模式）：ChannelId={ChannelId} 回显 echostr", _channel.ChannelId);
            return Task.FromResult<string?>(echoStr);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法——防御性拒（微信后台统一"token 验证失败"）
            _logger.LogWarning("微信 URL 验证解密失败：echostr AES 解密/格式异常 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<string?>(null);
        }
    }

/// <inheritdoc />
    public Task<ChannelEventOutcome> HandleEventAsync(ChannelInboundContext context, CancellationToken ct = default)
    {
        // 与 AuthenticateAsync 同款守卫（模板实例/禁用/配置缺失/参数缺失/验签失败/解密失败 → Rejected/NotApplicable——
        // 错误语义由装配层 AuthenticateAsync 先行映射（WebExtension 验签成功后才会调本方法）。
        // ⚠️ 重复验签+解密链（Oracle P2-2 记录——性能非安全，保留设计）：WebExtension 端点已先验签成功，
        // 本方法仍自验——因本方法是通道契约方法，可被直连调用（不经装配层），验签信任根须自持（纵深防御）；
        // 不可用"external_user_id 已存在即跳过验签"优化（存在直调伪造 openid 绕过信任根的漏洞）。
        if (_channel is null)
            return Task.FromResult(ChannelEventOutcome.NotApplicable);   // 模板实例（集合类型索引源）不解析
        if (!_channel.IsEnabled)
            return Task.FromResult(ChannelEventOutcome.NotApplicable);

        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey))
            return Task.FromResult(ChannelEventOutcome.Rejected);         // 信任根配置缺失——绝不解析

        if (context.Parameters == null)
            return Task.FromResult(ChannelEventOutcome.Rejected);

        // 双模式解析（P1-1——与 AuthenticateAsync 同款逻辑：msg_encrypt 可得=安全/兼容模式 4 参验签
        // +AES 解密；无 msg_encrypt=明文模式 3 参验签+body（__raw）即明文 XML。签名键兼容双命名）。
        string? signature = null;
        string? timestamp = null;
        string? nonce = null;
        string? msgEncrypt = null;
        string? rawBody = null;
        if (!context.Parameters.TryGetValue("msg_signature", out signature))
            context.Parameters.TryGetValue("signature", out signature);
        context.Parameters.TryGetValue("timestamp", out timestamp);
        context.Parameters.TryGetValue("nonce", out nonce);
        context.Parameters.TryGetValue("msg_encrypt", out msgEncrypt);
        context.Parameters.TryGetValue("__raw", out rawBody);

        // 安全模式 POST 消息推送：msg_encrypt 不在 query——在 body XML 的 <Encrypt>（__raw 提取补充）；
        // 明文模式 body 无 <Encrypt>（ExtractEncrypt 返回 null）→ 密文保持空 → 走明文分支（对齐
        // AuthenticateAsync 同款提取——本方法可被直连调用，纵深防御）。
        if (string.IsNullOrEmpty(msgEncrypt) && !string.IsNullOrEmpty(rawBody))
        {
            try
            {
                var encryptFromBody = WeChatEventCrypto.ExtractEncrypt(rawBody);
                if (!string.IsNullOrEmpty(encryptFromBody))
                {
                    msgEncrypt = encryptFromBody;
                    if (context.Parameters is Dictionary<string, string?> dict)
                        dict["msg_encrypt"] = encryptFromBody;
                }
            }
            catch (Exception) when (ct.IsCancellationRequested is false)
            {
                // __raw 非 XML——URL 验证/直调场景，保持原参数缺失语义
            }
        }

        string xml;
        if (!string.IsNullOrEmpty(msgEncrypt))
        {
            if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp) || string.IsNullOrEmpty(nonce))
                return Task.FromResult(ChannelEventOutcome.Rejected);

            if (!WeChatEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt, signature!))
                return Task.FromResult(ChannelEventOutcome.Rejected);

            try
            {
                xml = WeChatEventCrypto.DecryptMsg(encodingAesKey!, msgEncrypt);
            }
            catch (Exception) when (ct.IsCancellationRequested is false)
            {
                _logger.LogWarning("微信事件解密失败：msg_encrypt AES 解密/XML 解析异常 ChannelId={ChannelId}", _channel.ChannelId);
                return Task.FromResult(ChannelEventOutcome.Rejected);
            }
        }
        else
        {
            if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
                || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(rawBody))
                return Task.FromResult(ChannelEventOutcome.Rejected);

            // 信任根一票否决：3 参验签失败 → 拒绝（明文消息体不参与签名，验签为唯一伪造防线）
            if (!WeChatEventCrypto.VerifySignature3(token!, timestamp!, nonce!, signature!))
                return Task.FromResult(ChannelEventOutcome.Rejected);

            xml = rawBody!;
        }

        try
        {
            // 安全模式 AES 解密 / 明文模式直接透传 → FromUserName(openid) + MsgType/Event（评审 F4）
            // + Content（图文 Description 源）
            var openId = WeChatEventCrypto.ExtractFromUserName(xml);
            var toUserName = WeChatEventCrypto.ExtractToUserName(xml);
            var (msgType, eventValue) = WeChatEventCrypto.ExtractMsgTypeAndEvent(xml);
            var msgContent = WeChatEventCrypto.ExtractContent(xml);
            var eventKey = WeChatEventCrypto.ExtractEventKey(xml);
            var createTime = WeChatEventCrypto.ExtractCreateTime(xml);
            var msgId = WeChatEventCrypto.ExtractMsgId(xml);

            // 解密成功但 openid 不可得——按方案 §5.3「解析不出 openid → NotApplicable」处理
            if (string.IsNullOrWhiteSpace(openId))
            {
                _logger.LogWarning("微信事件入站缺 openid：ChannelId={ChannelId}", _channel.ChannelId);
                return Task.FromResult(ChannelEventOutcome.NotApplicable);
            }

            // I2-7 语义层精化：EventType 归一（平台厂商语义 → 关注域契约值）+ Attributes 键 wechat. 前缀
            // （oracle6 C6——跨平台键冲突消除）+ OccurredAt（CreateTime 秒级 → UTC）+ EventKey/Source/MessageId 赋值
            var (normalizedType, normalizedKey, source) = NormalizeWechatEvent(msgType, eventValue, eventKey);
            var occurredAt = createTime.HasValue
                ? DateTimeOffset.FromUnixTimeSeconds(createTime.Value).UtcDateTime
                : DateTime.UtcNow;

            var evt = new ChannelEvent
            {
                ChannelId = _channel.ChannelId,
                ChannelType = "wechat_event",
                ExternalUserId = openId,
                EventType = normalizedType,
                EventKey = normalizedKey,
                Source = source,
                MessageId = msgId,
                OccurredAt = occurredAt,
                ReceivedAt = DateTime.UtcNow,
                RawPayload = xml,   // 解密后明文 XML——handler/sync 透传数据源（FromChannelEvent 重建）
                Attributes = new Dictionary<string, string?>
                {
                    ["wechat.MsgType"] = msgType,
                    ["wechat.Event"] = eventValue,
                    ["wechat.ToUserName"] = toUserName,
                    ["wechat.FromUserName"] = openId,
                    ["wechat.Content"] = msgContent,
                    ["wechat.CreateTime"] = createTime?.ToString(),
                    // 装配层注入的本站点请求基址（HandleEvent → ChannelInboundContext.Parameters["base_url"]）——
                    // 透传 handler 供免登链接组装（BaseUrl 解析链第三级：ChannelBaseUrls → TestTicketEndpointBaseUrl → 本值）
                    ["wechat.base_url"] = context.Parameters?.TryGetValue("base_url", out var baseUrl) == true ? baseUrl : null,
                },
            };

            _logger.LogInformation("微信事件入站：ChannelId={ChannelId} MsgType={MsgType} Event={Event} OpenId={OpenId} Content={Content}",
                _channel.ChannelId, msgType ?? "", eventValue ?? "", openId, msgContent ?? "");

            return Task.FromResult(ChannelEventOutcome.Parsed(evt));
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法——信任根失败（Rejected——编排短路，200 空回复兜底由端点层保证，微信 5 秒超时规避）
            _logger.LogWarning("微信事件解密失败：msg_encrypt AES 解密/XML 解析异常 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(ChannelEventOutcome.Rejected);
        }
    }

    /// <summary>
    /// 微信事件类型归一（I2 §3.3 语义层——平台厂商语义 → 关注域契约值）。
    /// <para>映射（§4.1 EventType 枚举语义）：</para>
    /// <para>① <c>subscribe</c>（无 EventKey）→ <c>follow</c>——普通关注；带 <c>qrscene_</c> 前缀 EventKey → <c>scan_follow</c>
    /// （扫码关注——EventKey 去前缀存 EventKey，Source=scene id）；</para>
    /// <para>② <c>unsubscribe</c> → <c>unfollow</c>；③ <c>SCAN</c>（已关注用户扫码）→ <c>scan_only</c>
    /// （EventKey 去前缀，Source=scene id）；④ <c>CLICK</c> → <c>menu_click</c>（EventKey=菜单 key）；</para>
    /// <para>⑤ 其余事件值（VIEW/LOCATION/TEMPLATESENDJOBFINISH 等）→ 事件值原文（关注域不翻状态——记事实）；</para>
    /// <para>⑥ 消息类（MsgType != event）→ <c>message</c>（MsgId 承载，EventKey 不适用）。</para>
    /// <para>返回 (归一 EventType, 归一 EventKey, Source)。</para>
    /// </summary>
    private static (string Type, string? Key, string? Source) NormalizeWechatEvent(string? msgType, string? eventValue, string? eventKey)
    {
        // 消息类（text/image/voice...）——非事件
        if (!string.Equals(msgType, "event", StringComparison.OrdinalIgnoreCase))
            return ("message", null, null);

        switch (eventValue)
        {
            case "subscribe":
                // 无 EventKey（普通关注）→ follow；带 qrscene_ 前缀 → scan_follow（扫码关注）
                return string.IsNullOrEmpty(eventKey)
                    ? ("follow", null, null)
                    : NormalizeSceneKey(eventKey, follow: true);
            case "unsubscribe":
                return ("unfollow", null, null);
            case "SCAN":
                return NormalizeSceneKey(eventKey, follow: false);
            case "CLICK":
                return ("menu_click", eventKey, null);
            default:
                // 其余事件（VIEW/LOCATION/TEMPLATESENDJOBFINISH...）——原文保留（记事实不翻状态）
                return (eventValue ?? "unknown", eventKey, null);
        }
    }

    /// <summary>扫码事件（subscribe+qrscene_/SCAN）——EventKey 去 <c>qrscene_</c> 前缀 + Source=scene id（R5 统计维度）。</summary>
    private static (string Type, string? Key, string? Source) NormalizeSceneKey(string? eventKey, bool follow)
    {
        var scene = eventKey;
        if (scene is not null && scene.StartsWith("qrscene_", StringComparison.Ordinal))
            scene = scene["qrscene_".Length..];
        return (follow ? "scan_follow" : "scan_only", scene, scene);
    }
}