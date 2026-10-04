using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Authentication;

/// <summary>
/// SSO 渠道身份映射服务——认证中心联邦层（TKWF.Ext.SSO）消费面契约。
/// <para>承载 <c>(channel_id, external_user_id) → uid</c> 联邦身份映射（设计文档 §4.1/§7 联盟锚点一对多）；
/// 与既有 <c>IPlatformAccountMapService</c>（认证中心主包）的边界（ADR-SSO Oracle P1-2）：
/// 既有契约返回完整 <c>PlatformAccountMapEntity</c>（业务 app 映射，服务既有消费方）；
/// 本契约返回 <b>不可变 DTO</b>（<see cref="SsoChannelMapDto"/>）——channel 联邦映射专用，返回 DTO 不出实体形态。
/// 实现类留 Authentication 主包（委托既有 DataService，红线合规）。</para>
/// <para>注册：<c>AddConstructibleService</c>（门面标准形态，消费方经 <c>User.Use&lt;ISsoChannelMapService&gt;()</c> 帧内解析）。</para>
/// </summary>
public interface ISsoChannelMapService : IDomainService
{
    /// <summary>按 channel + 渠道身份标识查联邦映射；不存在返回 null。</summary>
    Task<SsoChannelMapDto?> GetByChannelAsync(string channelId, string externalUserId, CancellationToken ct = default);

    /// <summary>建立/更新联邦映射（upsert：<c>(channelId, externalUserId)</c> 存在则返回既有，不存在则建）。</summary>
    Task<SsoChannelMapDto> LinkAsync(string uid, string channelId, string externalUserId, CancellationToken ct = default);
}
