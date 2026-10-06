using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace TKWF.Federation.Oidc;

/// <summary>
/// JWKS 管理（M1 基座——RFC 7517）：按 `kid` 精确匹配取 RSA 公钥（RFC 7515 §4.1.4——禁取第一把），
/// L1 缓存 + SemaphoreSlim 并发锁 + 过期刷新。
/// <para>kid 匹配失败 → 强制重取 JWKS 再试一次（平台轮换密钥时恢复，不瞬间失败——M1-P1-7）；
/// 重取后仍不匹配 → 抛（信任根负路径——无 JWKS 无验签 = 无信任根）。</para>
/// <para>HttpClient 经 typed client 注入（<c>AddHttpClient&lt;OidcAuthFlow&gt;</c> 共用——DI 托管生命周期）。</para>
/// </summary>
public sealed class JwksManager
{
    private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(1);
    private const int MaxJwksSize = 512 * 1024;

    private readonly HttpClient _httpClient;
    private readonly ILogger<JwksManager> _logger;

    // L1 缓存：jwkSetUrl → (kid→RSA 公钥, fetchedAtUtc)
    private readonly ConcurrentDictionary<string, JwksCacheEntry> _cache = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _refreshLock = new(1, 1);

    public JwksManager(HttpClient httpClient, ILogger<JwksManager> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// 按 <see cref="RSA"/> 公钥换 <c>kid</c>（精确匹配）。
    /// <para>kid 匹配失败自动重取 JWKS 一次（M1-P1-7——平台轮换密钥场景），仍不匹配抛
    /// <see cref="InvalidOperationException"/>。</para>
    /// </summary>
    public async Task<RSA> GetSigningKeyAsync(string jwksUri, string kid, CancellationToken ct = default)
    {
        var entry = await GetOrFetchAsync(jwksUri, ct);
        if (entry.KeyByKid.TryGetValue(kid, out var key))
            return key;

        // kid 不匹配：强制重取一轮（密钥轮换窗口期），仍不匹配拒绝——信任根负路径
        _logger.LogWarning("JWKS kid 不匹配（{Kid}）——强制重取 {JwksUri} 再试一次", kid, jwksUri);
        await RefreshAsync(jwksUri, ct);
        entry = _cache[jwksUri];
        if (!entry.KeyByKid.TryGetValue(kid, out key))
            throw new InvalidOperationException($"JWKS kid 不匹配且重取后仍无：kid={kid}, jwks={jwksUri}");

        return key;
    }

    private async Task<JwksCacheEntry> GetOrFetchAsync(string jwksUri, CancellationToken ct)
    {
        if (_cache.TryGetValue(jwksUri, out var entry) && DateTime.UtcNow - entry.FetchedAtUtc < CacheTtl)
            return entry;

        await _refreshLock.WaitAsync(ct);
        try
        {
            // 双检（锁内复查缓存）
            if (_cache.TryGetValue(jwksUri, out entry) && DateTime.UtcNow - entry.FetchedAtUtc < CacheTtl)
                return entry;

            return await RefreshAsync(jwksUri, ct);
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private async Task<JwksCacheEntry> RefreshAsync(string jwksUri, CancellationToken ct)
    {
        using var resp = await _httpClient.GetAsync(jwksUri, HttpCompletionOption.ResponseHeadersRead, ct);
        resp.EnsureSuccessStatusCode();

        // 响应大小钳制（防恶意超大 JWKS）
        await using var stream = await resp.Content.ReadAsStreamAsync(ct);
        if (stream.CanSeek && stream.Length > MaxJwksSize)
            throw new InvalidOperationException($"JWKS 响应超限：{stream.Length} bytes（> {MaxJwksSize}）");

        string json;
        using (var reader = new StreamReader(stream))
        {
            // ReadToEndAsync 无大小钳制——用限量读取
            var buffer = new char[MaxJwksSize + 1];
            var read = await reader.ReadAsync(buffer.AsMemory(0, MaxJwksSize + 1), ct);
            if (read > MaxJwksSize)
                throw new InvalidOperationException($"JWKS 响应超限：> {MaxJwksSize}");
            json = new string(buffer, 0, read);
        }

        var keys = new Dictionary<string, RSA>(StringComparer.Ordinal);
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("keys", out var keysEl) || keysEl.ValueKind != JsonValueKind.Array)
            throw new InvalidOperationException("JWKS 响应缺 keys 数组");

        foreach (var keyEl in keysEl.EnumerateArray())
        {
            var kid = keyEl.TryGetProperty("kid", out var k) ? k.GetString() : null;
            if (string.IsNullOrWhiteSpace(kid)) continue;

            // 只收 RSA（RS256/RS384/RS512——OIDC 主流）；kty=RSA + n/e 二进（base64url）
            var kty = keyEl.TryGetProperty("kty", out var t) ? t.GetString() : null;
            if (!string.Equals(kty, "RSA", StringComparison.Ordinal)) continue;

            var n = keyEl.TryGetProperty("n", out var nEl) ? nEl.GetString() : null;
            var e = keyEl.TryGetProperty("e", out var eEl) ? eEl.GetString() : null;
            if (string.IsNullOrEmpty(n) || string.IsNullOrEmpty(e)) continue;

            try
            {
                var rsa = RSA.Create();
                rsa.ImportParameters(new RSAParameters
                {
                    Modulus = Base64UrlDecode(n),
                    Exponent = Base64UrlDecode(e)
                });
                // 防注入：拒绝过弱密钥（RSA < 2048 拒——对齐 Authentication 验签加固既有语义）
                if (rsa.KeySize < 2048)
                {
                    rsa.Dispose();
                    continue;
                }
                keys[kid] = rsa;
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                _logger.LogWarning("JWKS 密钥解析失败（kid={Kid}）：{Msg}", kid, ex.Message);
            }
        }

        if (keys.Count == 0)
            throw new InvalidOperationException("JWKS 无可验签密钥（RSA ≥2048 过滤后为空）");

        var entry = new JwksCacheEntry(keys, DateTime.UtcNow);
        _cache[jwksUri] = entry;
        return entry;
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

    private sealed record JwksCacheEntry(IReadOnlyDictionary<string, RSA> KeyByKid, DateTime FetchedAtUtc);
}