using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.TrustCenter;

/// <summary>channel 认证上下文——可扩展键值模型（区别于既有 ProviderAuthenticateContext 扁平 record 缺陷）。</summary>
public sealed record SsoChannelAuthContext(IReadOnlyDictionary<string, string?> Parameters);

/// <summary>channel 认证结果。</summary>
/// <param name="Success">是否成功。</param>
/// <param name="ExternalUserId">渠道身份标识（微信 = openid；成功时）。</param>
/// <param name="FailReason">失败原因（机器可读）。</param>
/// <param name="AuthLevel">认证强度（int——1=手机号 / 2=微信便捷 / 3=教师核实，对齐 Abstractions SsoAccountDto；不依赖主包 AuthLevel 枚举）。</param>
public sealed record SsoChannelAuthResult(bool Success, string? ExternalUserId, string? FailReason, int AuthLevel);

/// <summary>
/// SSO channel 契约（IdP 适配器接口——供平台适配扩展 <c>TKWF.Federation.{平台}</c> 实现）。
/// <para>设计文档 §4.1 channel 抽象：channel_type 决定"身份获取流程"（OAuth 代理 / Issue 票据 / 未来其他）；
/// 与既有 <c>IAuthenticationProvider</c> 的边界（Oracle P2-3）：Provider = 认证中心内部认证方式
/// （服务 token1 签发）；本契约 = 联邦 IdP 适配（外部身份源，服务 token2 联邦流）——两套并存。</para>
/// <para>注册：<c>TryAddEnumerableConstructible</c>（集合版守卫工厂，对齐 ADR92）；平台适配扩展 Initializer
/// 追加实现，未注册通道自然跳过。channel 的 IdP 凭证（如微信 AppId/AppSecret）归平台适配扩展自持（Oracle P2-4）。</para>
/// <para><b>契约归属（TrustCenter 剥离 2026-10-09）</b>：本契约随信任内核迁入 TrustCenter.Abstractions——
/// 定义"外部如何接入信任网络"，TrustCenter 经契约感知外部存在，不感知具体平台实现；Federation 编排面
/// （ISsoChannelFactory/SsoLogin）与平台库均经本契约消费。</para>
/// </summary>
public interface ISsoChannel : IDomainService
{
    /// <summary>channel 类型（wechat_oauth / wechat_event / google_oidc / ...）。</summary>
    string ChannelType { get; }

    /// <summary>channel 实例 id（微信 = 公众号 id）。</summary>
    string ChannelId { get; }

    /// <summary>身份获取流程（OAuth 代理 / Issue 票据 / 未来其他）——返回渠道身份标识。</summary>
    Task<SsoChannelAuthResult> AuthenticateAsync(SsoChannelAuthContext context, CancellationToken ct = default);
}
