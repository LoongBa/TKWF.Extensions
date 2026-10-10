using System;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.TrustCenter;

/// <summary>accesscode 签发请求（安全数据投递通道——业务层选用）。</summary>
/// <param name="ChannelId">channel 实例 id（微信 = 公众号 id）。</param>
/// <param name="UId">平台内部 id（查/建 uid 后）。</param>
/// <param name="TargetAppId">目标应用 id（token2 aud）。</param>
/// <param name="Scope">该应用注册范围（签发 token2 时注入）。</param>
/// <param name="OpenId">渠道 openid（仅审计——不出 token2/URL）。</param>
/// <param name="CodeVerifier">PKCE code_verifier（可选——存 SHA256 hash，Oracle P2-1）。</param>
/// <param name="IpAddress">来源 IP（审计）。</param>
public sealed record AccessCodeIssueRequest(
    string ChannelId, string UId, string TargetAppId, string? Scope,
    string? OpenId = null, string? CodeVerifier = null, string? IpAddress = null);

/// <summary>accesscode 签发结果。</summary>
/// <param name="Code">base64url 32 字节随机（唯一原文——库内只存 SHA256 hash）。</param>
/// <param name="ExpiresIn">有效秒数（默认 120）。</param>
public sealed record AccessCodeIssueResult(string Code, int ExpiresIn);

/// <summary>accesscode 消费结果。</summary>
/// <param name="UId">平台内部 id（token2 sub）。</param>
/// <param name="TargetAppId">目标应用 id（token2 aud）。</param>
/// <param name="Scope">注册范围。</param>
public sealed record AccessCodeConsumeResult(string UId, string TargetAppId, string? Scope);

/// <summary>
/// 授权码服务（accesscode）——安全数据投递通道。
/// <para>设计文档 §6.1 + Oracle P2-1/P2-2：CSPRNG 32 字节 base64url / TTL / 单次原子 CAS /
/// 只存 SHA256(code) 索引 / PKCE 可选（defense in depth）。</para>
/// <para><b>契约归属（TrustCenter 剥离 2026-10-09）</b>：信任内核契约——<c>SsoAccessCodeService</c>
/// 更名为 <see cref="IAccessCodeService"/>（去 Sso 前缀——语义泛化为数据投递，非 SSO 专属，方案 §5.8）。</para>
/// </summary>
public interface IAccessCodeService : IDomainService
{
    /// <summary>签发 accesscode（生成高熵 code + SHA256 落库 + TTL + 可选 PKCE hash）——<b>SSO 联邦流</b>（Channel B 票据，SsoLogin/Federation 编排）。</summary>
    Task<AccessCodeIssueResult> IssueAsync(AccessCodeIssueRequest request, CancellationToken ct = default);

    /// <summary>消费 accesscode（原子 CAS used=false→true；重放/过期/verifier 不匹配 → 抛异常含机器可读原因码）——<b>SSO 联邦流</b>。</summary>
    Task<AccessCodeConsumeResult> ConsumeAsync(string code, string? codeVerifier = null, string? ipAddress = null, CancellationToken ct = default);

    /// <summary>
    /// 签发 accesscode（<b>安全数据投递通道</b>——方案 §5.5）：附带信息 <paramref name="payloadJson"/> +
    /// 可核销人 <paramref name="expectedClaimant"/>。
    /// <para><paramref name="payloadJson"/>：业务层传入的 JSON 字符串（可空 = 纯核销通道，无附带信息）——
    /// 经 keyed <c>ISymmetricKeyProvider</c> AES-GCM 加密落 <c>PayloadEncrypted</c>（明文不落库）；
    /// 容量校验：UTF8 明文 ≤ ~4068 字节（4096-12-16，AES-GCM 开销），超限抛
    /// <c>AuthenticationException("PAYLOAD_TOO_LARGE")</c>。</para>
    /// <para><paramref name="expectedClaimant"/>：可核销人标识（可空 = 可转让，任意核销人可消费）——
    /// 进原子 CAS 条件（无 TOCTOU），消费人 <c>RedeemAsync</c> 不匹配拒绝。</para>
    /// <para><paramref name="ttl"/>：有效时长；ttl ≤ 0 → 钳制为 <c>TrustCenterOptions.AccessCodeExpirationSeconds</c>（默认 120）。</para>
    /// <para>与 <see cref="IssueAsync(AccessCodeIssueRequest, CancellationToken)"/> 重载并存（C# 重载）——
    /// SSO 联邦流继续用 request 重载，本重载服务数据投递场景。</para>
    /// </summary>
    Task<AccessCodeIssueResult> IssueAsync(string? payloadJson, TimeSpan ttl, string? expectedClaimant, CancellationToken ct = default);

    /// <summary>
    /// 预读附带信息（<b>只读快照非锁定</b>——<b>安全数据投递通道</b>）：查行（SHA256(code)）→ 过期拒（TICKET_EXPIRED）→
    /// 有密文解密 + JSON 反序列化为 <typeparamref name="T"/>；无附带信息返回 default。
    /// <para>⚠️ 并发语义（显式声明）：Peek <b>不锁定/不消费</b>——Peek 后行可能被并发 Redeem（行消失/已用）。
    /// 业务层不得基于 Peek 结果做<b>不可逆决策</b>；不可逆动作一律经 <see cref="RedeemAsync{T}"/>（原子 CAS 恰一成功）。</para>
    /// </summary>
    Task<T?> PeekAsync<T>(string code, CancellationToken ct = default);

    /// <summary>
    /// 核销取回附带信息 + 销毁（<b>安全数据投递通道</b>——单次原子 CAS）：查行（SHA256(code)）→ 过期拒（TICKET_EXPIRED）→
    /// <b>原子 CAS 带 ExpectedClaimant 条件</b>（<c>UPDATE ... WHERE used=false AND (ExpectedClaimant IS NULL OR = claimant)</c>）→
    /// 影响 0 → 重查判因：行不存在 → ACCESS_CODE_NOT_FOUND / 已用 → TICKET_CONSUMED / 核销人不符 → CLAIMANT_MISMATCH
    /// （无 TOCTOU——CAS 原子性保证恰一成功）→ 成功 → 解密 payload + JSON 反序列化为 <typeparamref name="T"/> + 销毁（Used=true）。
    /// <para><paramref name="claimant"/> 与签发时 <c>expectedClaimant</c>（IssueAsync(payloadJson,...)）匹配才可核销；
    /// expectedClaimant 为空（可转让）时任何 claimant（含 null）可核销。</para>
    /// </summary>
    Task<T?> RedeemAsync<T>(string code, string? claimant, CancellationToken ct = default);

    /// <summary>
    /// 清理过期 accesscode 行（含 Payload 密文——防业务数据残留）：删除 <c>ExpiresAt &lt; UtcNow - retentionDays</c> 的行。
    /// <para><paramref name="retentionDays"/> 为空 → 取 <c>TrustCenterOptions.AccessCodeRetentionDays</c>（默认 7）；
    /// ≤ 0 → 跳过清理（返回 0）。</para>
    /// <para>⚠️ 调度约定：本方法<b>不内置定时器</b>——消费方经 <c>IRecurringBackgroundJobManager</c> 周期调度
    /// （对齐 Metrics 定时重算范式，BackgroundJobs 清理配置同款语义）。</para>
    /// </summary>
    Task<int> CleanupExpiredAsync(int? retentionDays = null, CancellationToken ct = default);
}
