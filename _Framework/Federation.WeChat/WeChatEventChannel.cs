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
public sealed class WeChatEventChannel : DomainServiceBase, ISsoChannel
{
    private readonly ChannelConfig? _channel;
    private readonly ILogger<WeChatEventChannel> _logger;

    /// <summary>构造——channel 由 <see cref="ChannelConfig"/>（工厂预取传入，POCO 非域服务）；
    /// <paramref name="channel"/> 为 null = 集合模板实例（工厂类型索引源，不直接认证——v0.3.0 多通道）。
    /// 凭证（Token/EncodingAESKey——事件验签信任根）在 <c>AuthenticateAsync</c>/<c>HandleEventAsync</c>
    /// 从 cfg.Extra 使用。<paramref name="logger"/> = 结构化日志（工厂 ActivatorUtilities 从 DI 自动解析，
    /// 无需调用点改动）。</summary>
    public WeChatEventChannel(
        IDomainUser user,
        ChannelConfig? channel = null,
        ILogger<WeChatEventChannel>? logger = null)
        : base(user)
    {
        _channel = channel;
        // 模板实例（集合类型索引源——ctor channel=null）可能直构不经 DI——logger 缺失时 NullLogger 容错（不抛）
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WeChatEventChannel>.Instance;
    }

    /// <inheritdoc />
    public string ChannelType => "wechat_event";

    /// <inheritdoc />
    public string ChannelId => _channel?.ChannelId ?? "";

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

        // 微信官方 URL 参数名 = msg_signature；装配层亦可能透传 "signature"（兼容两种命名）
        // 预声明 + 独立 TryGetValue（out var 置于复合 || 条件中编译器不保证已赋值——CS0165 保守判定）
        string? signature = null;
        string? timestamp = null;
        string? nonce = null;
        string? msgEncrypt = null;
        if (!context.Parameters.TryGetValue("msg_signature", out signature))
            context.Parameters.TryGetValue("signature", out signature);
        context.Parameters.TryGetValue("timestamp", out timestamp);
        context.Parameters.TryGetValue("nonce", out nonce);
        context.Parameters.TryGetValue("msg_encrypt", out msgEncrypt);

        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
        {
            // ⚠️ 安全模式 POST 消息推送：msg_encrypt 不在 query——在 body XML 的 <Encrypt> 字段（装配层
            // 已把 rawBody 存入 __raw）。从 __raw 提取 Encrypt 密文补充 msg_encrypt，验签/解密链继续。
            string? encryptFromBody = null;
            if (context.Parameters.TryGetValue("__raw", out var rawBody) && !string.IsNullOrEmpty(rawBody))
            {
                try
                {
                    encryptFromBody = WeChatEventCrypto.ExtractEncrypt(rawBody);
                }
                catch (Exception) when (ct.IsCancellationRequested is false)
                {
                    // __raw 非 XML——URL 验证/直调场景，保持原参数缺失语义
                }
            }
            if (!string.IsNullOrEmpty(encryptFromBody))
            {
                msgEncrypt = encryptFromBody;
                // 原地富化——下游 HandleEventAsync/编排共用（装配层构造 Dictionary 实例经 record 传入）
                if (context.Parameters is Dictionary<string, string?> dict)
                    dict["msg_encrypt"] = encryptFromBody;
            }

            if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
                || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
            {
                _logger.LogWarning("微信事件验签拒绝：签名参数缺失 ChannelId={ChannelId}", _channel.ChannelId);
                return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_EVENT_PARAMS_MISSING", 0));
            }
        }

        // 信任根一票否决：验签失败 → 拒绝（绝不解密伪造密文）
        if (!WeChatEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
        {
            _logger.LogWarning("微信事件验签失败：msg_signature 校验不通过（信任根一票否决）ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult(new SsoChannelAuthResult(false, null, "WECHAT_SIGNATURE_INVALID", 0));
        }

        try
        {
            // 安全模式 AES 解密 → FromUserName(openid)
            var xml = WeChatEventCrypto.DecryptMsg(encodingAesKey!, msgEncrypt!);
            var openId = WeChatEventCrypto.ExtractFromUserName(xml);
            _logger.LogInformation("微信事件验签通过：ChannelId={ChannelId} OpenId={OpenId}", _channel.ChannelId, openId);
            return Task.FromResult(new SsoChannelAuthResult(true, openId, null, 2));  // AuthLevel=2
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法——降级失败（信任根仍通过签名；密文损坏返回失败）
            _logger.LogWarning("微信事件解密失败：msg_encrypt AES 解密/XML 解析异常 ChannelId={ChannelId}", _channel.ChannelId);
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
    /// <param name="context">事件上下文——query 透传参数（msg_signature/signature/timestamp/nonce/echostr）。</param>
    /// <returns>回显明文 echostr（text/plain）；失败 null。</returns>
    public Task<string?> VerifyUrlAsync(SsoChannelEventContext context, CancellationToken ct = default)
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
    public Task<EventReplyResult?> HandleEventAsync(SsoChannelEventContext context, CancellationToken ct = default)
    {
        // 与 AuthenticateAsync 同款守卫（模板实例/禁用/配置缺失/参数缺失/验签失败/解密失败 → null——
        // 错误语义由装配层 AuthenticateAsync 先行映射（WebExtension 验签成功后才会调本方法）；null 仅表示无平台回复）。
        // ⚠️ 重复验签+解密链（Oracle P2-2 记录——性能非安全，保留设计）：WebExtension 端点已先验签成功，
        // 本方法仍自验——因本方法是通道契约方法，可被直连调用（不经装配层），验签信任根须自持（纵深防御）；
        // 不可用"external_user_id 已存在即跳过验签"优化（存在直调伪造 openid 绕过信任根的漏洞）。
        // Phase 2 契约微调选项（记录，不阻断）：AuthenticateAsync 返回解密 XML 供 HandleEventAsync 复用，需契约微调。
        if (_channel is null)
            return Task.FromResult<EventReplyResult?>(null);
        if (!_channel.IsEnabled)
            return Task.FromResult<EventReplyResult?>(null);

        _channel.Extra.TryGetValue("Token", out var token);
        _channel.Extra.TryGetValue("EncodingAESKey", out var encodingAesKey);
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(encodingAesKey))
            return Task.FromResult<EventReplyResult?>(null);

        if (context.Parameters == null)
            return Task.FromResult<EventReplyResult?>(null);

        // 微信官方 URL 参数名 = msg_signature；装配层亦可能透传 "signature"（兼容两种命名）
        // 预声明 + 独立 TryGetValue（out var 置于复合 || 条件中编译器不保证已赋值——CS0165 保守判定）
        string? signature = null;
        string? timestamp = null;
        string? nonce = null;
        string? msgEncrypt = null;
        if (!context.Parameters.TryGetValue("msg_signature", out signature))
            context.Parameters.TryGetValue("signature", out signature);
        context.Parameters.TryGetValue("timestamp", out timestamp);
        context.Parameters.TryGetValue("nonce", out nonce);
        context.Parameters.TryGetValue("msg_encrypt", out msgEncrypt);

        if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
            || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
        {
            // ⚠️ 安全模式 POST 消息推送：msg_encrypt 不在 query——在 body XML 的 <Encrypt> 字段（装配层
            // 已把 rawBody 存入 __raw）。从 __raw 提取 Encrypt 密文补充 msg_encrypt，验签/解密链继续
            // （对齐 AuthenticateAsync 同款提取——本方法可被直连调用，纵深防御）。
            string? encryptFromBody = null;
            if (context.Parameters.TryGetValue("__raw", out var rawBody) && !string.IsNullOrEmpty(rawBody))
            {
                try
                {
                    encryptFromBody = WeChatEventCrypto.ExtractEncrypt(rawBody);
                }
                catch (Exception) when (ct.IsCancellationRequested is false)
                {
                    // __raw 非 XML——URL 验证/直调场景，保持原参数缺失语义
                }
            }
            if (!string.IsNullOrEmpty(encryptFromBody))
            {
                msgEncrypt = encryptFromBody;
                if (context.Parameters is Dictionary<string, string?> dict)
                    dict["msg_encrypt"] = encryptFromBody;
            }

            if (string.IsNullOrEmpty(signature) || string.IsNullOrEmpty(timestamp)
                || string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(msgEncrypt))
                return Task.FromResult<EventReplyResult?>(null);
        }

        // 信任根一票否决：验签失败 → 拒绝（绝不解密伪造密文）
        if (!WeChatEventCrypto.VerifySignature(token!, timestamp!, nonce!, msgEncrypt!, signature!))
            return Task.FromResult<EventReplyResult?>(null);

        try
        {
            // 安全模式 AES 解密 → FromUserName(openid) + MsgType/Event（评审 F4）+ Content（图文 Description 源）
            var xml = WeChatEventCrypto.DecryptMsg(encodingAesKey!, msgEncrypt!);
            var openId = WeChatEventCrypto.ExtractFromUserName(xml);
            var toUserName = WeChatEventCrypto.ExtractToUserName(xml);
            var (msgType, eventValue) = WeChatEventCrypto.ExtractMsgTypeAndEvent(xml);
            var msgContent = WeChatEventCrypto.ExtractContent(xml);

            // 富化共享参数字典（评审 F4——MsgType/Event 进 Parameters，供编排/handler 消费；装配层构造
            // Dictionary 实例经 record 传入——原地富化跨调用方可见）
            if (context.Parameters is Dictionary<string, string?> dict)
            {
                dict["external_user_id"] = openId;
                dict["msg_type"] = msgType ?? "";
                dict["event"] = eventValue ?? "";
                dict["to_user_name"] = toUserName;
                dict["from_user_name"] = openId;
                if (msgContent is not null)
                    dict["content"] = msgContent;
                // Oracle P1-4：解密后明文 XML 入 Parameters——平台基类 WechatEventMessage.Parse 数据源
                // （EventKey/经纬度/PicUrl/MediaId 等未富化字段从 __decrypted_xml XDocument 提取——通道不新增 Extract）
                dict["__decrypted_xml"] = xml;
            }

            _logger.LogInformation("微信事件入站：ChannelId={ChannelId} MsgType={MsgType} Event={Event} OpenId={OpenId} Content={Content}",
                _channel.ChannelId, msgType ?? "", eventValue ?? "", openId, msgContent ?? "");

            // 正常路径：验签解密成功 + 富化完成 → 返回 null（"无平台回复"——编排器 EventOrchestrator 接收后
            // 转 handler 双桶迭代：W3 绑定桶优先 → ChannelType 桶回退。方案 §3.4——G4 W3 绑定桶对微信可达 /
            // G5 迭代权统一编排器 / G6 XML 信封上移平台基类（通道回归"验签+解密+取身份+富化"，对齐 WeCom/DingTalk））
            return Task.FromResult<EventReplyResult?>(null);
        }
        catch (Exception) when (ct.IsCancellationRequested is false)
        {
            // 解密失败/格式非法——空哨兵（Oracle P2-3）：返回非 null 空回复（与正常路径 null 区分——
            // 编排器短路，不触发 handler 双桶；200 空回复兜底由端点层保证，微信 5 秒超时规避）
            _logger.LogWarning("微信事件解密失败：msg_encrypt AES 解密/XML 解析异常 ChannelId={ChannelId}", _channel.ChannelId);
            return Task.FromResult<EventReplyResult?>(new EventReplyResult("", "text/xml"));
        }
    }
}