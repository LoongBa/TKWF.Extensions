using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.TrustCenter;

/// <summary>
/// SSO 应用注册服务（下游 SP 接入点）。
/// <para>设计文档 §5.4/§6.3：app_id + origin 白名单（防开放重定向）+ scope + client credential（AES-GCM）
/// + per-channel HMAC 密钥（/sso/issue 验签密钥来源，Oracle P1-4——不复用 PlatformCredentialEntity）。</para>
/// <para><b>2026-10-11 子应用消费方接入</b>：只读校验面（GetInfoAsync/ValidateClientCredentialAsync/IsOriginAllowedAsync）
/// 经 <see cref="ISsoClientQueryService"/>（TrustCenter.Abstractions，L2 间接层）对外——本接口继承实现；
/// <see cref="SsoClientInfo"/> DTO 已迁移至 TrustCenter.Abstractions（Oracle 条件 6，同命名空间零 using 变更）。</para>
/// </summary>
public interface ISsoClientService : IDomainService, ISsoClientQueryService
{
    /// <summary>注册应用（生成 app_id + client secret + HMAC secret——均 AES-GCM 落库）。</summary>
    Task<SsoClientInfo> RegisterAsync(IReadOnlyList<string> originWhitelist, IReadOnlyList<string> scopes, CancellationToken ct = default);

    /// <summary>取 HMAC 密钥（明文——/sso/issue 验签用；AES-GCM 解密于 DataService 边界）。</summary>
    Task<string?> GetHmacSecretAsync(string appId, CancellationToken ct = default);
}
