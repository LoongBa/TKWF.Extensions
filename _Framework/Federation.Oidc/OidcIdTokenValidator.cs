using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TKWF.Federation.Oidc;

namespace TKWF.Federation.Oidc;

/// <summary>
/// id_token 验签结果（验签通过后归一 <c>SsoChannelAuthResult.ExternalUserId = sub</c>）。
/// </summary>
public sealed record OidcIdTokenValidationResult(string Sub, string Issuer, string Audience, string? AuthorizedParty);

/// <summary>
/// id_token 验签器（M1 基座信任根核心——OIDC §3.1.3.7）：
/// <para>alg 强制 RS256（拒 none/HS256 算法混淆）/ kid 白名单精确匹配 + JWKS 验签 /
/// iss 白名单（支持通配/正则——Microsoft common tenant iss 含实际租户 GUID，M2-P1-1）/
/// aud 含 client_id（数组场景多方时 azp 必校验 = client_id，Oracle P1-3）/
/// exp·iat leeway 30s + nbf ≤ now+leeway（P2-2）/ sub 必存在 / Base64Url 边界 + FixedTimeEquals 验签。</para>
/// <para>本类无状态（收 byte[]/string——不引 AspNetCore，纯逻辑）。</para>
/// </summary>
public sealed class OidcIdTokenValidator
{
    private static readonly TimeSpan Leeway = TimeSpan.FromSeconds(30);

    private readonly JwksManager _jwks;

    public OidcIdTokenValidator(JwksManager jwks)
    {
        _jwks = jwks ?? throw new ArgumentNullException(nameof(jwks));
    }

    /// <summary>
    /// 验签并解析 id_token（JWT RS256）。
    /// </summary>
    /// <param name="idToken">JWT（3 段点分）。</param>
    /// <param name="jwksUri">JWKS 端点（验签密钥源）。</param>
    /// <param name="clientId">aud 校验（= client_id；数组场景 azp = client_id）。</param>
    /// <param name="issuers">TokenIssuer 白名单（支持通配/正则——Microsoft 租户 GUID）。</param>
    public async Task<OidcIdTokenValidationResult> ValidateAsync(string idToken, string jwksUri, string clientId, IReadOnlyList<string> issuers, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(idToken))
            throw new ArgumentException("id_token 为空");

        var segments = idToken.Split('.');
        if (segments.Length != 3)
            throw new InvalidOperationException("id_token 非三段 JWT（格式非法）");

        var headerJson = DecodeSegment(segments[0]);
        using var headerDoc = JsonDocument.Parse(headerJson);

        // ① alg 强制 RS256（拒 none/HS256——算法混淆防护）
        var alg = headerDoc.RootElement.TryGetProperty("alg", out var a) ? a.GetString() : null;
        if (!string.Equals(alg, "RS256", StringComparison.Ordinal))
            throw new InvalidOperationException($"id_token alg 非 RS256（实际 {alg ?? "null"}——算法混淆拒）");

        var kid = headerDoc.RootElement.TryGetProperty("kid", out var k) ? k.GetString() : null;
        if (string.IsNullOrWhiteSpace(kid))
            throw new InvalidOperationException("id_token 缺 kid（无法选区 JWKS 密钥）");

        // ② 签名验签（kid 精确匹配 + JWKS 重取——M1-P1-7；FixedTimeEquals 底层由 RSA.VerifyData 保证）
        var rsa = await _jwks.GetSigningKeyAsync(jwksUri, kid, ct);
        var signedData = Encoding.UTF8.GetBytes($"{segments[0]}.{segments[1]}");
        var signatureRaw = Base64UrlDecode(segments[2]);
        try
        {
            if (!rsa.VerifyData(signedData, signatureRaw, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1))
                throw new InvalidOperationException("id_token 签名验签失败（篡改/密钥不匹配）");
        }
        finally
        {
            // JwksManager 缓存持有 RSA 实例——不 dispose（缓存复用于后续验签）
        }

        // ③ payload 校验
        var payloadJson = DecodeSegment(segments[1]);
        using var payloadDoc = JsonDocument.Parse(payloadJson);
        var root = payloadDoc.RootElement;

        var sub = root.TryGetProperty("sub", out var s) ? s.GetString() : null;
        if (string.IsNullOrWhiteSpace(sub))
            throw new InvalidOperationException("id_token 缺 sub（身份主体标识必存在——P12 不透明字符串不解析）");

        // iss 白名单（支持通配/正则——Microsoft common tenant iss 含实际租户 GUID）
        var iss = root.TryGetProperty("iss", out var i) ? i.GetString() : null;
        if (issuers.Count > 0)
        {
            if (string.IsNullOrEmpty(iss) || !IsIssuerAllowed(iss, issuers))
                throw new InvalidOperationException($"id_token iss 不在白名单：{iss ?? "null"}（配置 {issuers.Count} 项）");
        }

        // aud：字符串 = client_id 直接等值；数组 = 必含 client_id，且多方时 azp = client_id
        var aud = root.TryGetProperty("aud", out var audEl) ? audEl : default;
        if (aud.ValueKind == JsonValueKind.String)
        {
            if (!string.Equals(aud.GetString(), clientId, StringComparison.Ordinal))
                throw new InvalidOperationException($"id_token aud 不匹配 client_id：{aud.GetString()} != {clientId}");
        }
        else if (aud.ValueKind == JsonValueKind.Array)
        {
            var auds = aud.EnumerateArray().Select(x => x.GetString()).Where(x => x != null).Cast<string>().ToList();
            if (!auds.Contains(clientId, StringComparer.Ordinal))
                throw new InvalidOperationException($"id_token aud 数组不含 client_id：{string.Join(",", auds)}");
            if (auds.Count > 1)
            {
                var azp = root.TryGetProperty("azp", out var z) ? z.GetString() : null;
                if (!string.Equals(azp, clientId, StringComparison.Ordinal))
                    throw new InvalidOperationException($"id_token 多受众（aud {auds.Count} 项）且 azp 不匹配 client_id：{azp ?? "null"} != {clientId}");
            }
        }
        else
        {
            throw new InvalidOperationException("id_token aud 格式非法（非字符串非数组）");
        }

        // exp/iat leeway 30s
        var now = DateTimeOffset.UtcNow;
        var exp = GetNumericClaim(root, "exp");
        if (exp.HasValue && now > exp.Value.Add(Leeway))
            throw new InvalidOperationException("id_token 已过期");

        var iat = GetNumericClaim(root, "iat");
        if (iat.HasValue && iat.Value > now.Add(Leeway))
            throw new InvalidOperationException("id_token iat 在未来（时钟偏差越界）");

        // nbf ≤ now + leeway（P2-2）
        var nbf = GetNumericClaim(root, "nbf");
        if (nbf.HasValue && now.Add(Leeway) < nbf.Value)
            throw new InvalidOperationException("id_token 未生效（nbf 在未来）");

        return new OidcIdTokenValidationResult(
            sub,
            iss ?? "",
            aud.ValueKind == JsonValueKind.String ? aud.GetString()! : string.Join(",", aud.EnumerateArray().Select(x => x.GetString())),
            root.TryGetProperty("azp", out var azpEl) ? azpEl.GetString() : null);
    }

    static string DecodeSegment(string segment)
    {
        try
        {
            return Encoding.UTF8.GetString(Base64UrlDecode(segment));
        }
        catch (FormatException ex)
        {
            throw new InvalidOperationException("id_token 段 base64url 解码失败（边界校验）", ex);
        }
    }

    internal static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }

    static DateTimeOffset? GetNumericClaim(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var el)) return null;
        if (el.ValueKind == JsonValueKind.Number && el.TryGetInt64(out var seconds))
            return DateTimeOffset.FromUnixTimeSeconds(seconds);
        if (el.ValueKind == JsonValueKind.String && double.TryParse(el.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return DateTimeOffset.FromUnixTimeSeconds((long)d);
        return null;
    }

    /// <summary>iss 白名单匹配——支持精确等值 / 末尾 `*` 通配 / 正则（^...$ 整体匹配）。</summary>
    internal static bool IsIssuerAllowed(string iss, IReadOnlyList<string> patterns)
    {
        foreach (var p in patterns)
        {
            if (string.IsNullOrEmpty(p)) continue;

            // 正则形态（以 ^ 开头视为正则——Microsoft `https://login.microsoftonline.com/.+/v2.0`）
            if (p.StartsWith('^') || p.EndsWith('$'))
            {
                try
                {
                    if (System.Text.RegularExpressions.Regex.IsMatch(iss, p))
                        return true;
                }
                catch (ArgumentException) { /* 非法正则——跳过 */ }
                continue;
            }

            // 末尾通配（如 abc.evil.com* 或 *.example.com）
            if (p.EndsWith('*'))
            {
                var prefix = p[..^1];
                if (iss.StartsWith(prefix, StringComparison.Ordinal))
                    return true;
                continue;
            }

            if (string.Equals(iss, p, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}