using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using TKW.Framework.CodeGeneration;
using TKW.Framework.Domain;
using TKW.Framework.Domain.Interfaces;

namespace TKWF.Ext.Federation;

/// <summary>
/// 通道注册表组合门面（多通道联邦 v0.3.0——运行时唯一暴露实现，Oracle M3 修订）。
/// <para>组合语义：**DB 命中优先 → 静态回退**（用户裁定读取语义）；Phase 1 内部仅 <see cref="StaticChannelRegistry"/>
/// （DB 未落地——第二迭代 <c>SsoChannelRegistryEntity</c> 注入后组合自动生效，<b>零返工</b>，Oracle S2 预留）。</para>
/// <para>职责边界：只做"组合"不读数据——<see cref="StaticChannelRegistry"/> 读静态 Options（<b>普通 DI 服务</b>，
/// 无 IDomainUser 依赖，经 <see cref="IServiceProvider"/> C1 解析——不走 <c>User.Use</c>：NoAop 路径会把当前 user
/// 作为额外构造参数传入，仅适用于 DomainServiceBase 派生类）；第二迭代 <c>DbChannelRegistry</c> 读 DB
/// （纯净无回退依赖）；跨实现解耦归本组合单点。本实现继承 <see cref="DomainServiceBase"/>
/// （IChannelRegistry : IDomainService——tkwf-extension §4.3 铁律）。</para>
/// <para>注册：<c>AddConstructibleService&lt;IChannelRegistry, CompositeChannelRegistry&gt;()</c> 经
/// FederationInitializer——消费方统一 <c>User.Use&lt;IChannelRegistry&gt;()</c> 解析（门面标准形态）。</para>
/// </summary>
[DiContractIgnore]
public sealed class CompositeChannelRegistry : DomainServiceBase, IChannelRegistry
{
    private readonly IServiceProvider _sp;

    /// <summary>构造——IDomainUser 帧内供给；StaticChannelRegistry 经 IServiceProvider C1 解析（普通 DI 服务）。</summary>
    public CompositeChannelRegistry(IDomainUser user, IServiceProvider sp) : base(user)
    {
        _sp = sp ?? throw new ArgumentNullException(nameof(sp));
    }

    /// <inheritdoc />
    public Task<ChannelConfig?> GetAsync(string channelId, CancellationToken ct = default)
        => Static.GetAsync(channelId, ct);   // Phase 1：仅静态（DB 分支第二迭代追加）

    /// <inheritdoc />
    public Task<IReadOnlyList<ChannelConfig>> GetAllAsync(CancellationToken ct = default)
        => Static.GetAllAsync(ct);

    /// <inheritdoc />
    public Task<ChannelConfig?> GetDefaultAsync(CancellationToken ct = default)
        => Static.GetDefaultAsync(ct);

    private StaticChannelRegistry Static => _sp.GetRequiredService<StaticChannelRegistry>();
}