using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;
using TKWF.Ext.TrustCenter;

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道工厂门面（多通道联邦 v0.3.0——按需构造通道实例，Oracle M8 定案）。
/// <para>语义：按 <see cref="ChannelConfig.ChannelId"/> 精确定位 + 按 <paramref name="channelType"/>
/// 构造目标通道实例（凭证运行时经 <see cref="IChannelRegistry"/> 懒加载，通道不再单例枚举）。</para>
/// <para>注册：<c>AddConstructibleService&lt;ISsoChannelFactory, SsoChannelFactory&gt;()</c>——Singleton
/// 守卫工厂形态；<c>CreateAsync</c> 须在 <b>AOP 帧内</b>（门面 <c>ISsoLogin</c> 调用链）——帧内经
/// <c>CurrentAopUser</c> 供给 IDomainUser，帧外构造（启动预热/后台任务）无帧 → 抛守卫 = 正确 fail
/// （Oracle M8；测试覆盖帧内/帧外双路径）。</para>
/// </summary>
public interface ISsoChannelFactory : IDomainService
{
    /// <summary>
    /// 按 ChannelId 构造指定类型通道实例。
    /// </summary>
    /// <param name="channelId">通道实例键（registry 选区）。</param>
    /// <param name="channelType">目标通道类型（wechat_oauth / wechat_event / ...）；<c>null</c> → 自动推导 OAuth 通道
    /// （按 ChannelConfig.PlatformType 匹配集合内 ChannelType，优先 *_oauth/*_oidc——登录场景）。</param>
    /// <returns>构造的通道实例；channelId 不存在 / 平台库未注册对应类型 → null（装配层转映射错误）。</returns>
    Task<ISsoChannel?> CreateAsync(string channelId, string? channelType = null, CancellationToken ct = default);

    /// <summary>构造默认通道实例（单通道降级——registry 活跃通道数 ==1 时唯一通道；多通道须显式 channelId，
    /// B 守卫 CHANNEL_REQUIRED 由消费者层承担——Oracle 评审条件 1）。</summary>
    Task<ISsoChannel?> CreateDefaultAsync(CancellationToken ct = default);
}