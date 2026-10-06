using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Utility.Caching;

namespace TKWF.Ext.Federation;

/// <summary>
/// token2 服务——手写 ES256 JWT 签发/验证（认证中心联邦层统一断言，独立密钥域）。
/// <para>ADR-SSO Oracle P2-5：ES256（ECDSA P-256）与既有 Authentication RS256 密钥域完全独立——
/// 算法/iss/消费方/TTL 不同（token2 = 外部 H5 应用离线验签 + JWKS；token1 = 业务系统 Bearer 中间件），
/// 不复用既有 TokenService。手写对齐 Authentication 先例（零第三方依赖，仅 BCL ECDsa）。</para>
/// <para>🔒 验签安全加固（对齐 Authentication Oracle C1）：alg 强制 ES256 / 曲线强制 P-256 /
/// exp·iat leeway / kid 白名单 / iss 校验 / Base64Url 边界。</para>
/// <para>V4.10.53（领域自治根治，ADR90）：继承 <see cref="DomainServiceBase"/>——经基类 <c>User</c> 获取用户上下文；
/// 注册改 <c>AddConstructibleService&lt;IToken2Service, Token2Service&gt;</c>。<c>[DiContractIgnore]</c>：运行时手写注册。</para>
/// </summary>
[DiContractIgnore]
internal sealed class Token2Service : DomainServiceBase, IToken2Service
{
    private const string AlgHeader = "ES256";
    private const string CurveName = "P-256";

    private readonly IOptions<FederationOptions> _options;
    private readonly ILogger<Token2Service> _logger;
    private readonly DevKeyCache<EcKeySet> _devKeys;

    // 签名密钥（懒加载——kid 轮换经 SigningKeys 遍历验证；LoadKeysCore 单一真相源 + DevKeyCache dev 缓存（DI 单例））
    private readonly Lazy<EcKeySet> _keys;

    public Token2Service(
        IDomainUser user,
        DevKeyCache<EcKeySet> devKeys,
        IOptions<FederationOptions> options,
        ILogger<Token2Service> logger) : base(user)
    {
        _options = options;
        _logger = logger;
        _devKeys = devKeys;
        _keys = new Lazy<EcKeySet>(() => LoadKeysCore(), isThreadSafe: true);
    }

    public Task<Token2IssueResult> IssueToken2Async(Token2IssueRequest request, CancellationToken ct = default)
    {
        var keys = _keys.Value;
        var now = DateTime.UtcNow;
        var exp = now.AddSeconds(_options.Value.Token2ExpirationSeconds);

        var payload = new Dictionary<string, object>
        {
            ["iss"] = _options.Value.Issuer,
            ["sub"] = request.UId,
            ["aud"] = request.TargetAppId,
            ["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
            ["exp"] = new DateTimeOffset(exp).ToUnixTimeSeconds(),
            ["jti"] = NewTokenId(),
            ["kid"] = keys.CurrentKid
        };
        if (!string.IsNullOrWhiteSpace(request.Scope))
            payload["scope"] = request.Scope!;

        var token = SignToken(payload, keys);
        return Task.FromResult(new Token2IssueResult(token, _options.Value.Token2ExpirationSeconds));
    }

    public async Task<Token2ValidationResult> ValidateToken2Async(string token, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(token)) throw new AuthenticationException("TOKEN_MALFORMED");

        var keys = _keys.Value;
        var (header, payload, signature, signingInput) = ParseToken(token);

        // 1. alg 强制 ES256（防 alg=none / HS256 伪造）
        using var headerDoc = JsonDocument.Parse(Base64UrlDecode(header));
        var alg = headerDoc.RootElement.GetProperty("alg").GetString();
        if (!string.Equals(alg, AlgHeader, StringComparison.Ordinal))
            throw new AuthenticationException("ALG_NOT_ALLOWED");

        // 2. kid 白名单校验（防 kid 注入——验证遍历 SigningKeys）
        var kid = headerDoc.RootElement.TryGetProperty("kid", out var kidEl) ? kidEl.GetString() : null;
        if (string.IsNullOrEmpty(kid) || !keys.VerifyKeys.TryGetValue(kid, out var verifyKey))
            throw new AuthenticationException("KID_UNKNOWN");

        // 3. 签名验证（ECDsa.VerifyData——ES256 = SHA256 + ECDSA P-256 曲线）
        var signatureBytes = Base64UrlDecode(signature);
        if (!verifyKey.VerifyData(
                Encoding.UTF8.GetBytes(signingInput), signatureBytes,
                HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence))
            throw new AuthenticationException("INVALID_SIGNATURE");

        // 4. exp/iat 校验（leeway 30s）
        using var payloadDoc = JsonDocument.Parse(Base64UrlDecode(payload));
        var root = payloadDoc.RootElement;
        var now = DateTime.UtcNow;
        var nowUnix = new DateTimeOffset(now).ToUnixTimeSeconds();
        var skew = _options.Value.ClockSkewSeconds;
        var iat = root.TryGetProperty("iat", out var iatEl) && iatEl.TryGetInt64(out var iatVal) ? iatVal : 0;
        var exp = root.TryGetProperty("exp", out var expEl) && expEl.TryGetInt64(out var expVal) ? expVal : 0;
        if (iat > nowUnix + skew) throw new AuthenticationException("IAT_IN_FUTURE");
        if (exp <= nowUnix - skew) throw new AuthenticationException("TOKEN_EXPIRED");

        // 5. iss 校验（SSO 实例隔离）
        if (!string.Equals(root.TryGetProperty("iss", out var issEl) ? issEl.GetString() : null,
                _options.Value.Issuer, StringComparison.Ordinal))
            throw new AuthenticationException("ISS_MISMATCH");

        var jti = root.TryGetProperty("jti", out var jtiEl) ? jtiEl.GetString() : null;
        var sub = root.TryGetProperty("sub", out var subEl) ? subEl.GetString() : null;
        var aud = root.TryGetProperty("aud", out var audEl) ? audEl.GetString() : null;
        if (string.IsNullOrEmpty(jti) || string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(aud))
            throw new AuthenticationException("TOKEN_MALFORMED");

        var claims = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
                claims[prop.Name] = prop.Value.GetString()!;
            else if (prop.Value.ValueKind == JsonValueKind.Number)
                claims[prop.Name] = prop.Value.GetRawText();
            else if (prop.Value.ValueKind == JsonValueKind.True)
                claims[prop.Name] = "true";
            else if (prop.Value.ValueKind == JsonValueKind.False)
                claims[prop.Name] = "false";
        }

        return new Token2ValidationResult(
            sub, aud, jti, DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime, claims);
    }

    public string GetJwksJson()
    {
        var keys = _keys.Value;
        var jwks = new List<object>();
        foreach (var (kid, ec) in keys.VerifyKeys)
        {
            var p = ec.ExportParameters(false);
            jwks.Add(new Dictionary<string, object>
            {
                ["kty"] = "EC",
                ["crv"] = CurveName,
                ["kid"] = kid,
                ["x"] = Base64UrlEncode(p.Q.X!),
                ["y"] = Base64UrlEncode(p.Q.Y!),
                ["alg"] = AlgHeader,
                ["use"] = "sig"
            });
        }
        return JsonSerializer.Serialize(new Dictionary<string, object> { ["keys"] = jwks });
    }

    /// <summary>启动预检（Initializer.InitializeAsync 经系统作用域解析实例后调用）——触发密钥懒加载，生产 fail-fast。</summary>
    internal void EnsureKeysLoaded() => _ = _keys.Value;

    // ── 私有实现 ──────────────────────────────────────────────────────────

    internal sealed record EcKeySet(string CurrentKid, ECDsa SigningKey, Dictionary<string, ECDsa> VerifyKeys) : IDisposable
    {
        public void Dispose()
        {
            SigningKey.Dispose();
            foreach (var k in VerifyKeys.Values) k.Dispose();
        }
    }

    /// <summary>加载签名密钥（fail-fast：生产缺密钥 → 拒绝启动；开发自动生成临时密钥 + Warning + DevKeyCache 缓存）。</summary>
    private EcKeySet LoadKeysCore()
    {
        var o = _options.Value;
        var logger = _logger;
        if (string.IsNullOrEmpty(o.Issuer))
        {
            if (o.IsProduction) throw new InvalidOperationException("FederationOptions.Issuer 未配置——生产环境禁止签发/验证 token2");
            logger.LogWarning("FederationOptions.Issuer 未配置——开发环境使用空签发者");
        }

        var signingKey = CreateEcFromPem(o.SigningKeyPath);
        var currentKid = string.IsNullOrEmpty(o.CurrentKid) ? "sso-key-1" : o.CurrentKid;

        if (signingKey == null)
        {
            if (o.IsProduction)
                throw new InvalidOperationException($"FederationOptions.SigningKeyPath 未配置——生产环境必须提供 EC 私钥 PEM（kid={currentKid}）");
            logger.LogWarning("FederationOptions.SigningKeyPath 未配置——开发环境自动生成临时 EC 密钥（重启即变，仅限开发）");
            return _devKeys.GetOrCreate(() => CreateDevKeySet(currentKid, _options.Value, _logger));
        }

        EnsureP256(signingKey);

        var verifyKeys = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
        if (o.SigningKeys.Count > 0)
        {
            foreach (var cfg in o.SigningKeys)
            {
                if (string.IsNullOrEmpty(cfg.Kid)) continue;
                var ec = CreateEcFromPem(cfg.PrivateKeyPath) ?? CreateEcFromPem(cfg.PublicKeyPath);
                if (ec == null) continue;
                EnsureP256(ec);
                verifyKeys[cfg.Kid] = ec;
            }
            if (!verifyKeys.ContainsKey(currentKid)) verifyKeys[currentKid] = signingKey;
        }
        else
        {
            verifyKeys[currentKid] = signingKey;
        }

        logger.LogInformation("SSO 签名密钥已加载：kid={CurrentKid}，验证密钥数={Count}", currentKid, verifyKeys.Count);
        return new EcKeySet(currentKid, signingKey, verifyKeys);
    }

    /// <summary>构建开发模式密钥集（工厂——DevKeyCache 仅首次执行）。</summary>
    private static EcKeySet CreateDevKeySet(string currentKid, FederationOptions o, ILogger logger)
    {
        var signingKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        EnsureP256(signingKey);

        var verifyKeys = new Dictionary<string, ECDsa>(StringComparer.Ordinal);
        if (o.SigningKeys.Count > 0)
        {
            foreach (var cfg in o.SigningKeys)
            {
                if (string.IsNullOrEmpty(cfg.Kid)) continue;
                var ec = CreateEcFromPem(cfg.PrivateKeyPath) ?? CreateEcFromPem(cfg.PublicKeyPath);
                if (ec == null) continue;
                EnsureP256(ec);
                verifyKeys[cfg.Kid] = ec;
            }
            if (!verifyKeys.ContainsKey(currentKid)) verifyKeys[currentKid] = signingKey;
        }
        else
        {
            verifyKeys[currentKid] = signingKey;
        }

        logger.LogInformation("SSO 签名密钥已加载（dev）：kid={CurrentKid}，验证密钥数={Count}", currentKid, verifyKeys.Count);
        return new EcKeySet(currentKid, signingKey, verifyKeys);
    }

    private static ECDsa? CreateEcFromPem(string? pemPath)
    {
        if (string.IsNullOrEmpty(pemPath) || !File.Exists(pemPath)) return null;
        var ec = ECDsa.Create();
        try
        {
            ec.ImportFromPem(File.ReadAllText(pemPath));
            return ec;
        }
        catch
        {
            ec.Dispose();
            return null;
        }
    }

    private static void EnsureP256(ECDsa ec)
    {
        // 曲线强制 P-256（防弱曲线/参数注入——ES256 语义）
        if (ec.ExportParameters(false).Curve.Oid.FriendlyName != CurveName
            && ec.ExportParameters(false).Curve.Oid.Value != "1.2.840.10045.3.1.7")
            throw new InvalidOperationException($"EC 密钥曲线非 P-256（当前 {ec.ExportParameters(false).Curve.Oid}）——SSO 要求 P-256");
    }

    private (string Header, string Payload, string Signature, string SigningInput) ParseToken(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) throw new AuthenticationException("TOKEN_MALFORMED");
        return (parts[0], parts[1], parts[2], parts[0] + "." + parts[1]);
    }

    private string SignToken(Dictionary<string, object> payload, EcKeySet keys)
    {
        var headerJson = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["alg"] = AlgHeader,
            ["typ"] = "JWT",
            ["kid"] = keys.CurrentKid
        });
        var payloadJson = JsonSerializer.Serialize(payload);

        var headerB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(headerJson));
        var payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payloadJson));
        var signingInput = headerB64 + "." + payloadB64;

        var signature = keys.SigningKey.SignData(
            Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, DSASignatureFormat.Rfc3279DerSequence);
        return signingInput + "." + Base64UrlEncode(signature);
    }

    private static string NewTokenId()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    /// <summary>SHA256 hex（小写）——accesscode 落库 / PKCE verifier hash 统一约定。</summary>
    public static string Sha256Hex(string input)
        => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(input)));

    /// <summary>Base64Url 编码（RFC 7515——trim padding，+/ 替换 -_）。</summary>
    public static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Base64Url 解码（补齐 padding，-_ 还原 +/；非法字符抛 FormatException）。</summary>
    public static byte[] Base64UrlDecode(string input)
    {
        var s = input.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
            case 1: throw new FormatException("Invalid Base64Url string");
        }
        return Convert.FromBase64String(s);
    }
}
