using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using TKWF.Ext.Federation;

namespace TKWF.Federation.WeChat;

/// <summary>
/// 微信消息/事件上下文（方案 §3.2——平台字段类型化，子类零 XML 解析、零 Parameters 裸读）。
/// <para>字段来源分两层（Oracle P1-4 采纳——<c>__decrypted_xml</c> 为通道存解密后明文 XML）：</para>
/// <para>① <b>Parameters 基础字段</b>（通道已富化——<see cref="WeChatEventChannel"/>）：<c>msg_type</c> /
/// <c>event</c> / <c>from_user_name</c>（openid）/ <c>to_user_name</c>（公众号 id）/ <c>external_user_id</c>
/// （openid 归一）/ <c>content</c>（文本消息原文）/<c>uid</c>（编排层只读富化）/ <c>channel_id</c>（装配层传入）；</para>
/// <para>② <b>未富化字段</b>（<see cref="Parse"/> 从 <c>__decrypted_xml</c> XDocument 提取——CDATA 感知、
/// 缺节点容错返回 null）：EventKey/纬度/经度/精度/Scale/Label/PicUrl/MediaId/Format/ThumbMediaId/
/// Title/Description/Url/群发统计（Status/TotalCount/FilterCount/SentCount/ErrorCount）。</para>
/// <para><b>容错</b>：<c>__decrypted_xml</c> 缺失（明文模式/未解密场景）→ 未富化字段全 null（不抛）；
/// 存在但非法 XML → 抛 <see cref="InvalidOperationException"/>（对齐 <see cref="WeChatEventCrypto"/>
/// 形态——数据已解密即应可解析，解析失败 = 上游异常）。</para>
/// <para><see cref="SceneId"/>：SCAN 事件场景值（<see cref="EventKey"/> 去 <c>qrscene_</c> 前缀——
/// 方案 §3.2 OnScanAsync 强类型参数）。</para>
/// </summary>
public sealed record WechatEventContext(
    string? MsgType,                    // msg_type（通道富化——text/image/voice/video/shortvideo/location/link/event）
    string? Event,                      // event（事件类型——subscribe/unsubscribe/SCAN/CLICK/VIEW/LOCATION/...）
    string? FromUserName,               // from_user_name（openid——原消息发送者）
    string? ToUserName,                 // to_user_name（公众号 id——被动回复 FromUserName 用）
    string? EventKey,                   // EventKey（CLICK 菜单 key / SCAN 场景值 / VIEW url——未富化，XML 提取）
    double? Latitude,                   // Latitude（location 消息/事件纬度）
    double? Longitude,                  // Longitude（location 消息/事件经度）
    double? Precision,                  // Precision（location 事件精度）
    string? Scale,                      // Scale（location 消息缩放比例——微信 XML 为文本数字）
    string? Label,                      // Label（location 消息地点名称）
    string? Content,                    // content（文本消息原文——通道富化）
    string? MediaId,                    // MediaId（image/voice/video/shortvideo 媒体 id）
    string? PicUrl,                     // PicUrl（image 图片链接）
    string? Format,                     // Format（voice 音频格式，如 amr）
    string? ThumbMediaId,               // ThumbMediaId（video/shortvideo 视频缩略图媒体 id）
    string? Title,                      // Title（link 消息标题）
    string? Description,                // Description（link 消息描述 / 图文 Description 源）
    string? Url,                        // Url（link 消息链接）
    string? Status,                     // Status（MASSSENDJOBFINISH 群发结果状态）
    int? TotalCount,                    // TotalCount（MASSSENDJOBFINISH 粉丝总数）
    int? FilterCount,                   // FilterCount（MASSSENDJOBFINISH 过滤后待发送数）
    int? SentCount,                     // SentCount（MASSSENDJOBFINISH 发送成功数）
    int? ErrorCount,                    // ErrorCount（MASSSENDJOBFINISH 发送失败数）
    string? OpenId,                     // external_user_id 归一（通道/编排富化——OpenId=external_user_id ?? FromUserName）
    string? UId,                        // uid（编排层只读查询富化——openid 未绑定为 null）
    string? ChannelId,                  // channel_id（装配层传入——registry 选区键）
    ChannelConfig? Channel = null,      // Channel（基类反查 registry 后富化——Channel.Extra 模板数据源；null = 查不到/未反查）
    string? MsgId = null,               // MsgId（微信消息 id——消息形态幂等键来源；__decrypted_xml 提取，事件类为 null；V0.2.0 网关幂等）
    long? CreateTime = null,            // CreateTime（微信原始时间戳 Unix 秒——事件形态幂等键来源；__decrypted_xml 提取，原样留存非 UTC 转换）
    string? RawXml = null)              // RawXml（__decrypted_xml 解密后明文 XML——网关转发原始报文透传数据源，方案 §十·二 ④；null = 未解密/缺失）
{
    /// <summary>
    /// SCAN 事件场景值（<see cref="EventKey"/> 去 <c>qrscene_</c> 前缀——方案 §3.2 OnScanAsync 强类型参数；
    /// 非 <c>qrscene_</c> 前缀场景值原样返回；<see cref="EventKey"/> 为空返回 null）。
    /// </summary>
    public string? SceneId
        => string.IsNullOrEmpty(EventKey)
            ? null
            : EventKey.StartsWith("qrscene_", StringComparison.Ordinal)
                ? EventKey["qrscene_".Length..]
                : EventKey;

    /// <summary>
    /// 解析（方案 §3.2 <c>WechatEventMessage.Parse</c>——一次性平台侧解析，业务 handler 零裸解析）。
    /// <para>① Parameters 基础键（通道已富化）：<c>msg_type</c>/<c>event</c>/<c>from_user_name</c>/
    /// <c>to_user_name</c>/<c>external_user_id</c>/<c>content</c>/<c>uid</c>/<c>channel_id</c>；
    /// ② <c>__decrypted_xml</c>（通道存解密后明文）XDocument 提取未富化字段（EventKey/经纬度/PicUrl/
    /// MediaId/Scale/Label/Title/Description/Url/群发统计）——CDATA 感知（XElement.Value 自动解包），
    /// 缺节点容错返回 null，非法 XML 抛 <see cref="InvalidOperationException"/>（对齐
    /// <see cref="WeChatEventCrypto"/> 形态）。</para>
    /// <para><b>容错</b>：<c>__decrypted_xml</c> 缺失 → 未富化字段全 null（不抛——兼容未解密场景）。</para>
    /// </summary>
    /// <param name="parameters">事件参数包（<see cref="ChannelInboundContext.Parameters"/>——装配层/编排层
    /// 构造 Dictionary 实例经 record 传入，通道/编排层原地富化）。</param>
    internal static WechatEventContext Parse(IReadOnlyDictionary<string, string?> parameters)
    {
        ArgumentNullException.ThrowIfNull(parameters);

        var msgType = GetValue(parameters, "msg_type");
        var eventValue = GetValue(parameters, "event");
        var fromUserName = GetValue(parameters, "from_user_name");
        var toUserName = GetValue(parameters, "to_user_name");
        var openId = GetValue(parameters, "external_user_id") ?? fromUserName;
        var uid = GetValue(parameters, "uid");
        var channelId = GetValue(parameters, "channel_id");
        var content = GetValue(parameters, "content");

        // __decrypted_xml（通道存解密后明文 XML——Oracle P1-4 Parse 数据源）缺失 → 未富化字段全 null（容错不抛）
        var xml = GetValue(parameters, "__decrypted_xml");

        string? eventKey = null;
        string? scale = null;
        string? label = null;
        double? latitude = null;
        double? longitude = null;
        double? precision = null;
        string? mediaId = null;
        string? picUrl = null;
        string? format = null;
        string? thumbMediaId = null;
        string? title = null;
        string? description = null;
        string? url = null;
        string? msgId = null;
        long? createTime = null;
        string? status = null;
        int? totalCount = null;
        int? filterCount = null;
        int? sentCount = null;
        int? errorCount = null;

        if (!string.IsNullOrWhiteSpace(xml))
        {
            // 非法 XML 抛 InvalidOperationException（对齐 WeChatEventCrypto 形态——已解密数据不可解析 = 上游异常）
            XDocument doc;
            try
            {
                doc = XDocument.Parse(xml);
            }
            catch (XmlException ex)
            {
                throw new InvalidOperationException("微信消息 XML 非法（__decrypted_xml）", ex);
            }

            msgId = ExtractText(doc, "MsgId");
            createTime = ExtractLong(doc, "CreateTime");   // 微信原始时间戳（Unix 秒）——V0.2.0 网关幂等键来源（原样留存）
            eventKey = ExtractText(doc, "EventKey");
            scale = ExtractText(doc, "Scale");
            label = ExtractText(doc, "Label");
            latitude = ExtractDouble(doc, "Latitude");
            longitude = ExtractDouble(doc, "Longitude");
            precision = ExtractDouble(doc, "Precision");
            mediaId = ExtractText(doc, "MediaId");
            picUrl = ExtractText(doc, "PicUrl");
            format = ExtractText(doc, "Format");
            thumbMediaId = ExtractText(doc, "ThumbMediaId");
            title = ExtractText(doc, "Title");
            description = ExtractText(doc, "Description");
            url = ExtractText(doc, "Url");
            status = ExtractText(doc, "Status");
            totalCount = ExtractInt(doc, "TotalCount");
            filterCount = ExtractInt(doc, "FilterCount");
            sentCount = ExtractInt(doc, "SentCount");
            errorCount = ExtractInt(doc, "ErrorCount");
        }

        return new WechatEventContext(
            MsgType: msgType,
            Event: eventValue,
            FromUserName: fromUserName,
            ToUserName: toUserName,
            EventKey: eventKey,
            Latitude: latitude,
            Longitude: longitude,
            Precision: precision,
            Scale: scale,
            Label: label,
            Content: content,
            MediaId: mediaId,
            PicUrl: picUrl,
            Format: format,
            ThumbMediaId: thumbMediaId,
            Title: title,
            Description: description,
            Url: url,
            Status: status,
            TotalCount: totalCount,
            FilterCount: filterCount,
            SentCount: sentCount,
            ErrorCount: errorCount,
            OpenId: openId,
            UId: uid,
            ChannelId: channelId,
            MsgId: msgId,
            CreateTime: createTime,
            RawXml: xml);
    }

    /// <summary>
    /// 从语义事件重建微信上下文（I2 语义层 §8.2 C3——<see cref="ChannelEventDispatch"/> → handler 一次性重建，
    /// 14 个 <c>OnXxxAsync</c> 签名与实现零改动）。
    /// <para>机制（双数据源）：① <c>evt.RawPayload</c>（解密后明文 XML——通道已承载）复用 <see cref="Parse"/> 复原全部微信字段
    /// （EventKey/经纬度/PicUrl/MediaId/群发统计等——XDocument 提取）；② RawPayload 缺失（测试/降级场景）→
    /// 用语义层一等字段（<c>ExternalUserId</c>/<c>EventType</c>/<c>Attributes["msg_type"]</c> 等）组装基础上下文，
    /// 未富化字段 null（不猜字段）。语义层字段防御性覆盖——防 RawPayload 缺节时 XML 提取值为 null。</para>
    /// <para><b>fail-closed 边界</b>：RawPayload 缺失 <b>且</b> Attributes 无 <c>msg_type</c>（无法区分事件/消息形态）→
    /// 返回 null（编排跳过 handler，不猜字段——方案 §8.2 第 3 条）。</para>
    /// </summary>
    /// <param name="evt">语义事件（<c>ChannelEventDispatch.Event</c>）。</param>
    /// <param name="uid">编排层已解析 UId（<c>ChannelEventDispatch.UId</c>）。</param>
    internal static WechatEventContext? FromChannelEvent(ChannelEvent evt, string? uid)
    {
        ArgumentNullException.ThrowIfNull(evt);

        var msgType = evt.Attributes is not null && evt.Attributes.TryGetValue("msg_type", out var mt) ? mt : null;
        var hasRawPayload = !string.IsNullOrWhiteSpace(evt.RawPayload);

        if (!hasRawPayload && string.IsNullOrWhiteSpace(msgType))
            return null;   // RawPayload 缺失且无 msg_type——无法判断事件/消息形态（fail-closed）

        var parameters = new Dictionary<string, string?>
        {
            ["channel_id"] = evt.ChannelId,
            ["external_user_id"] = evt.ExternalUserId,
            ["msg_type"] = msgType ?? "event",
            ["event"] = evt.EventType,
            ["from_user_name"] = evt.ExternalUserId,
            ["uid"] = uid,
        };
        // RawPayload 缺席时用 Attributes 兜底（msg_type/event 已在上面一等字段；content/from_user_name/to_user_name 从 Attributes 补）
        if (evt.Attributes is not null)
        {
            foreach (var (key, value) in evt.Attributes)
            {
                if (key is not ("channel_id" or "external_user_id" or "msg_type" or "event" or "from_user_name") && value is not null)
                    parameters[key] = value;
            }
        }
        if (hasRawPayload)
            parameters["__decrypted_xml"] = evt.RawPayload;

        var ctx = Parse(parameters);
        // 语义层一等字段防御性覆盖（RawPayload 缺节容错——XML 提取值为 null 时用语义层值）
        return ctx with
        {
            ChannelId = evt.ChannelId,
            OpenId = evt.ExternalUserId,
            FromUserName = evt.ExternalUserId,
            Event = evt.EventType,
            UId = uid,
            RawXml = evt.RawPayload,
            Channel = null,   // 基类 HandleAsync 内部反查 registry 后 with 富化
        };
    }

    private static string? GetValue(IReadOnlyDictionary<string, string?> parameters, string key)
        => parameters.TryGetValue(key, out var v) ? v : null;

    private static string? ExtractText(XDocument doc, string name)
    {
        var value = doc.Root?.Element(name)?.Value;
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static double? ExtractDouble(XDocument doc, string name)
    {
        var value = ExtractText(doc, name);
        return double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
    }

    private static int? ExtractInt(XDocument doc, string name)
    {
        var value = ExtractText(doc, name);
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i) ? i : null;
    }

    private static long? ExtractLong(XDocument doc, string name)
    {
        var value = ExtractText(doc, name);
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var l) ? l : null;
    }
}