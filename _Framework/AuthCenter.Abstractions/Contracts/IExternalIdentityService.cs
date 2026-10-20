using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部身份统一契约（V0.11.0 Iteration 1——方案 §4.10 D6）——<c>(channelId, externalUserId) ↔ uid</c>
/// 联邦身份映射门面；合并 <c>ISsoChannelMapService</c> + <c>ISsoAccountLinkService</c> 两旧契约的消费面
/// （旧契约随 T4 契约合并同批退役）。实现归 AuthCenter 主包 <c>PlatformAccountMapService</c>——四方法委托
/// <c>IAuthFactorService</c>（因素行 = 绑定关系唯一权威）；DTO <see cref="ExternalIdentityDto"/> 不承载任何凭据字段。
/// </summary>
public interface IExternalIdentityService : IDomainService
{
    /// <summary>建立/更新联邦映射（upsert：<c>(channelId, externalUserId)</c> 存在则幂等返回既有，否则新建；唯一冲突 catch-重查硬化）。</summary>
    Task<ExternalIdentityDto> LinkAsync(string uid, string channelId, string externalUserId, string? operatorName = null, CancellationToken ct = default);

    /// <summary>解绑联邦映射（物理删除；不存在幂等返回）。</summary>
    Task UnlinkAsync(string uid, string channelId, string externalUserId, string? operatorName = null, CancellationToken ct = default);

    /// <summary>按 channel + 外部用户 id 查映射账号；不存在返回 null。</summary>
    Task<ExternalIdentityDto?> GetByExternalIdAsync(string channelId, string externalUserId, CancellationToken ct = default);

    /// <summary>按 uid 查全部联邦映射（Federated 投影——ChannelId 非空行）。</summary>
    Task<IReadOnlyList<ExternalIdentityDto>> GetByAccountAsync(string uid, CancellationToken ct = default);
}
