using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>外部 IdP 借道验证契约（三层边界 2026-10-09）——AuthCenter 内网经此契约借道 Federation 连接层完成外部身份认证；未装配 Federation（实现未注册）→ User.Use 抛守卫（fail-hard，不静默降级）。实现归 Federation（ExternalIdpAuthenticator，委托平台库通道）。</summary>
public interface IExternalIdpAuthenticator : IDomainService
{
    Task<ExternalIdpAuthResult> AuthenticateAsync(string channelType, IReadOnlyDictionary<string, string?> parameters, CancellationToken ct = default);
}

/// <summary>外部 IdP 认证结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="ExternalUserId">渠道身份标识（微信 = openid；成功时）。</param>
/// <param name="FailReason">失败原因（机器可读）。</param>
/// <param name="AuthLevel">认证强度（int——1=手机号 / 2=联邦快捷）。</param>
public sealed record ExternalIdpAuthResult(bool Success, string? ExternalUserId, string? FailReason, int AuthLevel);
