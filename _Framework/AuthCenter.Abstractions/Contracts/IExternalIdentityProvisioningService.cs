using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.Federation;

namespace TKWF.Ext.AuthCenter;

/// <summary>
/// 外部身份 L2 预置 + 档案合并门面（D03 1.4 §6.1/§6.2 定稿）——事件路径专用。
/// <para>与登录路径 <see cref="IFederatedLoginOrchestrator"/> 共享映射/建号内核（<c>PlatformAccountMapService</c> 映射 +
/// AuthFactor 唯一约束兜底），不新造。契约依赖 FA 的 <see cref="FederatedTicketPayload"/>（事件路径吃免登票据载荷——
/// AuthCenter.Abstractions → Federation.Abstractions 契约包间引用，对齐 Navigation.Abstractions 先例）。</para>
/// <para>注册：<c>AddConstructibleService&lt;IExternalIdentityProvisioningService, ExternalIdentityProvisioningService&gt;()</c>——
/// 门面标准形态（接口 : IDomainService，实现继承 DomainServiceBase，消费方帧内 <c>User.Use&lt;T&gt;()</c>）。</para>
/// </summary>
public interface IExternalIdentityProvisioningService : IDomainService
{
    /// <summary>
    /// 事件路径 L2 预置（D03 A11——免登录落地兑付触发）：外部身份 → 分配/复用平台 uid。
    /// <para>编排（与登录路径同内核）：payload.PlatformUid 非空直读复用 → <c>IExternalIdentityService.GetByExternalIdAsync</c>
    /// (ChannelId, ExternalUserId) 映射复用 → 无 → AutoProvision 门控建号 + LinkAsync（AuthFactor 唯一约束冲突
    /// catch → 回滚空军账号 → 回读胜者——并发首登幂等）。</para>
    /// </summary>
    Task<ProvisionedIdentity> ProvisionAsync(FederatedTicketPayload payload, CancellationToken ct = default);

    /// <summary>
    /// 档案合并（D03 §5.2 策略）——拿到一次记一次，按来源优先级合并写入 <c>UserProfileEntity</c>。
    /// <para>优先级：user_filled &gt; merchant_set(仅L3) &gt; oauth_userinfo &gt; event；
    /// 规则：补空/升级（低→高填 null 字段）；同源刷新以新为准；<b>绝不覆盖</b>——user_filled 不被 oauth 默认值覆盖、
    /// "微信用户"默认昵称不覆盖真实值。<paramref name="source"/> 传 <see cref="ProfileSource"/> 字符串常量
    /// （user_filled/oauth_userinfo/event/merchant_set——参数类型 string，常量类仅供取值）。</para>
    /// </summary>
    Task MergeProfileAsync(string platformUid, ExternalProfile profile, string source, CancellationToken ct = default);
}

/// <summary>L2 预置结果（平台身份视图）。</summary>
/// <param name="PlatformUid">平台内部 id（token2 <c>sub</c>）。</param>
/// <param name="IsNew">是否本调用新建（false = 复用既有账号/映射）。</param>
public sealed record ProvisionedIdentity(string PlatformUid, bool IsNew);