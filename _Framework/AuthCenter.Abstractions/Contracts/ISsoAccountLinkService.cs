using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// SSO 账号关联写契约——认证中心联邦层（TKWF.Ext.SSO / TKWF.Ext.SSO.WeChat）消费面。
/// <para>与 <see cref="ISsoAccountQueryService"/>（只读查询）的边界（SSO.WeChat 开发方案 Oracle 评审 P1-1）：
/// 本契约为<b>写面</b>——承载联盟锚点的写入与反查：
/// <c>SetFederationAnchorAsync</c> 写 <c>AuthAccount.FederationAnchorOpenId</c>（一对一，唯一索引 NULL 放行），
/// <c>GetByFederationAnchorAsync</c> 反查（设计文档 §7"后续任一 openid 直认"链路必需）。
/// 实现类留 Authentication 主包（<c>AuthAccountQueryService</c> 同文件补实现，委托既有 DataService，红线合规）。</para>
/// <para><b>锚点语义（N2 联盟锚点映射策略 Oracle 评审 P1-4 定案——2026-10-06）</b>：
/// 锚点值 = TKWF 自生成的<b>平台无关稳定值</b>（<see cref="SsoAccountAnchor.Create"/>——<c>anc_</c> 前缀 + CSPRNG，
/// 非 uid 本身、非某平台 unionid、非 null）——Federation 编排层在<b>首个渠道绑定</b>时生成并写入；
/// 各平台 <c>(channel_id, external_uid)</c> 经 <c>PlatformAccountMap</c> 归一指向同一 uid，anchor 列 = 加速直认快照。
/// 语义演化兼容：既有微信 unionid 落地数据（如有）按 channel 映射保留，新锚点值另生成重写列。</para>
/// <para>注册：<c>AddConstructibleService</c>（门面标准形态，消费方经 <c>User.Use&lt;ISsoAccountLinkService&gt;()</c> 帧内解析）。</para>
/// </summary>
public interface ISsoAccountLinkService : IDomainService
{
    /// <summary>按联盟锚点值反查账号（SSO 消费面 DTO）；不存在返回 null。</summary>
    Task<SsoAccountDto?> GetByFederationAnchorAsync(string federationAnchorOpenId, CancellationToken ct = default);

    /// <summary>写/更新联盟锚点（<c>AuthAccount.FederationAnchorOpenId</c> 一对一；唯一索引冲突抛异常）；返回更新后 DTO。
    /// <para>入参 <paramref name="federationAnchorOpenId"/> 应为 <see cref="SsoAccountAnchor.Create"/> 生成的平台无关值
    /// （N2 定案——非某平台 unionid/非 uid 本身）。</para></summary>
    Task<SsoAccountDto> SetFederationAnchorAsync(string uid, string federationAnchorOpenId, CancellationToken ct = default);
}