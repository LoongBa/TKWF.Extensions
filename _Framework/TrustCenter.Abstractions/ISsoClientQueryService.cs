using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.TrustCenter;

/// <summary>SSO 客户端（下游 SP 应用）DTO——注册/查询面（2026-10-11 自 TrustCenter 主包迁移，子应用消费方接入 Oracle 条件 6）。</summary>
/// <param name="AppId">目标应用 id。</param>
/// <param name="OriginWhitelist">注册精确 origin（scheme+host）集合。</param>
/// <param name="Scopes">注册范围。</param>
/// <param name="IsEnabled">启用。</param>
public sealed record SsoClientInfo(string AppId, IReadOnlyList<string> OriginWhitelist, IReadOnlyList<string> Scopes, bool IsEnabled);

/// <summary>
/// SSO 客户端只读校验契约（子应用消费方接入 2026-10-11——校验面契约化，Federation 等编排面经 L2 间接层消费，
/// 避免引 TrustCenter 主包违反 TKWF0022 门控）。实现归 TrustCenter 主包（其 ISsoClientService 继承本契约）。
/// </summary>
public interface ISsoClientQueryService : IDomainService
{
    /// <summary>取注册信息（校验 origin 白名单用；404/禁用语义经返回 null + 调用方判定）。</summary>
    Task<SsoClientInfo?> GetInfoAsync(string appId, CancellationToken ct = default);

    /// <summary>校验客户端凭据（server-to-server profile API 鉴权；AES-GCM 解密 + FixedTimeEquals 防时序攻击）。</summary>
    Task<bool> ValidateClientCredentialAsync(string appId, string clientSecret, CancellationToken ct = default);

    /// <summary>校验重定向 origin（防开放重定向——target_app_id → 注册精确 origin，Ordinal 精确匹配）。</summary>
    Task<bool> IsOriginAllowedAsync(string appId, string origin, CancellationToken ct = default);
}
