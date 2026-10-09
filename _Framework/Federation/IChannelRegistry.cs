using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道注册表门面（多通道联邦 v0.3.0——替代平台库硬编码 <c>Channels.FirstOrDefault()</c>）。
/// <para>选区内核：<see cref="IChannelRegistry"/> 按 <see cref="ChannelConfig.ChannelId"/> 精确定位通道配置
/// （凭证 + 平台扩展负载），平台库通道只负责"解析自身凭证"不感知选区（方案 §3.4）。两层存储：
/// <see cref="StaticChannelRegistry"/>（静态 Options.Channels 投影——第一迭代立即落地）+
/// 第二迭代 <c>SsoChannelRegistryEntity</c>（DB 动态权威）；<c>CompositeChannelRegistry</c>
/// 承担组合语义（DB 命中优先 → 静态回退）。</para>
/// <para>注册：<c>AddConstructibleService</c>（门面标准形态——消费方经 <c>User.Use&lt;IChannelRegistry&gt;()</c>
/// 帧内解析；实现类 throw-factory）。本接口 <c>: IDomainService</c>——IDomainUser 永不注册 DI，经基类
/// <c>User</c> 取上下文（tkwf-extension §4.3 铁律）。</para>
/// </summary>
public interface IChannelRegistry : IDomainService
{
    /// <summary>按 ChannelId 精确定位通道配置；不存在返回 null（装配层转 CHANNEL_NOT_FOUND）。</summary>
    Task<ChannelConfig?> GetAsync(string channelId, CancellationToken ct = default);

    /// <summary>
    /// 统一解析入口（方案 §3.7 双键）——先 <see cref="ChannelConfig.Alias"/> 精确匹配 → 未中再
    /// <see cref="ChannelConfig.ChannelId"/> 匹配 → 未中 null；key 空白 → null。
    /// <para>用途：对外入口（路由段 /sso/oauth/{channelAlias}/callback 与 {channelId} 并存 / 客户端入参）
    /// 解析一次后归一内部 ChannelId——<b>内部消费一律走 <see cref="GetAsync"/>（ChannelId），本方法不承载选区</b>。
    /// alias 缺省 = ChannelId（存量零迁移：无 alias 通道经本方法仍可按 ChannelId 命中）。</para>
    /// </summary>
    Task<ChannelConfig?> GetByAliasOrIdAsync(string key, CancellationToken ct = default);

    /// <summary>枚举全部通道配置（构建通道集合 / 校验 / 管理面）。</summary>
    Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken ct = default);

    /// <summary>默认通道（无前缀 /sso/oauth/callback 降级选区——单实例向后兼容；多公众号语义边界见方案 §3.2）。</summary>
    Task<ChannelConfig?> GetDefaultAsync(CancellationToken ct = default);
}