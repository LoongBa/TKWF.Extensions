using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>SSO 客户端（下游 SP 应用）DTO——注册面。</summary>
/// <param name="AppId">目标应用 id。</param>
/// <param name="OriginWhitelist">注册精确 origin（scheme+host）集合。</param>
/// <param name="Scopes">注册范围。</param>
/// <param name="IsEnabled">启用。</param>
public sealed record SsoClientInfo(string AppId, IReadOnlyList<string> OriginWhitelist, IReadOnlyList<string> Scopes, bool IsEnabled);

/// <summary>
/// SSO 应用注册服务（下游 SP 接入点）。
/// <para>设计文档 §5.4/§6.3：app_id + origin 白名单（防开放重定向）+ scope + client credential（AES-GCM）
/// + per-channel HMAC 密钥（/sso/issue 验签密钥来源，Oracle P1-4——不复用 PlatformCredentialEntity）。</para>
/// </summary>
public interface ISsoClientService : IDomainService
{
    /// <summary>注册应用（生成 app_id + client secret + HMAC secret——均 AES-GCM 落库）。</summary>
    Task<SsoClientInfo> RegisterAsync(IReadOnlyList<string> originWhitelist, IReadOnlyList<string> scopes, CancellationToken ct = default);

    /// <summary>取注册信息（校验 origin 白名单用；404/禁用语义经返回 null + 调用方判定）。</summary>
    Task<SsoClientInfo?> GetInfoAsync(string appId, CancellationToken ct = default);

    /// <summary>校验客户端凭据（server-to-server profile API 鉴权）。</summary>
    Task<bool> ValidateClientCredentialAsync(string appId, string clientSecret, CancellationToken ct = default);

    /// <summary>取 HMAC 密钥（明文——/sso/issue 验签用；AES-GCM 解密于 DataService 边界）。</summary>
    Task<string?> GetHmacSecretAsync(string appId, CancellationToken ct = default);

    /// <summary>校验重定向 origin（防开放重定向——target_app_id → 注册精确 origin，设计文档 §8.4）。</summary>
    Task<bool> IsOriginAllowedAsync(string appId, string origin, CancellationToken ct = default);
}
