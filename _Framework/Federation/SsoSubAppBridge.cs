using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;
using TKW.Framework.Domain.KeyManagement;
using TKW.Framework.Domain.Ticket;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 子应用消费方桥接门面实现（子应用消费方接入 2026-10-11——仅共享 Federation 模式核心编排）。
/// <para>对齐 <see cref="SsoLogin"/> 门面范式：继承 <see cref="DomainServiceBase"/>（经基类 <c>User</c> 取上下文）、
/// 内部经 <c>User.Use&lt;契约&gt;()</c> 懒加载（DI004 零豁免）、<c>AddConstructibleService</c> 注册（守卫工厂）。
/// 异常收敛：非取消异常 catch → FailReason（机器可读），成功返回 Success——端点 handler 按 FailReason 映射 HTTP。</para>
/// <para><b>state 票据</b>：HMAC-SHA256 签名（keyed <c>ISymmetricKeyProvider</c> <c>SymmetricKeyProviderKeys.Federation</c>，
/// Oracle 条件 3 域内自洽——Federation 自有密钥，不跨域复用 TrustCenter 密钥）承载
/// <c>app_id|redirect|sub_state</c>，格式 <c>base64url(payload) + "." + base64url(签名前 8 字节)</c>（URL-safe）；
/// 回调验签解 app_id/redirect，防开放重定向 + 防 state 伪造。⚠️ 选型：payload 非机密（仅需完整性防篡改）——
/// HMAC 优于 AES-GCM（更短，兼容支付宝 state ≤100 字符硬约束 N4 §3.2）；签名截断 8 字节（64-bit，state 短 TTL
/// 一次性防篡改够用），详见 <see cref="SignState"/>。</para>
/// </summary>
[DiContractIgnore]
public sealed class SsoSubAppBridge : DomainServiceBase, ISsoSubAppBridge
{
    private const string TicketSeparator = "|";
    private static readonly TimeSpan TicketTtl = TimeSpan.FromSeconds(120);   // 密封票据短 TTL（对齐 TrustCenter 默认）

    private readonly ILogger<SsoSubAppBridge> _logger;
    private readonly ISymmetricKeyProvider _keyProvider;

    private ISsoClientQueryService? _clients;
    private ISsoChannelFactory? _channels;
    private ISsoLogin? _login;
    private ISealedTicketService<FederatedIdentity>? _ticket;

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
    private ISealedTicketService<FederatedIdentity> Ticket => _ticket ??= User.Use<ISealedTicketService<FederatedIdentity>>();
    private IChannelRegistry Registry => _registry ??= User.Use<IChannelRegistry>();

    private IChannelRegistry? _registry;

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

            // ③ 通道选区（null ChannelId → 默认通道降级；B 守卫——多通道活跃数 >1 → CHANNEL_REQUIRED，
            //    防 GetDefaultAsync IsDefault→首项 非确定性静默选区，Oracle 评审条件 1；单通道降级无歧义）
            if (request.ChannelId is null && (await Registry.GetAllAsync(ct)).Count(c => c.IsEnabled) > 1)
                return new AuthorizeStartResult(false, null, null, "CHANNEL_REQUIRED");

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

            // ③ 签密封票据（面① ADR109 D4——FederatedIdentity 载荷：mode=federated 进 Extensions，
            //    expectedClaimant=app_id 防他应用冒领；UId 空——子应用自行映射 external_user_id）
            var identity = new FederatedIdentity(
                UId: string.Empty,
                ChannelId: channelId,
                ExternalUserId: result.ExternalUserId ?? string.Empty,
                ChannelType: null,
                AuthLevel: result.AuthLevel,
                Extensions: new Dictionary<string, string?> { ["mode"] = "federated" });
            var issued = await Ticket.IssueAsync(
                identity, TicketTtl, new SealedTicketOptions(ExpectedClaimant: appId), ct);

            // ④ 302 跳回子应用（ticket + 子应用原 state 回传）
            return new OAuthCallbackResult(true, AppendQuery(redirect, new Dictionary<string, string?>
            {
                ["code"] = issued.Ticket,
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

            // ③ 签密封票据（面①——FederatedIdentity 载荷：mode=direct，UId=platformUid 承载，
            //    expectedClaimant=app_id）
            var identity = new FederatedIdentity(
                UId: platformUid ?? string.Empty,
                ChannelId: string.Empty,
                ExternalUserId: string.Empty,
                ChannelType: null,
                AuthLevel: 2,
                Extensions: new Dictionary<string, string?> { ["mode"] = "direct" });
            var issued = await Ticket.IssueAsync(
                identity, TicketTtl, new SealedTicketOptions(ExpectedClaimant: request.AppId), ct);

            // ④ 302 跳回子应用（ticket）
            return new TrustIssueResult(true, AppendQuery(request.Redirect, new Dictionary<string, string?>
            {
                ["code"] = issued.Ticket,
                ["state"] = request.State,
            }), issued.Ticket, null);
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

            // ② RedeemAsync 原子取回（CAS 单次 + TicketRedeemContext.ExpectedClaimant=app_id 联合判定无 TOCTOU；
            //    密封票据契约语义——失败/过期/已用/核销人不符返回 null，不泄露判因码）
            var identity = await Ticket.RedeemAsync(
                request.Code, new TicketRedeemContext(ExpectedClaimant: request.AppId), ct);
            if (identity is null)
                return new IdentityClaimResult(false, null, null, null, null, "ACCESS_CODE_NOT_FOUND");

            // ③ 解码 FederatedIdentity → 子应用身份载荷（mode 经 Extensions 承载——拆分命名面① 往返保真）
            string mode = (identity.Extensions?.TryGetValue("mode", out var m) == true && !string.IsNullOrEmpty(m))
                ? m!
                : (string.IsNullOrEmpty(identity.UId) ? "federated" : "direct");
            return new IdentityClaimResult(
                true,
                mode,
                NullIfEmpty(identity.ChannelId),
                NullIfEmpty(identity.ExternalUserId),
                string.Equals(mode, "direct", StringComparison.Ordinal) ? identity.UId : null,
                null);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "identity/claim 编排异常：AppId={AppId}", request.AppId);
            return new IdentityClaimResult(false, null, null, null, null, "CHANNEL_REGISTRY_UNAVAILABLE");
        }
    }

    // ─────────────────────────── 内部辅助 ───────────────────────────

    /// <summary>空字符串归一为 null（FederatedIdentity 非空字符串字段 → 子应用载荷可空语义）。</summary>
    private static string? NullIfEmpty(string? value)
        => string.IsNullOrEmpty(value) ? null : value;

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
