using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>SSO client 存储记录（Federation 消费面——不含密文，敏感字段不流出 Store 边界）。</summary>
/// <param name="AppId">目标应用 id。</param>
/// <param name="OriginWhitelist">注册精确 origin（scheme+host）集合。</param>
/// <param name="Scopes">注册范围。</param>
/// <param name="IsEnabled">启用。</param>
public sealed record SsoClientStoreRecord(string AppId, IReadOnlyList<string> OriginWhitelist, IReadOnlyList<string> Scopes, bool IsEnabled);

/// <summary>SSO client 创建命令（Federation 编排 → Store 实现；明文 secret 由 Store 边界 AES-GCM 加密落库）。</summary>
/// <param name="AppId">目标应用 id。</param>
/// <param name="OriginWhitelist">注册精确 origin 集合。</param>
/// <param name="Scopes">注册范围。</param>
/// <param name="ClientSecret">明文 client credential（Store 内部加密，明文不出 Store）。</param>
/// <param name="HmacSecret">明文 per-channel HMAC 密钥（Store 内部加密）。</param>
public sealed record SsoClientCreateCommand(
    string AppId, IReadOnlyList<string> OriginWhitelist, IReadOnlyList<string> Scopes,
    string ClientSecret, string HmacSecret);

/// <summary>
/// SSO client 存储契约（Federation 定义、AuthCenter 实现——ADR-AuthCenter-归层与命名 Oracle P1-1）。
/// <para>承载 client 注册表（<c>SsoClientEntity</c>）的持久化 + 加密边界：Federation 编排生成明文 secret
/// → Store 实现 AES-GCM 加密落库（密文不出契约）；校验/解密在 Store 边界（内部 FixedTimeEquals 比对）。</para>
/// <para>实现归 AuthCenter 主包（委托 <c>SsoClientEntityDataService</c>，红线合规）。</para>
/// <para>注册：<c>AddConstructibleService</c>（门面标准形态，消费方经 <c>User.Use&lt;ISsoClientStore&gt;()</c> 帧内解析）。</para>
/// </summary>
public interface ISsoClientStore : IDomainService
{
    /// <summary>按 app_id 取注册记录；不存在返回 null。</summary>
    Task<SsoClientStoreRecord?> GetByAppIdAsync(string appId, CancellationToken ct = default);

    /// <summary>创建 client（明文 secret 内部 AES-GCM 加密落库）；返回记录。</summary>
    Task<SsoClientStoreRecord> CreateAsync(SsoClientCreateCommand command, CancellationToken ct = default);

    /// <summary>校验 client credential（内部解密 + FixedTimeEquals 恒定时间比对；防时序攻击）。</summary>
    Task<bool> ValidateCredentialAsync(string appId, string clientSecret, CancellationToken ct = default);

    /// <summary>取 HMAC 密钥明文（Federation 编排 /sso/issue 验签用；Store 边界解密）。</summary>
    Task<string?> GetHmacSecretAsync(string appId, CancellationToken ct = default);

    /// <summary>校验重定向 origin（防开放重定向——注册精确 origin，Ordinal 匹配）。</summary>
    Task<bool> IsOriginAllowedAsync(string appId, string origin, CancellationToken ct = default);
}