using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>
/// 需要域用户上下文的通道来源（D03 1.7 审核 P0-1 修复——DB 权威源）。
/// <para><b>为什么需要</b>：DB 源读配置必须经 SG1 生成 DataService，而 DataService 的 DI 注册是
/// throw-factory（<c>[领域架构守卫] 必须经 User.Use&lt;T&gt;() 解析</c>）——普通 DI 直取在生产必抛。
/// <c>User.Use&lt;T&gt;()</c> 的 NoAop 直建又需要 <see cref="IDomainUser"/> 实例（<c>IDomainUser</c> 永不注册 DI），
/// 而 <see cref="IChannelSource"/> 是普通 DI 集合元素（<c>TryAddEnumerable</c>，非守卫工厂），本身拿不到用户。</para>
/// <para><b>绑定时机与语义</b>：<c>CompositeChannelRegistry</c> 继承 <c>DomainServiceBase</c>，其构造由守卫工厂在
/// <c>User.Use&lt;IChannelRegistry&gt;()</c> 帧内完成（<see cref="IDomainUser"/> 已注入）——构造时对本接口实现逐个
/// <see cref="BindDomainUser"/>，来源即持有用户实例；后续 <c>GetChannels()</c>（<c>Use</c> 返回后 AOP 帧已关闭）仍可经
/// <c>user.Use&lt;DataService&gt;()</c> 取数——该调用不依赖 AOP 帧，但依赖<b>域解析作用域存活</b>
/// （与全部 <c>DomainServiceBase</c> 派生门面一致）。</para>
/// <para><b>生命周期约束</b>：实现必须注册为 <c>Scoped</c>（与解析作用域同寿命）。注册为 <c>Singleton</c> 会使
/// 首个绑定用户被进程内所有后续调用复用（跨用户/跨租户泄漏），属装配错误。</para>
/// <para><b>边界</b>：静态源（各平台库 <c>XxxChannelSource</c>）投影自 Options，无需用户上下文——<b>不实现本接口</b>，
/// 零改动。本接口只增语义、不改 <see cref="IChannelSource"/> 契约（避免 8 平台库连带改动与 IDomainService 标记污染）。</para>
/// </summary>
public interface IUserBoundChannelSource
{
    /// <summary>
    /// 绑定当前域用户（由 <c>CompositeChannelRegistry</c> 构造时调用一次；重复绑定以后者为准）。
    /// 实现方应仅持有引用，<b>不得</b>在绑定前访问数据（绑定前数据访问属装配错误，应 fail-closed 降级并留痕）。
    /// </summary>
    void BindDomainUser(IDomainUser user);
}
