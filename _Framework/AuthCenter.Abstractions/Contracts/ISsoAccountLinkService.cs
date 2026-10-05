using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// SSO 账号关联写契约——认证中心联邦层（TKWF.Ext.SSO / TKWF.Ext.SSO.WeChat）消费面。
/// <para>与 <see cref="ISsoAccountQueryService"/>（只读查询）的边界（SSO.WeChat 开发方案 Oracle 评审 P1-1）：
/// 本契约为<b>写面</b>——承载联盟锚点（自建 unionid）的写入与反查：
/// <c>SetFederationAnchorAsync</c> 写 <c>AuthAccount.FederationAnchorOpenId</c>（一对一，唯一索引 NULL 放行），
/// <c>GetByFederationAnchorAsync</c> 反查（设计文档 §7"后续任一 openid 直认"链路必需）。
/// 实现类留 Authentication 主包（<c>AuthAccountQueryService</c> 同文件补实现，委托既有 DataService，红线合规）。</para>
/// <para>注册：<c>AddConstructibleService</c>（门面标准形态，消费方经 <c>User.Use&lt;ISsoAccountLinkService&gt;()</c> 帧内解析）。</para>
/// </summary>
public interface ISsoAccountLinkService : IDomainService
{
    /// <summary>按联盟锚点 openid 反查账号（SSO 消费面 DTO）；不存在返回 null。</summary>
    Task<SsoAccountDto?> GetByFederationAnchorAsync(string federationAnchorOpenId, CancellationToken ct = default);

    /// <summary>写/更新联盟锚点（<c>AuthAccount.FederationAnchorOpenId</c> 一对一；唯一索引冲突抛异常）；返回更新后 DTO。</summary>
    Task<SsoAccountDto> SetFederationAnchorAsync(string uid, string federationAnchorOpenId, CancellationToken ct = default);
}