using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// SSO 账号查询服务——认证中心联邦层（TKWF.Ext.SSO）消费面契约。
/// <para>与既有 <c>IAuthAccountQueryService</c>（认证中心主包）的边界（ADR-SSO Oracle P1-2）：
/// 既有契约返回完整 <c>AuthAccountEntity</c>（含 TokenVersion/PasswordHash/IsEnabled，服务认证中心内部
/// 与装配实例）；本契约返回 <b>不可变 DTO</b>（<see cref="SsoAccountDto"/>，只含 SSO 消费字段——
/// 敏感字段不出契约包）。实现类留 Authentication 主包（委托既有 DataService，红线合规）。</para>
/// <para>注册：<c>AddConstructibleService</c>（门面标准形态，消费方经 <c>User.Use&lt;ISsoAccountQueryService&gt;()</c> 帧内解析）。</para>
/// </summary>
public interface ISsoAccountQueryService : IDomainService
{
    /// <summary>按平台内部 id 取 SSO 消费面账号档案；不存在返回 null。</summary>
    Task<SsoAccountDto?> GetByUIdAsync(string uid, CancellationToken ct = default);
}
