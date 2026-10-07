using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using TKW.Framework.Utility.Cryptography;

namespace TKWF.Federation.Alipay;

/// <summary>
/// 支付宝 RSA2 双向签名/验签服务（N4 T2 核心——纯逻辑类，不 IDomainService）。
/// <para>双向签名（N4 §3.2 信任根完备——强双向签名）：① <see cref="BuildRequestSignature"/>——商户私钥签出站
/// 请求（gateway.do 公共参数 + 方法特定顶层参数，委托框架 <see cref="AlipaySignUtil.BuildSignature"/>——已含
/// 剔 sign/空值 + key 首字符 ASCII 升序 + <c>key=value&amp;</c> 拼接 + SHA256WithRSA）；② <see cref="VerifyResponse"/>——
/// 支付宝公钥验网关同步响应（自实现——保留原始 body 防 JSON 字段顺序/空格差异，librarian §5）；③
/// <see cref="VerifyNotification"/>——支付宝公钥验异步通知（dict 扁平形态 = rsaCheckV1 等价，委托
/// <see cref="AlipaySignUtil.VerifyCallbackSignature"/>；本库暂无入站回调，保留供未来）。</para>
/// <para>RSA 密钥强度守卫（RSA ≥ 2048）由框架 <see cref="RsaUtil"/> 承担（fail-fast 抛
/// <see cref="CryptographicException"/>——NIST SP 800-131A 112-bit 下限）。</para>
/// </summary>
public sealed class AlipaySignService
{
    /// <summary>
    /// 出站请求签名（RSA2——参数已含全部公共 + 业务顶层参数，含 biz_content 顶层；方案 P2-1）。
    /// <para>委托 <see cref="AlipaySignUtil.BuildSignature"/>：剔 sign/空值 → key 首字符 ASCII 升序 →
    /// <c>key=value&amp;</c> 拼接 → SHA256WithRSA(商户私钥) → Base64。</para>
    /// </summary>
    /// <param name="parameters">签名参数（不含 sign——方法内部剔除）；值须为原始未 URL Encode 形态（传输层编码）。</param>
    /// <param name="privateKeyPem">商户私钥 PEM（PKCS8，RSA ≥ 2048）。</param>
    public string BuildRequestSignature(IReadOnlyDictionary<string, string> parameters, string privateKeyPem)
    {
        if (parameters == null)
            throw new ArgumentNullException(nameof(parameters));
        if (string.IsNullOrWhiteSpace(privateKeyPem))
            throw new ArgumentException("商户私钥 PEM 必填（支付宝 RSA2 请求签名）", nameof(privateKeyPem));

        var dict = parameters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        return AlipaySignUtil.BuildSignature(dict, privateKeyPem);
    }

    /// <summary>
    /// 网关同步响应验签（自实现——librarian §5 关键裁定：<see cref="AlipaySignUtil.VerifyCallbackSignature"/>
    /// 接收 dict 扁平参数，而 gateway.do 同步响应是 JSON <c>{"alipay_xxx_response":{...},"sign":"..."}</c>——
    /// 若解析成 dict 再回序列化内层对象，JSON 字段顺序/空格/引号差异导致验签失败）。
    /// <para>算法：保留原始 <paramref name="rawBody"/>，用 <see cref="JsonDocument"/> 解析顶层
    /// （<see cref="JsonElement.GetRawText"/> 保留内层原始子串），剥离顶层 "sign" 键后，其余顶层字段
    /// （值 = 原始 JSON 子串）按 key ASCII 升序拼接 <c>key=value&amp;</c>，再用
    /// <see cref="RsaUtil.VerifyDataSha256"/>（支付宝公钥）验签。</para>
    /// <para>空 body / 格式非法 / 密钥非法 → <c>false</c>（不抛 500——负路径安全默认拒绝）。</para>
    /// </summary>
    public bool VerifyResponse(string rawBody, string sign, string alipayPublicKeyPem)
    {
        if (string.IsNullOrWhiteSpace(rawBody) || string.IsNullOrWhiteSpace(sign) || string.IsNullOrWhiteSpace(alipayPublicKeyPem))
            return false;

        try
        {
            using var doc = JsonDocument.Parse(rawBody);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                return false;

            // 剥离顶层 "sign" 键；其余顶层字段值用 GetRawText 保留原始 JSON 子串（防回序列化差异）
            var entries = new List<KeyValuePair<string, string>>();
            foreach (var prop in root.EnumerateObject())
            {
                if (string.Equals(prop.Name, "sign", StringComparison.Ordinal))
                    continue;
                var value = prop.Value.ValueKind == JsonValueKind.String
                    ? prop.Value.GetString() ?? ""
                    : prop.Value.GetRawText();
                entries.Add(new KeyValuePair<string, string>(prop.Name, value));
            }

            // 按 key ASCII 升序拼接 key=value&（对齐支付宝签名语义——不含 URL Encode）
            entries.Sort(static (a, b) => string.CompareOrdinal(a.Key, b.Key));
            var sb = new StringBuilder();
            foreach (var e in entries)
            {
                if (sb.Length > 0) sb.Append('&');
                sb.Append(e.Key).Append('=').Append(e.Value);
            }

            return RsaUtil.VerifyDataSha256(sb.ToString(), sign, alipayPublicKeyPem);
        }
        catch (Exception ex) when (ex is JsonException or FormatException or CryptographicException)
        {
            // 空/格式非法/签名 Base64 非法/密钥强度不足 → 拒绝（不抛 500）
            return false;
        }
    }

    /// <summary>
    /// 异步通知验签（dict 扁平形态 = rsaCheckV1 等价——仅适用于异步通知；委托
    /// <see cref="AlipaySignUtil.VerifyCallbackSignature"/>）。本库暂无入站回调（出站-only 形态），保留供未来。
    /// </summary>
    /// <param name="parameters">通知参数（含 sign——方法内部剔除）。</param>
    /// <param name="alipayPublicKeyPem">支付宝公钥 PEM。</param>
    public bool VerifyNotification(IReadOnlyDictionary<string, string> parameters, string alipayPublicKeyPem)
    {
        if (parameters == null)
            throw new ArgumentNullException(nameof(parameters));
        if (string.IsNullOrWhiteSpace(alipayPublicKeyPem))
            return false;

        var dict = parameters.ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal);
        return AlipaySignUtil.VerifyCallbackSignature(dict, alipayPublicKeyPem);
    }
}
