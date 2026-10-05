using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>SSO accesscode 存储记录（Federation 消费面——CodeHash 已是 SHA256，不存原文）。</summary>
/// <param name="Id">主键（原子 CAS 定位）。</param>
/// <param name="CodeHash">SHA256(code) hex（唯一）。</param>
/// <param name="ChannelId">channel 实例 id。</param>
/// <param name="TargetAppId">目标应用 id（token2 aud）。</param>
/// <param name="UId">平台内部 id。</param>
/// <param name="Scope">注册范围。</param>
/// <param name="ExpiresAt">过期时间（UTC）。</param>
/// <param name="Used">单次消费标记。</param>
/// <param name="IpAddress">来源 IP（审计）。</param>
/// <param name="CodeVerifierHash">PKCE code_verifier SHA256 hash（可选）。</param>
public sealed record SsoAccessCodeStoreRecord(
    long Id, string CodeHash, string ChannelId, string TargetAppId, string UId,
    string? Scope, System.DateTime ExpiresAt, bool Used, string? IpAddress, string? CodeVerifierHash);

/// <summary>SSO accesscode 创建命令（Federation 编排 → Store 实现）。</summary>
/// <param name="CodeHash">SHA256(code) hex。</param>
/// <param name="ChannelId">channel 实例 id。</param>
/// <param name="TargetAppId">目标应用 id。</param>
/// <param name="UId">平台内部 id。</param>
/// <param name="Scope">注册范围。</param>
/// <param name="ExpiresAt">过期时间（UTC）。</param>
/// <param name="IpAddress">来源 IP（审计）。</param>
/// <param name="CodeVerifierHash">PKCE code_verifier SHA256 hash（可选）。</param>
public sealed record SsoAccessCodeCreateCommand(
    string CodeHash, string ChannelId, string TargetAppId, string UId,
    string? Scope, System.DateTime ExpiresAt, string? IpAddress, string? CodeVerifierHash);

/// <summary>
/// SSO accesscode 存储契约（Federation 定义、AuthCenter 实现——ADR-AuthCenter-归层与命名 Oracle P1-1）。
/// <para>承载授权码状态（<c>SsoAccessCodeEntity</c>）的持久化 + 原子消费：<c>TryConsumeAsync</c> 原子 CAS
/// （<c>UPDATE ... WHERE used=false</c>——ADR89 引擎级条件更新）返回影响行数，Federation 编排判重放。</para>
/// <para>实现归 AuthCenter 主包（委托 <c>SsoAccessCodeEntityDataService</c>，红线合规）。</para>
/// <para>注册：<c>AddConstructibleService</c>（门面标准形态，消费方经 <c>User.Use&lt;ISsoAccessCodeStore&gt;()</c> 帧内解析）。</para>
/// </summary>
public interface ISsoAccessCodeStore : IDomainService
{
    /// <summary>按 code hash 取记录；不存在返回 null。</summary>
    Task<SsoAccessCodeStoreRecord?> GetByCodeHashAsync(string codeHash, CancellationToken ct = default);

    /// <summary>创建 accesscode（只存 SHA256 hash，不存原文）。</summary>
    Task<SsoAccessCodeStoreRecord> CreateAsync(SsoAccessCodeCreateCommand command, CancellationToken ct = default);

    /// <summary>原子 CAS 消费（WHERE Id 匹配 AND used=false → set used=true）；返回影响行数（1=独占消费 / 0=已被并发消费）。</summary>
    Task<int> TryConsumeAsync(long id, CancellationToken ct = default);
}