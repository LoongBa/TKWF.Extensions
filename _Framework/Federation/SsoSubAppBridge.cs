using System;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 子应用消费方桥接门面实现（子应用消费方接入 2026-10-11——仅共享 Federation 模式核心编排）。
/// <para>对齐 <see cref="SsoLogin"/> 门面范式：继承 <see cref="DomainServiceBase"/>（经基类 <c>User</c> 取上下文）、
/// 内部经 <c>User.Use&lt;契约&gt;()</c> 懒加载（DI004 零豁免）、<c>AddConstructibleService</c> 注册（守卫工厂）。
/// 异常收敛：非取消异常 catch → FailReason（机器可读），成功返回 Success——端点 handler 按 FailReason 映射 HTTP。</para>
/// <para><b>state 票据</b>：AES-GCM 单段密文（keyed <c>ISymmetricKeyProvider</c> <c>SymmetricKeyProviderKeys.Federation</c>，
/// Oracle 条件 3 域内自洽——Federation 自有密钥，不跨域复用 TrustCenter 密钥）承载
/// <c>app_id|redirect|sub_state</c>，Base64Url 编码（URL-safe）；回调验签解密解 app_id/redirect，
/// 防开放重定向 + 防 state 伪造（机密性 + 完整性，优于 HMAC）。</para>
/// </summary>
[DiContractIgnore]
public sealed class SsoSubAppBridge : DomainServiceBase, ISsoSubAppBridge
{
    private const string TicketSeparator = "|";
    private static readonly TimeSpan TicketTtl = TimeSpan.FromSeconds(120);   // accesscode 短 TTL（对齐 TrustCenter 默认）

    private readonly ILogger<SsoSubAppBridge> _logger;
    private readonly ISymmetricKeyProvider _keyProvider;

    private ISsoClientQueryService? _clients;
    private ISsoChannelFactory? _channels;
    private ISsoLogin? _login;
    private IAccessCodeService? _codes;

    public SsoSubAppBridge(
        IDomainUser user,
        [FromKeyedServices(SymmetricKeyProviderKeys.Federation)] ISymmetricKeyProvider keyProvider,
        ILogger<SsoSubAppBridge> logger)
        : base(user)
    {
        _keyProvider = keyProvider ?? throw new ArgumentNullException(nameof(keyProvider));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    private ISsoClientQueryService Clients => _clients ??= User.Use<ISsoClientQueryService>();
    private ISsoChannelFactory Channels => _channels ??= User.Use<ISsoChannelFactory>();
    private ISsoLogin Login => _login ??= User.Use<ISsoLogin>();
    private IAccessCodeService Codes => _codes ??= User.Use<IAccessCodeService>();

    /// <inheritdoc />
    public async Task<AuthorizeStartResult> StartAsync(AuthorizeStartRequest request, CancellationToken ct = default)
    {
        try
        {
            if (request.Mode != "federated")
                return new AuthorizeStartResult(false, null, null, "INVALID_MODE");

            // ① app_id 存在 + 启用（缺失统一 ORIGIN_NOT_ALLOWED——防 app_id 枚举，Oracle 条件 4 原则）
            var info = await Clients.GetInfoAsync(request.AppId, ct);
            if (info is null || !info.IsEnabled)
                return new AuthorizeStartResult(false, null, null, "ORIGIN_NOT_ALLOWED");

            // ② redirect 白名单（Ordinal 精确 origin 匹配）
            if (!await IsRedirectAllowedAsync(request.AppId, request.Redirect, ct))
                return new AuthorizeStartResult(false, null, null, "ORIGIN_NOT_ALLOWED");

            // ③ 通道选区（null ChannelId → 默认通道降级）
            ISsoChannel? channel = request.ChannelId is null
                ? await Channels.CreateDefaultAsync(ct)
                : await Channels.CreateAsync(request.ChannelId, null, ct);
            if (channel is null)
                return new AuthorizeStartResult(false, null, null, "CHANNEL_NOT_FOUND");

            // ④ state 票据（AES-GCM 密文承载 app_id|redirect|sub_state——回调验签解 app_id/redirect）
            string stateTicket = SignState(request.AppId, request.Redirect, request.State);

            // ⑤ 通道构造授权 URL（凭证自持 Oracle P2-4；redirect_uri = Platform 回调端点）
            var authorize = await channel.BuildAuthorizeUrlAsync(new SsoChannelAuthorizeContext(
                RedirectUri: request.CallbackBaseUrl,
                State: stateTicket,
                Scope: request.Scope,
                Parameters: request.Parameters ?? new Dictionary<string, string?>(StringComparer.Ordinal)),
                ct);
            if (authorize is null)
                return new AuthorizeStartResult(false, null, null, "AUTHORIZE_NOT_SUPPORTED");

            return new AuthorizeStartResult(true, authorize.Url, authorize.State ?? stateTicket, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "authorize/start 编排异常：AppId={AppId}", request.AppId);
            return new AuthorizeStartResult(false, null, null, "CHANNEL_REGISTRY_UNAVAILABLE");
        }
    }

    /// <inheritdoc />
    public async Task<OAuthCallbackResult> CompleteOAuthAsync(string channelId, IReadOnlyDictionary<string, string?> parameters, CancellationToken ct = default)
    {
        try
        {
            // ① state 票据验签（解 app_id/redirect/sub_state——防开放重定向 + 防 state 伪造；缺失/篡改 → STATE_INVALID）
            if (!parameters.TryGetValue("state", out var state) || string.IsNullOrWhiteSpace(state))
                return new OAuthCallbackResult(false, null, null, "STATE_INVALID");

            (string appId, string redirect, string subState) = UnsignState(state);

            // ② 纯通道认证（ISsoLogin——无建号副作用，外部身份不在 Platform 建号）
            var result = await Login.LoginAsync(channelId, new SsoChannelAuthContext(parameters), ct);
            if (!result.Success)
                return new OAuthCallbackResult(false, null, null, result.FailReason ?? "LOGIN_FAILED");

            // ③ 签 accesscode（payload = 外部身份，expectedClaimant=app_id 防他应用冒领）
            string payloadJson = JsonSerializer.Serialize(new IdentityPayload("federated", channelId, result.ExternalUserId, null));
            var code = await Codes.IssueAsync(payloadJson, TicketTtl, appId, ct);

            // ④ 302 跳回子应用（code + 子应用原 state 回传）
            return new OAuthCallbackResult(true, AppendQuery(redirect, new Dictionary<string, string?>
            {
                ["code"] = code.Code,
                ["state"] = subState,
            }), subState, null);
        }
        catch (CryptographicException)
        {
            // state 票据验签失败/格式非法（篡改/过期/伪造）——STATE_INVALID（区别于 registry 故障）
            return new OAuthCallbackResult(false, null, null, "STATE_INVALID");
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "oauth/callback 编排异常：ChannelId={ChannelId}", channelId);
            return new OAuthCallbackResult(false, null, null, "CHANNEL_REGISTRY_UNAVAILABLE");
        }
    }

    /// <inheritdoc />
    public async Task<TrustIssueResult> IssueForPlatformUserAsync(TrustIssueRequest request, string platformUid, CancellationToken ct = default)
    {
        try
        {
            // ① app_id 存在 + 启用（缺失统一 ORIGIN_NOT_ALLOWED——防 app_id 枚举，Oracle 条件 4 原则）
            var info = await Clients.GetInfoAsync(request.AppId, ct);
            if (info is null || !info.IsEnabled)
                return new TrustIssueResult(false, null, null, "ORIGIN_NOT_ALLOWED");

            // ② redirect 白名单
            if (!await IsRedirectAllowedAsync(request.AppId, request.Redirect, ct))
                return new TrustIssueResult(false, null, null, "ORIGIN_NOT_ALLOWED");

            // ③ 签 accesscode（payload = Platform uid，expectedClaimant=app_id）
            string payloadJson = JsonSerializer.Serialize(new IdentityPayload("direct", null, null, platformUid));
            var code = await Codes.IssueAsync(payloadJson, TicketTtl, request.AppId, ct);

            // ④ 302 跳回子应用（code）
            return new TrustIssueResult(true, AppendQuery(request.Redirect, new Dictionary<string, string?>
            {
                ["code"] = code.Code,
                ["state"] = request.State,
            }), code.Code, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "trust/issue 编排异常：AppId={AppId}", request.AppId);
            return new TrustIssueResult(false, null, null, "CHANNEL_REGISTRY_UNAVAILABLE");
        }
    }

    /// <inheritdoc />
    public async Task<IdentityClaimResult> ClaimAsync(IdentityClaimRequest request, CancellationToken ct = default)
    {
        try
        {
            // ① credential 鉴权（统一 AUTHENTICATION_FAILED——Oracle 条件 4 防 app_id 枚举）
            if (!await Clients.ValidateClientCredentialAsync(request.AppId, request.ClientSecret, ct))
                return new IdentityClaimResult(false, null, null, null, null, "AUTHENTICATION_FAILED");

            // ② RedeemAsync 原子取回（CAS 单次 + ExpectedClaimant=app_id 联合判定无 TOCTOU；判因透传）
            var payload = await Codes.RedeemAsync<IdentityPayload>(request.Code, request.AppId, ct);
            if (payload is null)
                return new IdentityClaimResult(false, null, null, null, null, "ACCESS_CODE_NOT_FOUND");

            return new IdentityClaimResult(true, payload.Mode, payload.ChannelId, payload.ExternalUserId, payload.PlatformUid, null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            string? failReason = ExtractTicketFailReason(ex) ?? "CHANNEL_REGISTRY_UNAVAILABLE";
            if (failReason == "CHANNEL_REGISTRY_UNAVAILABLE")
                _logger.LogError(ex, "identity/claim 编排异常：AppId={AppId}", request.AppId);
            return new IdentityClaimResult(false, null, null, null, null, failReason);
        }
    }

    // ─────────────────────────── 内部辅助 ───────────────────────────

    /// <summary>accesscode payload 契约（serialize/deserialize——字段与 IdentityClaimResult 对齐）。</summary>
    private sealed record IdentityPayload(string Mode, string? ChannelId, string? ExternalUserId, string? PlatformUid);

    /// <summary>redirect 白名单校验（origin = scheme + host，Ordinal 精确——防开放重定向）。</summary>
    private async Task<bool> IsRedirectAllowedAsync(string appId, string redirect, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(redirect) || !Uri.TryCreate(redirect, UriKind.Absolute, out var uri))
            return false;
        return await Clients.IsOriginAllowedAsync(appId, $"{uri.Scheme}://{uri.Host}", ct);
    }

    /// <summary>state 票据签发——HMAC-SHA256 签名（Oracle 条件 3，Federation 密钥）承载 <c>app_id|redirect|sub_state</c>。
    /// <para>⚠️ 选型：payload 非机密（app_id/redirect/sub_state 均为非敏感），仅需完整性（防篡改）——HMAC 签名优于 AES-GCM
    /// 加密（更短，兼容<b>支付宝 state base64 ≤100 字符</b>硬约束 N4 §3.2）；签名截断 8 字节（64-bit，state 短 TTL 一次性防篡改够用）。</para>
    /// 格式：<c>base64url(payload) + "." + base64url(HMAC-SHA256(payload)[前 8 字节])</c>。</summary>
    private string SignState(string appId, string redirect, string? subState)
    {
        string payload = $"{appId}{TicketSeparator}{redirect}{TicketSeparator}{subState ?? string.Empty}";
        byte[] key = _keyProvider.GetKey();
        try
        {
            using var hmac = new HMACSHA256(key);
            byte[] signature = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            return ToBase64Url(Encoding.UTF8.GetBytes(payload)) + "." + ToBase64Url(signature.AsSpan(0, 8));
        }
        finally
        {
            Array.Clear(key, 0, key.Length);
        }
    }

    /// <summary>state 票据验签——拆 payload|签名 → HMAC 重算 + <see cref="CryptographicOperations.FixedTimeEquals"/> 比对
    /// → 三字段拆分；失败抛 <see cref="CryptographicException"/>。</summary>
    private (string AppId, string Redirect, string SubState) UnsignState(string state)
    {
        string[] parts = state.Split('.', 2);
        if (parts.Length != 2)
            throw new CryptographicException("state 票据格式非法");

        byte[] key = _keyProvider.GetKey();
        try
        {
            using var hmac = new HMACSHA256(key);
            byte[] expected = hmac.ComputeHash(Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(FromBase64Url(parts[0]))));
            byte[] actual = FromBase64Url(parts[1]);
            if (actual.Length != 8 || !CryptographicOperations.FixedTimeEquals(expected.AsSpan(0, 8), actual))
                throw new CryptographicException("state 票据验签失败");

            string payload = Encoding.UTF8.GetString(FromBase64Url(parts[0]));
            var fields = payload.Split(TicketSeparator, 3);
            if (fields.Length != 3 || string.IsNullOrWhiteSpace(fields[0]) || string.IsNullOrWhiteSpace(fields[1]))
                throw new CryptographicException("state 票据格式非法");
            return (fields[0], fields[1], fields[2]);
        }
        finally
        {
            Array.Clear(key, 0, key.Length);
        }
    }

    /// <summary>跳回 URL 追加 query 参数（保留既有 query；QueryHelpers 处理编码）。</summary>
    private static string AppendQuery(string url, IReadOnlyDictionary<string, string?> query)
        => QueryHelpers.AddQueryString(url, query);

    /// <summary>RedeemAsync 判因异常 → 机器可读原因码（消息含 ACCESS_CODE_NOT_FOUND/TICKET_* 等）；未知异常 null。</summary>
    private static string? ExtractTicketFailReason(Exception ex)
    {
        if (ex.Message.Contains("ACCESS_CODE_NOT_FOUND", StringComparison.Ordinal)) return "ACCESS_CODE_NOT_FOUND";
        if (ex.Message.Contains("TICKET_CONSUMED", StringComparison.Ordinal)) return "TICKET_CONSUMED";
        if (ex.Message.Contains("CLAIMANT_MISMATCH", StringComparison.Ordinal)) return "CLAIMANT_MISMATCH";
        if (ex.Message.Contains("TICKET_EXPIRED", StringComparison.Ordinal)) return "TICKET_EXPIRED";
        if (ex.Message.Contains("TICKET_STATE_MISMATCH", StringComparison.Ordinal)) return "TICKET_STATE_MISMATCH";
        return null;
    }

    private static string ToBase64Url(byte[] data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string ToBase64Url(ReadOnlySpan<byte> data)
        => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string base64Url)
    {
        string s = base64Url.Replace('-', '+').Replace('_', '/');
        switch (s.Length % 4)
        {
            case 2: s += "=="; break;
            case 3: s += "="; break;
        }
        return Convert.FromBase64String(s);
    }
}
