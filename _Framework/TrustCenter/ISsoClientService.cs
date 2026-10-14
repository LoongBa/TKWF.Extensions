using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.TrustCenter;

/// <summary>
/// SSO 应用注册结果（OAuth 动态注册惯例 RFC 7591 §4.3——注册响应一次性携带明文 client_secret；
/// 调用方须立即安全保存，服务端只存 AES-GCM 密文不再恢复明文）。
/// <para><b>2026-10-14 SsoClient 注册契约缺口修复</b>：原 <see cref="SsoClientService.RegisterAsync"/> 仅返回
/// <see cref="SsoClientInfo"/>（无 secret）——动态注册的子应用永远拿不到 client_secret，兑现身份（identity/claim
/// → ValidateClientCredentialAsync）必 401。本 record 为注册专属响应（查询面 <see cref="SsoClientInfo"/>
/// 保持脱敏不变，杜绝"同名 DTO 一处明文一处脱敏"语义分裂）。</para>
/// </summary>
/// <param name="AppId">应用 ID（token2 aud / 端点选区依据）。</param>
/// <param name="ClientSecret">客户端凭据明文（**仅本次注册响应返回一次**，服务端不再提供恢复接口）。</param>
/// <param name="Info">注册信息快照（脱敏——含 origin 白名单/scopes/IsEnabled，不含任何密钥）。</param>
public sealed record SsoClientRegistrationResult(string AppId, string ClientSecret, SsoClientInfo Info);

/// <summary>
/// SSO 应用注册服务（下游 SP 接入点）。
/// <para>设计文档 §5.4/§6.3：app_id + origin 白名单（防开放重定向）+ scope + client credential（AES-GCM）
/// + per-channel HMAC 密钥（/sso/issue 验签密钥来源，Oracle P1-4——不复用 PlatformCredentialEntity）。</para>
/// <para><b>2026-10-11 子应用消费方接入</b>：只读校验面（GetInfoAsync/ValidateClientCredentialAsync/IsOriginAllowedAsync）
/// 经 <see cref="ISsoClientQueryService"/>（TrustCenter.Abstractions，L2 间接层）对外——本接口继承实现；
/// <see cref="SsoClientInfo"/> DTO 已迁移至 TrustCenter.Abstractions（Oracle 条件 6，同命名空间零 using 变更）。</para>
/// <para><b>2026-10-14 SsoClient 注册契约缺口修复（方案①）</b>：<see cref="RegisterAsync"/> 返回类型
/// <see cref="SsoClientInfo"/> → <see cref="SsoClientRegistrationResult"/>（含一次性明文 ClientSecret）——
/// 对齐 OAuth 动态注册惯例；契约包 <see cref="ISsoClientQueryService"/>/<see cref="SsoClientInfo"/> 不变
/// （L2 消费面零破坏）；查询面不提供 client_secret 恢复（安全面最小化）。</para>
/// </summary>
public interface ISsoClientService : IDomainService, ISsoClientQueryService
{
    /// <summary>注册应用（生成 app_id + client secret + HMAC secret——均 AES-GCM 落库；
    /// 返回 <see cref="SsoClientRegistrationResult"/>——ClientSecret 明文仅注册响应返回一次）。</summary>
    Task<SsoClientRegistrationResult> RegisterAsync(IReadOnlyList<string> originWhitelist, IReadOnlyList<string> scopes, CancellationToken ct = default);

    /// <summary>取 HMAC 密钥（明文——/sso/issue 验签用；AES-GCM 解密于 DataService 边界）。</summary>
    Task<string?> GetHmacSecretAsync(string appId, CancellationToken ct = default);
}
