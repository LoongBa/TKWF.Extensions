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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace TKWF.Ext.Authentication;

/// <summary>
/// 令牌服务——手写 JWT（RS256）签发/验证/刷新/撤销（业界成熟重写，方案 §5.4）。
/// <para>摒弃 DMP 三缺陷：内存 Lazy&lt;RSA&gt; 密钥（重启丢失/多实例不一致）→ 持久化 PEM 密钥 + kid 轮换；
/// 内存 ConcurrentDictionary 黑名单（重启丢失）→ AuthTokenBlacklistEntity 落库 + IMemoryCache 短 TTL 前置过滤（Oracle C3）；
/// 直注入 IEntityDAC&lt;AuthRefreshToken&gt;（红线违规）→ 委托 AuthRefreshTokenEntityDataService。</para>
/// <para>🔒 验签安全加固清单（Oracle C1）：alg 强制 RS256 / FixedTimeEquals / RSA≥2048 / exp·iat / kid 白名单 / iss 校验 / Base64Url 边界。</para>
/// <para>internal sealed（对齐 Identity UserStore/UserManager 先例——DataService 为 internal，public 构造器会 CS0051）。</para>
/// </summary>
internal sealed class TokenService : ITokenService
{
    private const string AlgHeader = "RS256";
    private const int RsaMinKeyBits = 2048;
    private const int BlacklistCacheSeconds = 45;

    private readonly IOptions<AuthCenterOptions> _options;
    private readonly AuthAccountEntityDataService _accountDataService;
    private readonly AuthRefreshTokenEntityDataService _refreshTokenDataService;
    private readonly AuthTokenBlacklistEntityDataService _blacklistDataService;
    private readonly IMemoryCache _cache;
    private readonly ILogger<TokenService> _logger;

    // 签名密钥（懒加载——启动首个调用时加载，kid 轮换经 SigningKeys 遍历验证）
    private readonly Lazy<RsaKeySet> _keys;

    public TokenService(
        IOptions<AuthCenterOptions> options,
        AuthAccountEntityDataService accountDataService,
        AuthRefreshTokenEntityDataService refreshTokenDataService,
        AuthTokenBlacklistEntityDataService blacklistDataService,
        IMemoryCache cache,
        ILogger<TokenService> logger)
    {
        _options = options;
        _accountDataService = accountDataService;
        _refreshTokenDataService = refreshTokenDataService;
        _blacklistDataService = blacklistDataService;
        _cache = cache;
        _logger = logger;
        _keys = new Lazy<RsaKeySet>(LoadKeys, isThreadSafe: true);
    }

    public async Task<TokenIssueResult> IssueTokenAsync(TokenIssueRequest request, CancellationToken ct = default)
    {
        var keys = _keys.Value;
        var now = DateTime.UtcNow;
        var jti = NewTokenId();
        var exp = now.AddMinutes(_options.Value.AccessTokenExpirationMinutes);

        var payload = new Dictionary<string, object>
        {
            ["iss"] = _options.Value.Issuer,
            ["sub"] = $"user:{request.UserId}",
            ["userId"] = request.UserId,
            ["authType"] = request.AuthType,
            ["auth_level"] = request.AuthLevel,
            ["teacher_verified"] = request.TeacherVerified,
            ["exp"] = new DateTimeOffset(exp).ToUnixTimeSeconds(),
            ["iat"] = new DateTimeOffset(now).ToUnixTimeSeconds(),
            ["jti"] = jti,
            ["kid"] = keys.CurrentKid
        };

        var accessToken = SignToken(payload, keys);

        // Refresh rotation：SHA256 落库（不存明文）+ TokenVersion 闭环（账号当前版本）
        var account = await _accountDataService.GetByUIdAsync(request.UserId, ct);
        var refreshToken = NewTokenId();
        await _refreshTokenDataService.CreateAsync(new AuthRefreshTokenEntity
        {
            Jti = jti,
            UserId = request.UserId,
            AuthType = request.AuthType,
            TokenHash = Sha256Hex(refreshToken),
            TokenVersion = account?.TokenVersion ?? 0,
            ExpiresAt = now.AddDays(_options.Value.RefreshTokenExpirationDays),
            DeviceInfo = request.DeviceInfo,
            CreateTime = now
        }, ct);

        return new TokenIssueResult(accessToken, refreshToken, _options.Value.AccessTokenExpirationMinutes * 60);
    }

    public async Task<TokenValidationResult> ValidateTokenAsync(string accessToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(accessToken)) throw new AuthenticationException("TOKEN_MALFORMED");

        var keys = _keys.Value;
        var (header, payload, signature, signingInput) = ParseToken(accessToken);

        // C1-a：alg 强制 RS256（防 alg=none / HS256 伪造）——header 为 Base64Url，先解码
        using var headerDoc = JsonDocument.Parse(Base64UrlDecode(header));
        var alg = headerDoc.RootElement.GetProperty("alg").GetString();
        if (!string.Equals(alg, AlgHeader, StringComparison.Ordinal))
            throw new AuthenticationException("ALG_NOT_ALLOWED");

        // C1-e：kid 白名单校验（防 kid 注入——验证遍历 SigningKeys）
        var kid = headerDoc.RootElement.TryGetProperty("kid", out var kidEl) ? kidEl.GetString() : null;
        if (string.IsNullOrEmpty(kid) || !keys.VerifyKeys.TryGetValue(kid, out var verifyKey))
            throw new AuthenticationException("KID_UNKNOWN");

        // C1-b：签名比对 FixedTimeEquals（防时序攻击）+ C1-g：签名范围 base64url(header).base64url(payload)
        var signatureBytes = Base64UrlDecode(signature);
        var expectedSignature = verifyKey.VerifyData(
            Encoding.UTF8.GetBytes(signingInput), signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        if (!expectedSignature)
            throw new AuthenticationException("INVALID_SIGNATURE");

        // C1-d：exp/iat 校验（iat 不得未来；exp 不得过去）——payload 为 Base64Url，先解码
        using var payloadDoc = JsonDocument.Parse(Base64UrlDecode(payload));
        var root = payloadDoc.RootElement;
        var now = DateTime.UtcNow;
        var nowUnix = new DateTimeOffset(now).ToUnixTimeSeconds();
        var iat = root.TryGetProperty("iat", out var iatEl) && iatEl.TryGetInt64(out var iatVal) ? iatVal : 0;
        var exp = root.TryGetProperty("exp", out var expEl) && expEl.TryGetInt64(out var expVal) ? expVal : 0;
        if (iat > nowUnix + 60) throw new AuthenticationException("IAT_IN_FUTURE");
        if (exp <= nowUnix) throw new AuthenticationException("TOKEN_EXPIRED");

        // C1-f：iss 校验（多 auth 实例隔离——Oracle 8.4）
        if (!string.Equals(root.TryGetProperty("iss", out var issEl) ? issEl.GetString() : null,
                _options.Value.Issuer, StringComparison.Ordinal))
            throw new AuthenticationException("ISS_MISMATCH");

        var jti = root.TryGetProperty("jti", out var jtiEl) ? jtiEl.GetString() : null;
        var userId = root.TryGetProperty("userId", out var uidEl) ? uidEl.GetString() : null;
        if (string.IsNullOrEmpty(jti) || string.IsNullOrEmpty(userId))
            throw new AuthenticationException("TOKEN_MALFORMED");

        // Oracle C3：黑名单 IMemoryCache 短 TTL 前置过滤（避免每请求 1 次 DB lookup）
        var cacheKey = "auth:blacklist:" + jti;
        if (_cache.TryGetValue(cacheKey, out _))
            throw new AuthenticationException("TOKEN_REVOKED");
        var blacklisted = await _blacklistDataService.GetByJtiAsync(jti, ct);
        if (blacklisted != null)
        {
            _cache.Set(cacheKey, true, TimeSpan.FromSeconds(BlacklistCacheSeconds));
            throw new AuthenticationException("TOKEN_REVOKED");
        }

        var authType = root.TryGetProperty("authType", out var atEl) ? atEl.GetString() : AuthTypes.Sms;
        var authLevel = root.TryGetProperty("auth_level", out var alEl) && alEl.TryGetInt32(out var alVal) ? alVal : (int)AuthLevel.Phone;
        var teacherVerified = root.TryGetProperty("teacher_verified", out var tvEl) && tvEl.ValueKind == JsonValueKind.True;

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

        return new TokenValidationResult(
            userId, authType, authLevel, teacherVerified, jti,
            DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime, claims);
    }

    public async Task<TokenRefreshResult> RefreshTokenAsync(string refreshToken, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new AuthenticationException("REFRESH_NOT_FOUND");

        var row = await _refreshTokenDataService.GetByTokenHashAsync(Sha256Hex(refreshToken), ct);
        if (row == null) throw new AuthenticationException("REFRESH_NOT_FOUND");

        // 重用检测（业界 BCP）：已撤销 TokenHash 再次出现 → 判定泄露 → 撤销该用户全部 refresh + Warning
        if (row.IsRevoked)
        {
            _logger.LogWarning("Refresh token reuse detected for user {UserId} — revoking all refresh tokens", row.UserId);
            await _refreshTokenDataService.RevokeAllByUserIdAsync(row.UserId, ct);
            throw new AuthenticationException("REFRESH_REUSED");
        }

        if (row.ExpiresAt <= DateTime.UtcNow) throw new AuthenticationException("REFRESH_EXPIRED");

        var account = await _accountDataService.GetByUIdAsync(row.UserId, ct);
        if (account == null || !account.IsEnabled) throw new AuthenticationException("ACCOUNT_NOT_FOUND");

        // TokenVersion 闭环：刷新时校验（不匹配 → 拒绝——密码/绑定变更后旧 refresh 失效）
        if (row.TokenVersion != account.TokenVersion) throw new AuthenticationException("REFRESH_STALE");

        await _refreshTokenDataService.MarkRevokedAsync(row.Id, DateTime.UtcNow, ct);

        var result = await IssueTokenAsync(new TokenIssueRequest(
            row.UserId, row.AuthType, account.AuthLevel, account.TeacherVerified, row.DeviceInfo), ct);

        return new TokenRefreshResult(result.AccessToken, result.RefreshToken, result.ExpiresIn);
    }

    public async Task RevokeTokenAsync(string jti, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jti)) return;

        var cacheKey = "auth:blacklist:" + jti;
        var existing = await _blacklistDataService.GetByJtiAsync(jti, ct);
        if (existing != null) return; // 幂等

        // ExpiresAt = 该 token 自然过期（近似：剩余生命 ≤ 全生命周期——调用方通常先 ValidateToken 后撤销）
        var now = DateTime.UtcNow;
        await _blacklistDataService.CreateAsync(new AuthTokenBlacklistEntity
        {
            Jti = jti,
            UserId = "",
            ExpiresAt = now.AddMinutes(_options.Value.AccessTokenExpirationMinutes),
            RevokedAt = now,
            Reason = reason
        }, ct);
        _cache.Set(cacheKey, true, TimeSpan.FromSeconds(BlacklistCacheSeconds));
    }

    /// <summary>启动预检（Initializer.InitializeAsync 调用）——触发密钥懒加载，生产 fail-fast（缺 Issuer/密钥 → InvalidOperationException）。</summary>
    internal void EnsureKeysLoaded() => _ = _keys.Value;

    // ── 私有实现 ──────────────────────────────────────────────────────────

    private sealed record RsaKeySet(string CurrentKid, RSA SigningKey, Dictionary<string, RSA> VerifyKeys);

    /// <summary>
    /// 加载签名密钥（fail-fast：生产缺密钥/默认值 → 拒绝启动；开发自动生成临时密钥 + Warning）。
    /// <para>kid 轮换（JWK RFC 7517 语义）：CurrentKid 指定签发；验证遍历 SigningKeys 全量 kid 匹配。</para>
    /// </summary>
    private RsaKeySet LoadKeys()
    {
        var o = _options.Value;
        if (string.IsNullOrEmpty(o.Issuer))
        {
            if (o.IsProduction) throw new InvalidOperationException("AuthCenterOptions.Issuer 未配置——生产环境禁止签发/验证令牌");
            _logger.LogWarning("AuthCenterOptions.Issuer 未配置——开发环境使用空签发者");
        }

        var signingKey = CreateRsaFromPem(o.SigningKeyPath);
        var currentKid = string.IsNullOrEmpty(o.CurrentKid) ? "rsa-key-1" : o.CurrentKid;

        if (signingKey == null)
        {
            // 生产 fail-fast；开发自动生成临时密钥（不落盘——重启即变，仅开发便利）
            if (o.IsProduction)
                throw new InvalidOperationException($"AuthCenterOptions.SigningKeyPath 未配置——生产环境必须提供 RSA 私钥 PEM（kid={currentKid}）");
            _logger.LogWarning("AuthCenterOptions.SigningKeyPath 未配置——开发环境自动生成临时 RSA 密钥（重启即变，仅限开发）");
            signingKey = RSA.Create(RsaMinKeyBits);
        }

        EnsureKeyLength(signingKey);

        var verifyKeys = new Dictionary<string, RSA>(StringComparer.Ordinal);
        if (o.SigningKeys.Count > 0)
        {
            foreach (var cfg in o.SigningKeys)
            {
                if (string.IsNullOrEmpty(cfg.Kid)) continue;
                var rsa = CreateRsaFromPem(cfg.PrivateKeyPath) ?? CreateRsaFromPem(cfg.PublicKeyPath);
                if (rsa == null) continue;
                EnsureKeyLength(rsa);
                verifyKeys[cfg.Kid] = rsa;
            }
            // CurrentKid 未列入 SigningKeys 时补充（签发密钥即验证密钥）
            if (!verifyKeys.ContainsKey(currentKid)) verifyKeys[currentKid] = signingKey;
        }
        else
        {
            verifyKeys[currentKid] = signingKey;
        }

        _logger.LogInformation("认证中心签名密钥已加载：kid={CurrentKid}，验证密钥数={Count}", currentKid, verifyKeys.Count);
        return new RsaKeySet(currentKid, signingKey, verifyKeys);
    }

    private static RSA? CreateRsaFromPem(string? pemPath)
    {
        if (string.IsNullOrEmpty(pemPath) || !File.Exists(pemPath)) return null;
        var rsa = RSA.Create();
        try
        {
            rsa.ImportFromPem(File.ReadAllText(pemPath));
            return rsa;
        }
        catch
        {
            rsa.Dispose();
            return null;
        }
    }

    private static void EnsureKeyLength(RSA rsa)
    {
        // C1-c：RSA 密钥长度校验 ≥ 2048-bit（启动 fail-fast）
        if (rsa.KeySize < RsaMinKeyBits)
            throw new InvalidOperationException($"RSA 密钥长度 {rsa.KeySize} < {RsaMinKeyBits}——认证中心要求 ≥2048-bit");
    }

    private (string Header, string Payload, string Signature, string SigningInput) ParseToken(string token)
    {
        var parts = token.Split('.');
        if (parts.Length != 3) throw new AuthenticationException("TOKEN_MALFORMED");
        return (parts[0], parts[1], parts[2], parts[0] + "." + parts[1]);
    }

    private string SignToken(Dictionary<string, object> payload, RsaKeySet keys)
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
            Encoding.UTF8.GetBytes(signingInput), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        return signingInput + "." + Base64UrlEncode(signature);
    }

    private static string NewTokenId()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Base64UrlEncode(bytes);
    }

    /// <summary>SHA256 hex（小写）——Refresh Token 落库 / PKCE verifier hash / 验证码散列统一约定。</summary>
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
